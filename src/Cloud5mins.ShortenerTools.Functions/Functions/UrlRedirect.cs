using Azure;
using Cloud5mins.ShortenerTools.Core.Domain;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    <meta name=""og:title"" content=""$title"" >
    <meta name=""og:type"" content=""$type"" >
    <meta name=""og:url"" content=""$shortUrl"" >
    $ogImageTag
    $ogVideoTag
    <meta http-equiv=""refresh"" content=""0;url=$redirectUrl"">
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

        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;

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

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "text/plain; charset=utf-8");
                response.WriteString(Utility.ROBOT_RESPONSE);
                return response;
            }
            else if (shortUrl == Utility.FAV_ICON)
            {
                _logger.LogInformation("Request for favicon.ico");
                return StreamFavoriteIcon(req, _logger);
            }

            var shortUrlEntity = default(ShortUrlEntity);

            if (!string.IsNullOrWhiteSpace(shortUrl))
            {
                StorageTableHelper stgHelper = new StorageTableHelper(_settings.DataStorage);

                var tempUrl = new ShortUrlEntity(string.Empty, shortUrl);
                shortUrlEntity = await stgHelper.GetShortUrlEntity(tempUrl);

                if (shortUrlEntity != null)
                {
                    _logger.LogInformation($"Found it: {shortUrlEntity.Url}");
                    shortUrlEntity.Clicks++;
                    await stgHelper.SaveClickStatsEntity(new ClickStatsEntity(shortUrlEntity.RowKey));
                    await stgHelper.SaveShortUrlEntity(shortUrlEntity);
                    redirectUrl = WebUtility.UrlDecode(shortUrlEntity.ActiveUrl);
                }
            }
            else
            {
                _logger.LogInformation("Bad Link, resorting to fallback.");
            }

            if (shortUrlEntity is { UseOpenGraph: true, OpenGraphInfo: not null }) //  ?.UseOpenGraph == true && shortUrlEntity?.OpenGraphInfo != null)                
            {
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

        private static string BuildOpenGraphHtml(HttpRequestData req, string redirectUrl, ShortUrlEntity shortUrlEntity)
        {
            var ogInfo = shortUrlEntity.OpenGraphInfo;

            var imageTag = string.Empty;
            foreach (var imageInfo in ogInfo.Images)
            {
                imageTag += OpenGraphImageTagTemplate.Replace("$imageUrl", imageInfo.Url);
            }

            var videoTag = string.Empty;
            foreach (var videoInfo in ogInfo.Videos)
            {
                videoTag += OpenGraphVideoTagTemplate.Replace("$videoUrl", videoInfo.Url);
            }

            var html = OpenGraphDocTemplate
                .Replace("$title", shortUrlEntity.Title)
                .Replace("$type", ogInfo.Type)
                .Replace("$shortUrl", req.Url.ToString())
                .Replace("$ogImageTag", imageTag)
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

        private static HttpResponseData StreamWwwContent(HttpRequestData req, ILogger log, string path, string mediaType)
        {
            var scriptPath = Path.Combine(Environment.CurrentDirectory, "www");
            if (!Directory.Exists(scriptPath))
            {
                scriptPath = Path.Combine(
                    Environment.GetEnvironmentVariable("HOME", EnvironmentVariableTarget.Process),
                    @"site\wwwroot\www");
            }

            var filePath = Path.GetFullPath(Path.Combine(scriptPath, path));
            if (!File.Exists(filePath))
            {
                return req.CreateResponse(HttpStatusCode.NotFound);
            }

            log.LogInformation($"Attempting to retrieve file at path {filePath}.");
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", mediaType);
            var stream = new FileStream(filePath, FileMode.Open);
            response.Body = stream; 
            return response;
        }

    }
}
