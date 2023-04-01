namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class OpenGraphVideo
    {
        public OpenGraphVideo() { }

        public short Height { get; set; } = 0;
        public string MimeType { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public short Width { get; set; } = 0;
    }
}
