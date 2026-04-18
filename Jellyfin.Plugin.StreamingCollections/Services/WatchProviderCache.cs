using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamingCollections.Services;

public class CachedProviders
{
    public DateTimeOffset FetchedAt { get; set; }

    public string[] Flatrate { get; set; } = Array.Empty<string>();

    public string[] Free { get; set; } = Array.Empty<string>();

    public string[] Ads { get; set; } = Array.Empty<string>();

    public string[] Rent { get; set; } = Array.Empty<string>();

    public string[] Buy { get; set; } = Array.Empty<string>();
}

public class WatchProviderCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IApplicationPaths _paths;
    private readonly ILogger<WatchProviderCache> _log;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public WatchProviderCache(IApplicationPaths paths, ILogger<WatchProviderCache> log)
    {
        _paths = paths;
        _log = log;
    }

    private string CacheDir
    {
        get
        {
            var dir = Path.Combine(_paths.CachePath, "streaming-collections");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public async Task<CachedProviders?> GetAsync(
        TmdbMediaType mediaType,
        int tmdbId,
        string region,
        TimeSpan ttl,
        CancellationToken ct)
    {
        var path = PathFor(mediaType, tmdbId, region);
        if (!File.Exists(path))
        {
            return null;
        }

        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<CachedProviders>(stream, SerializerOptions, ct).ConfigureAwait(false);
            if (entry == null)
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - entry.FetchedAt > ttl)
            {
                return null;
            }

            return entry;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to read cache entry for {MediaType} {TmdbId}", mediaType, tmdbId);
            return null;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SetAsync(
        TmdbMediaType mediaType,
        int tmdbId,
        string region,
        CachedProviders providers,
        CancellationToken ct)
    {
        var path = PathFor(mediaType, tmdbId, region);
        providers.FetchedAt = DateTimeOffset.UtcNow;

        await _ioGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tmp = path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, providers, SerializerOptions, ct).ConfigureAwait(false);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to write cache entry for {MediaType} {TmdbId}", mediaType, tmdbId);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private string PathFor(TmdbMediaType mediaType, int tmdbId, string region)
    {
        var kind = mediaType == TmdbMediaType.Movie ? "movie" : "tv";
        var safeRegion = string.IsNullOrWhiteSpace(region) ? "xx" : region.ToLowerInvariant();
        return Path.Combine(CacheDir, $"{kind}_{safeRegion}_{tmdbId.ToString(CultureInfo.InvariantCulture)}.json");
    }
}
