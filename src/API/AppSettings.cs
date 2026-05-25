namespace IA.API
{
    public class AppSettings : IAppSettings
    {
        public string BaseURL { get; set; } = string.Empty;
        public string BaseURLPrefix { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string[] APIKeys { get; set; } = [];
        public string ContentRootPath { get; set; } = string.Empty;

        public AppSettings()
        {
        }

    }

    public interface IAppSettings
    {
        string BaseURL { get; set; }
        string BaseURLPrefix { get; set; }
        public string Version { get; set; }
        public string[] APIKeys { get; set; }
        string ContentRootPath { get; set; }

    }
}
