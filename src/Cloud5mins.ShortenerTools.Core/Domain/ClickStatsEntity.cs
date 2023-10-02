using Microsoft.Azure.Cosmos.Table;
using System;

namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class ClickStatsEntity : TableEntity
    {
        //public string Id { get; set; }
        public string Datetime { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;

        public ClickStatsEntity() { }

        public ClickStatsEntity(string vanity, string query)
            : this(vanity)
        {
            Query = query;
        }

        public ClickStatsEntity(string vanity)
        {
            PartitionKey = vanity;
            RowKey = Guid.NewGuid().ToString();
            Datetime = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }
    }


}