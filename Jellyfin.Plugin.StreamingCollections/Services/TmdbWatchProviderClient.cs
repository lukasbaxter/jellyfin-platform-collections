using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamingCollections.Services;

public enum TmdbMediaType
{
    Movie,
    Tv
}

public class TmdbWatchProviderClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3";

    private readonly HttpClient _http;
    private readonly ILogger<TmdbWatchProviderClient> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextAllowedAt = DateTimeOffset.MinValue;

    public TmdbWatchProviderClient(HttpClient http, ILogger<TmdbWatchProviderClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<WatchProviderResponse?> GetAsync(
        TmdbMediaType mediaType,
        int tmdbId,
        string apiKey,
        int maxRequestsPerSecond,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var segment = mediaType == TmdbMediaType.Movie ? "movie" : "tv";
        var url = $"{BaseUrl}/{segment}/{tmdbId.ToString(CultureInfo.InvariantCulture)}/watch/providers?api_key={Uri.EscapeDataString(apiKey)}";

        await ThrottleAsync(maxRequestsPerSecond, ct).ConfigureAwait(false);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    _log.LogWarning("TMDB rate limited, backing off for {Delay}", retryAfter);
                    await Task.Delay(retryAfter, ct).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<WatchProviderResponse>(cancellationToken: ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < 2)
            {
                _log.LogWarning(ex, "TMDB request failed, retrying ({Attempt})", attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct).ConfigureAwait(false);
            }
        }

        return null;
    }

    private async Task ThrottleAsync(int maxRequestsPerSecond, CancellationToken ct)
    {
        if (maxRequestsPerSecond <= 0)
        {
            return;
        }

        var spacing = TimeSpan.FromSeconds(1.0 / maxRequestsPerSecond);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now < _nextAllowedAt)
            {
                await Task.Delay(_nextAllowedAt - now, ct).ConfigureAwait(false);
            }
            _nextAllowedAt = DateTimeOffset.UtcNow + spacing;
        }
        finally
        {
            _gate.Release();
        }
    }
}
