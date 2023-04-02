namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class OpenGraphInfo
    {
        public OpenGraphInfo() 
        { 
            Images = new();
            Videos = new();
        }

        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public List<OpenGraphImage> Images { get; set; }
        public List<OpenGraphVideo> Videos { get; set; }

    }
}
