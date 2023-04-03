using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Cloud5mins.ShortenerTools.Functions.Functions
{
    public class KeepAlive
    {
        private readonly ILogger _logger;

        public KeepAlive(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<KeepAlive>();
        }

        [Function("KeepAlive")]
        public void Run([TimerTrigger("0 */5 * * * *")] KeepAliveTimerInfo myTimer)
        {
            _logger.LogInformation($"KeepAlive executed {DateTime.Now} - {myTimer.ScheduleStatus.Next}");
        }
    }

    public class KeepAliveTimerInfo
    {
        public MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
