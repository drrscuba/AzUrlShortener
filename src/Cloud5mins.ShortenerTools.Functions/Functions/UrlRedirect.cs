using Azure;
using Azure.Core;
using Cloud5mins.ShortenerTools.Core.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cloud5mins.ShortenerTools.Functions
{
    public class UrlRedirect
    {

        private const string OpenGraphImageTagTemplate = @"<meta property=""og:image"" content=""$imageUrl"">";
        private const string OpenGraphVideoTagTemplate = @"<meta property=""og:video"" content=""$videoUrl"">";
        private const string OpenGraphDocTemplate = @"
<!DOCTYPE html>
<html lang=""en-US"" >
<head>
    <meta charset=""utf-8"">
    <title>$title</title>
    <meta property=""og:title"" content=""$title"" >
    <meta property=""og:description"" content=""$description"" >
    <meta property=""og:site_name"" content=""$site_name"" >
    <meta property=""og:type"" content=""$type"" >
    <meta property=""og:url"" content=""$shortUrl"" >
    $ogImageTag
    $ogVideoTag
    <!-- meta http-equiv=""refresh"" content=""0;url=$redirectUrl"" -->
    <Style>
    .center-screen {
      display: flex;
      justify-content: center;
      align-items: center;
      text-align: center;
      min-height: 100vh;
    }
    </Style>
</head>
<body>
    <div class=""center-screen"">
        <p>You are being redirected to $title.<br/>If you are not redirected in 10 seconds click <a href=""$redirectUrl"">here</a></p>
    </div>
</body>
</html>
";

        /*
        $FirstImageTag
        <h1>$title</h1>
        <p>$description</p>
        */

        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;

        private readonly string[] SocialMediaBotUserAgentStarts = new[]
        {
            "facebookexternalhit/",
            "facebot",
            "facebookcatalog",
            "linkedinbot",
            "twitterbot",
            "slackbot-linkexpanding"
        };
        
        private readonly string[] SocialMediaBotUserAgentContains = new[]
        {
            "facebookbot/",
            "bluesky cardyb/",
            "discordbot/",
            "opengraph.io/"
        };

        public UrlRedirect(ILoggerFactory loggerFactory, ShortenerSettings settings)
        {
            _logger = loggerFactory.CreateLogger<UrlRedirect>();
            _settings = settings;
        }

        [Function("UrlRedirect")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{shortUrl}")]
            HttpRequestData req,
            string shortUrl,
            ExecutionContext context)
        {            
            string redirectUrl = _settings.DefaultRedirectUrl ?? "https://azure.com";

            if (shortUrl == Utility.ROBOTS)
            {
                _logger.LogInformation("Request for robots.txt.");
                return StreamRobotsTxt(req, _logger);
                
                // var response = req.CreateResponse(HttpStatusCode.OK);
                // response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                // response.WriteString(Utility.ROBOT_RESPONSE);
                // return response;
            }
            else if (shortUrl == Utility.FAV_ICON)
            {
                _logger.LogInformation("Request for favicon.ico");
                return StreamFavoriteIcon(req, _logger);
            }

            var shortUrlEntity = default(ShortUrlEntity);
            var isSocialShare = IsSocialShare(req);

            if (!string.IsNullOrWhiteSpace(shortUrl))
            {
                StorageTableHelper stgHelper = new StorageTableHelper(_settings.DataStorage);

                var tempUrl = new ShortUrlEntity(string.Empty, shortUrl);
                shortUrlEntity = await stgHelper.GetShortUrlEntity(tempUrl);

                if (shortUrlEntity != null)
                {
                    _logger.LogInformation($"Found it: {shortUrlEntity.Url}");
                    if (!isSocialShare)
                    {
                        shortUrlEntity.Clicks++;
                        await stgHelper.SaveClickStatsEntity(new ClickStatsEntity(shortUrlEntity.RowKey, req.Url.Query));
                        await stgHelper.SaveShortUrlEntity(shortUrlEntity);
                    }
                    redirectUrl = WebUtility.UrlDecode(shortUrlEntity.ActiveUrl);
                }
            }
            else
            {
                _logger.LogInformation("Bad Link, resorting to fallback.");
            }

            if (isSocialShare && shortUrlEntity is { UseOpenGraph: true, OpenGraphInfo: not null }) //  ?.UseOpenGraph == true && shortUrlEntity?.OpenGraphInfo != null)                
            {
                _logger.LogInformation("Serving OpenGraph content");
                
                string html = BuildOpenGraphHtml(req, redirectUrl, shortUrlEntity);

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "text/html; charset=utf-8");
                response.WriteString(html);
                return response;
            }
            else
            {
                var res = req.CreateResponse(HttpStatusCode.Redirect);
                res.Headers.Add("Location", redirectUrl);
                return res;
            }

        }

        private bool IsSocialShare(HttpRequestData req)
        {
            var userAgents = req.Headers.Where(h => h.Key.Equals(HttpHeader.Names.UserAgent));

            foreach (var agent in userAgents)
            {
                _logger.LogInformation($"{agent.Key} = '{string.Join(",", agent.Value)}'");
            }

            return req.Headers
                .Any(h => h.Key.Equals(HttpHeader.Names.UserAgent) 
                    && h.Value.Any(v => 
                    SocialMediaBotUserAgentStarts.Any(f => v.ToLower().StartsWith(f))
                    || SocialMediaBotUserAgentContains.Any(f => v.ToLower().Contains(f))
                    ));
        }

        private string BuildOpenGraphHtml(HttpRequestData req, string redirectUrl, ShortUrlEntity shortUrlEntity)
        {
            var ogInfo = shortUrlEntity.OpenGraphInfo;

            var imageTag = string.Empty;
            foreach (var imageInfo in ogInfo.Images)
            {
                imageTag += OpenGraphImageTagTemplate.Replace("$imageUrl", imageInfo.Url);
            }

            var firstImage = ogInfo.Images.FirstOrDefault();
            var firstImageTag = firstImage == null ? string.Empty : $"<img src=\"{firstImage.Url}\" />";

            var videoTag = string.Empty;
            foreach (var videoInfo in ogInfo.Videos)
            {
                videoTag += OpenGraphVideoTagTemplate.Replace("$videoUrl", videoInfo.Url);
            }

            var sitename = string.IsNullOrWhiteSpace(ogInfo.SiteName) ? _settings.DefaultOpenGraphSiteName : ogInfo.SiteName;

            var html = OpenGraphDocTemplate
                .Replace("$title", shortUrlEntity.Title)
                .Replace("$description", ogInfo.Description)
                .Replace("$site_name", sitename)
                .Replace("$type", ogInfo.Type)
                .Replace("$shortUrl", req.Url.ToString())
                .Replace("$ogImageTag", imageTag)
                .Replace("$FirstImageTag", firstImageTag)
                .Replace("$ogVideoTag", videoTag)
                .Replace("$redirectUrl", redirectUrl)
                ;
            return html;
        }

        private static HttpResponseData StreamFavoriteIcon(HttpRequestData req, ILogger log)
        {
            const string path = "favicon.ico";
            const string mediaType = "image/vnd.microsoft.icon";

            return StreamWwwContent(req, log, path, mediaType);
        }

        private static HttpResponseData StreamRobotsTxt(HttpRequestData req, ILogger log)
        {
            const string path = "robots.txt";
            const string mediaType = "text/plain";

            return StreamWwwContent(req, log, path, mediaType);
        }

        private static HttpResponseData StreamWwwContent(HttpRequestData req, ILogger log, string path, string mediaType)
        {
            var scriptPath = Path.Combine(Environment.CurrentDirectory, "www");
            if (!Directory.Exists(scriptPath))
            {
                scriptPath = Path.Combine(
                    Environment.GetEnvironmentVariable("HOME", EnvironmentVariableTarget.Process),
                    @"site/wwwroot/www");
            }

            var filePath = Path.GetFullPath(Path.Combine(scriptPath, path));
            if (!File.Exists(filePath))
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            log.LogInformation($"Attempting to retrieve file at path {filePath}.");
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", mediaType);
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            response.Body = stream; 
            return response;
        }

    }
}
