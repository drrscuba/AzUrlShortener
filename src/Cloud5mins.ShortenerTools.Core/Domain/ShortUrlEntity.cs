using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class ShortUrlEntity : TableEntity
    {
        public string Url { get; set; }
        private string _activeUrl { get; set; }

        public string ActiveUrl
        {
            get
            {
                if (String.IsNullOrEmpty(_activeUrl))
                    _activeUrl = GetActiveUrl();
                return _activeUrl;
            }
        }


        public string Title { get; set; }

        public string ShortUrl { get; set; }

        public bool UseOpenGraph { get; set; }

        public string OpenGraphInfoPropertyRaw { get; set; }

        private OpenGraphInfo _openGraphInfo { get; set; }

        [IgnoreProperty]
        public OpenGraphInfo OpenGraphInfo
        {
            get
            {
                if (_openGraphInfo == null)
                {
                    if (String.IsNullOrEmpty(OpenGraphInfoPropertyRaw))
                    {
                        _openGraphInfo = new OpenGraphInfo();
                    }
                    else
                    {
                        _openGraphInfo = JsonSerializer.Deserialize<OpenGraphInfo>(OpenGraphInfoPropertyRaw) ?? new OpenGraphInfo();
                    }
                }
                return _openGraphInfo;
            }
            set
            {
                _openGraphInfo = value;
            }
        }

        public int Clicks { get; set; }

        public bool? IsArchived { get; set; }
        public string SchedulesPropertyRaw { get; set; }

        private List<Schedule> _schedules { get; set; }

        [IgnoreProperty]
        public List<Schedule> Schedules
        {
            get
            {
                if (_schedules == null)
                {
                    if (String.IsNullOrEmpty(SchedulesPropertyRaw))
                    {
                        _schedules = new List<Schedule>();
                    }
                    else
                    {
                        _schedules = JsonSerializer.Deserialize<Schedule[]>(SchedulesPropertyRaw).ToList<Schedule>();
                    }
                }
                return _schedules;
            }
            set
            {
                _schedules = value;
            }
        }

        public ShortUrlEntity() { }

        public ShortUrlEntity(string longUrl, string endUrl)
        {
            Initialize(longUrl, endUrl, string.Empty, false, null, null);
        }

        public ShortUrlEntity(string longUrl, string endUrl, Schedule[] schedules)
        {
            Initialize(longUrl, endUrl, string.Empty, false, null, schedules);
        }

        public ShortUrlEntity(string longUrl, string endUrl, string title, Schedule[] schedules)
        {
            Initialize(longUrl, endUrl, title, false, null, schedules);
        }

        public ShortUrlEntity(string longUrl, string endUrl, string title, bool useOpenGraph, OpenGraphInfo openGraphInfo, Schedule[] schedules)
        {
            Initialize(longUrl, endUrl, title, useOpenGraph, openGraphInfo, schedules);
        }

        private void Initialize(string longUrl, string endUrl, string title, bool useOpenGraph, OpenGraphInfo? openGraphInfo, Schedule[] schedules)
        {
            PartitionKey = endUrl.First().ToString();
            RowKey = endUrl;
            Url = longUrl;
            Title = title;
            Clicks = 0;
            IsArchived = false;
            UseOpenGraph = useOpenGraph;

            if (schedules?.Length>0)
            {
                Schedules = schedules.ToList<Schedule>();
                SchedulesPropertyRaw = JsonSerializer.Serialize<List<Schedule>>(Schedules);
            }

            if (openGraphInfo != null)
            {
                OpenGraphInfo = openGraphInfo;
                OpenGraphInfoPropertyRaw = JsonSerializer.Serialize(OpenGraphInfo);
            }
        }

        public static ShortUrlEntity GetEntity(string longUrl, string endUrl, string title, Schedule[] schedules)
        {
            return new ShortUrlEntity
            {
                PartitionKey = endUrl.First().ToString(),
                RowKey = endUrl,
                Url = longUrl,
                Title = title,
                Schedules = schedules.ToList<Schedule>()
            };
        }

        private string GetActiveUrl()
        {
            if (Schedules != null)
                return GetActiveUrl(DateTime.UtcNow);
            return Url;
        }

        private string GetActiveUrl(DateTime pointInTime)
        {
            var link = Url;
            var active = Schedules.Where(s =>
                s.End > pointInTime && //hasn't ended
                s.Start < pointInTime //already started
                ).OrderBy(s => s.Start); //order by start to process first link

            foreach (var sched in active.ToArray())
            {
                if (sched.IsActive(pointInTime))
                {
                    link = sched.AlternativeUrl;
                    break;
                }
            }
            return link;
        }
    }

}