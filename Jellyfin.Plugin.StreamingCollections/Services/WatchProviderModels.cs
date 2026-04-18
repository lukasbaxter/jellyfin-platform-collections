using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.StreamingCollections.Services;

public class WatchProviderSet
{
    [JsonPropertyName("flatrate")]
    public List<Provider>? Flatrate { get; set; }

    [JsonPropertyName("free")]
    public List<Provider>? Free { get; set; }

    [JsonPropertyName("ads")]
    public List<Provider>? Ads { get; set; }

    [JsonPropertyName("rent")]
    public List<Provider>? Rent { get; set; }

    [JsonPropertyName("buy")]
    public List<Provider>? Buy { get; set; }
}

public class Provider
{
    [JsonPropertyName("provider_id")]
    public int ProviderId { get; set; }

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = string.Empty;
}

public class WatchProviderResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("results")]
    public Dictionary<string, WatchProviderSet>? Results { get; set; }
}
