using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.StreamingCollections.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public PluginConfiguration()
    {
        TmdbApiKey = string.Empty;
        Region = "US";
        CacheTtlDays = 7;
        CollectionPrefix = string.Empty;
        TagPrefix = "streaming:";
        IncludeFreeWithAds = true;
        IncludeRent = false;
        IncludeBuy = false;
        ProviderAllowlist = string.Empty;
        MaxRequestsPerSecond = 4;
    }

    public string TmdbApiKey { get; set; }

    public string Region { get; set; }

    public int CacheTtlDays { get; set; }

    public string CollectionPrefix { get; set; }

    public string TagPrefix { get; set; }

    public bool IncludeFreeWithAds { get; set; }

    public bool IncludeRent { get; set; }

    public bool IncludeBuy { get; set; }

    public string ProviderAllowlist { get; set; }

    public int MaxRequestsPerSecond { get; set; }
}
