using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.StreamingCollections.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.StreamingCollections.Services;

public class StreamingCollectionSyncer
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly TmdbWatchProviderClient _tmdb;
    private readonly WatchProviderCache _cache;
    private readonly ILogger<StreamingCollectionSyncer> _log;

    public StreamingCollectionSyncer(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        TmdbWatchProviderClient tmdb,
        WatchProviderCache cache,
        ILogger<StreamingCollectionSyncer> log)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _tmdb = tmdb;
        _cache = cache;
        _log = log;
    }

    public async Task RunAsync(IProgress<double> progress, CancellationToken ct)
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null)
        {
            _log.LogWarning("Plugin configuration unavailable; skipping run.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            _log.LogWarning("TMDB API key not configured; skipping run.");
            return;
        }

        var ttl = TimeSpan.FromDays(Math.Max(1, config.CacheTtlDays));
        var allowlist = ParseAllowlist(config.ProviderAllowlist);

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
            Recursive = true,
            IsVirtualItem = false
        });

        _log.LogInformation("Streaming Collections sync starting: {Count} items", items.Count);

        var providerToItems = new Dictionary<string, List<BaseItem>>(StringComparer.OrdinalIgnoreCase);
        var total = Math.Max(1, items.Count);

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            progress.Report(i * 90.0 / total);

            var tmdbIdRaw = item.GetProviderId(MetadataProvider.Tmdb);
            if (string.IsNullOrWhiteSpace(tmdbIdRaw) || !int.TryParse(tmdbIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId))
            {
                continue;
            }

            var mediaType = item is Movie ? TmdbMediaType.Movie : TmdbMediaType.Tv;

            var cached = await _cache.GetAsync(mediaType, tmdbId, config.Region, ttl, ct).ConfigureAwait(false);
            if (cached == null)
            {
                var response = await _tmdb.GetAsync(mediaType, tmdbId, config.TmdbApiKey, config.MaxRequestsPerSecond, ct).ConfigureAwait(false);
                cached = BuildCacheEntry(response, config.Region);
                await _cache.SetAsync(mediaType, tmdbId, config.Region, cached, ct).ConfigureAwait(false);
            }

            var slugs = SelectOfferings(cached, config);
            var filtered = slugs
                .Where(s => allowlist.Count == 0 || allowlist.Contains(s))
                .ToArray();

            if (filtered.Length == 0)
            {
                // Still strip stale managed tags if any were previously applied.
                await ApplyTagsAsync(item, Array.Empty<string>(), config.TagPrefix, ct).ConfigureAwait(false);
                continue;
            }

            await ApplyTagsAsync(item, filtered, config.TagPrefix, ct).ConfigureAwait(false);

            foreach (var provider in filtered)
            {
                if (!providerToItems.TryGetValue(provider, out var list))
                {
                    list = new List<BaseItem>();
                    providerToItems[provider] = list;
                }
                list.Add(item);
            }
        }

        progress.Report(90.0);

        var index = 0;
        var providerCount = Math.Max(1, providerToItems.Count);
        foreach (var (provider, members) in providerToItems)
        {
            ct.ThrowIfCancellationRequested();
            await SyncCollectionAsync(provider, config.CollectionPrefix, members, ct).ConfigureAwait(false);
            index++;
            progress.Report(90.0 + (index * 10.0 / providerCount));
        }

        progress.Report(100.0);
        _log.LogInformation("Streaming Collections sync complete: {ProviderCount} providers mapped", providerToItems.Count);
    }

    private static HashSet<string> ParseAllowlist(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return set;
        }

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(Slugify(part));
        }
        return set;
    }

    private static CachedProviders BuildCacheEntry(WatchProviderResponse? response, string region)
    {
        var entry = new CachedProviders();
        if (response?.Results == null || !response.Results.TryGetValue(region, out var regional) || regional == null)
        {
            return entry;
        }

        entry.Flatrate = ToSlugs(regional.Flatrate);
        entry.Free = ToSlugs(regional.Free);
        entry.Ads = ToSlugs(regional.Ads);
        entry.Rent = ToSlugs(regional.Rent);
        entry.Buy = ToSlugs(regional.Buy);
        return entry;
    }

    private static string[] ToSlugs(List<Provider>? providers)
    {
        if (providers == null || providers.Count == 0)
        {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            if (!string.IsNullOrWhiteSpace(p.ProviderName))
            {
                set.Add(Slugify(p.ProviderName));
            }
        }
        return set.ToArray();
    }

    private static IReadOnlyCollection<string> SelectOfferings(CachedProviders cached, PluginConfiguration config)
    {
        var slugs = new HashSet<string>(cached.Flatrate, StringComparer.OrdinalIgnoreCase);
        if (config.IncludeFreeWithAds)
        {
            foreach (var s in cached.Free) slugs.Add(s);
            foreach (var s in cached.Ads) slugs.Add(s);
        }
        if (config.IncludeRent)
        {
            foreach (var s in cached.Rent) slugs.Add(s);
        }
        if (config.IncludeBuy)
        {
            foreach (var s in cached.Buy) slugs.Add(s);
        }
        return slugs;
    }

    private async Task ApplyTagsAsync(BaseItem item, IReadOnlyCollection<string> providers, string tagPrefix, CancellationToken ct)
    {
        var prefix = tagPrefix ?? string.Empty;
        var desiredTags = providers.Select(p => prefix + p).ToArray();
        var currentTags = item.Tags ?? Array.Empty<string>();

        var currentManaged = currentTags
            .Where(t => !string.IsNullOrEmpty(prefix) && t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredSet = new HashSet<string>(desiredTags, StringComparer.OrdinalIgnoreCase);

        if (currentManaged.SetEquals(desiredSet))
        {
            return;
        }

        var preserved = currentTags.Where(t => string.IsNullOrEmpty(prefix) || !t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        item.Tags = preserved.Concat(desiredTags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
    }

    private async Task SyncCollectionAsync(string providerSlug, string collectionPrefix, List<BaseItem> members, CancellationToken ct)
    {
        var displayName = (collectionPrefix ?? string.Empty) + PrettyName(providerSlug);

        var existing = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.BoxSet },
            Name = displayName,
            Recursive = true
        }).OfType<BoxSet>().FirstOrDefault();

        var desiredIds = members.Select(m => m.Id).Distinct().ToHashSet();

        if (existing == null)
        {
            await _collectionManager.CreateCollectionAsync(new CollectionCreationOptions
            {
                Name = displayName,
                ItemIdList = desiredIds.Select(id => id.ToString("N")).ToArray(),
                IsLocked = false
            }).ConfigureAwait(false);
            _log.LogInformation("Created collection '{Name}' with {Count} members", displayName, desiredIds.Count);
            return;
        }

        var currentIds = existing.GetLinkedChildren().Select(i => i.Id).ToHashSet();
        var toAdd = desiredIds.Where(id => !currentIds.Contains(id)).ToArray();
        var toRemove = currentIds.Where(id => !desiredIds.Contains(id)).ToArray();

        if (toAdd.Length > 0)
        {
            await _collectionManager.AddToCollectionAsync(existing.Id, toAdd).ConfigureAwait(false);
        }
        if (toRemove.Length > 0)
        {
            await _collectionManager.RemoveFromCollectionAsync(existing.Id, toRemove).ConfigureAwait(false);
        }

        if (toAdd.Length > 0 || toRemove.Length > 0)
        {
            _log.LogInformation("Updated collection '{Name}': +{Added} -{Removed}", displayName, toAdd.Length, toRemove.Length);
        }
    }

    private static string Slugify(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (c == ' ' || c == '-' || c == '_' || c == '+')
            {
                if (sb.Length > 0 && sb[^1] != '-')
                {
                    sb.Append('-');
                }
            }
        }
        while (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }
        return sb.ToString();
    }

    private static string PrettyName(string slug)
    {
        var parts = slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }
}
