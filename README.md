# Jellyfin Streaming Collections

A Jellyfin plugin that tags movies and shows by the streaming services they're available on and builds matching Jellyfin collections (Netflix, Disney+, Hulu, Crave, Nickelodeon, and anything else TMDB knows about).

**It does not move or rename any files.** Only tags and collections are created/updated.

## How it works

1. A weekly scheduled task walks your Movies and TV libraries.
2. For each item with a TMDB id, it asks TMDB `/watch/providers` which services carry it in your region.
3. Results are cached on disk for the configured TTL (default 7 days). Subsequent scans reuse the cache, so TMDB is not re-queried on every run.
4. Each item gets tags like `streaming:netflix`, `streaming:disney-plus`.
5. For each provider, a Jellyfin collection (BoxSet) is created/updated so its members match everything tagged with that service.

## Install (plugin repository — recommended)

In Jellyfin: **Dashboard → Plugins → Repositories → `+`**, then add:

- **Repository Name:** `Streaming Collections`
- **Repository URL:** `https://raw.githubusercontent.com/lukasbaxter/jellyfin-platform-collections/main/manifest.json`

Then go to **Catalog**, install **Streaming Collections**, and restart Jellyfin. Configure it under **Dashboard → Plugins → Streaming Collections**.

Updates appear automatically whenever a new version is published to this repo.

## Install (manual)

1. Download the latest `streaming-collections-*.zip` from [Releases](../../releases).
2. Unzip into your Jellyfin `plugins/StreamingCollections_<version>/` directory.
3. Restart Jellyfin.

## Configure

| Setting | Default | Notes |
| --- | --- | --- |
| TMDB API Key | _(required)_ | v3 API key from https://www.themoviedb.org/settings/api |
| Region | `US` | Two-letter country code; controls which provider list TMDB returns |
| Cache TTL (days) | `7` | How long responses stay cached on disk |
| Max TMDB requests / sec | `4` | Simple rate limit on outbound calls |
| Tag prefix | `streaming:` | Prefix for tags written onto items |
| Collection prefix | _(empty)_ | Optional prefix for generated collection names |
| Provider allowlist | _(empty)_ | Comma-separated provider names. Blank = include everything TMDB returns |
| Include ad-supported | `on` | Include `free`/`ads` TMDB offerings |
| Include rentals | `off` | Include `rent` offerings |
| Include purchases | `off` | Include `buy` offerings |

The scheduled task `Update streaming collections` runs weekly by default (Sunday 03:00). You can trigger it manually from Dashboard → Scheduled Tasks.

## Caching

API responses are cached to `<jellyfin cache>/streaming-collections/` as JSON, keyed by media type + region + TMDB id. Delete that folder to force a full refresh. Cache is also respected across server restarts.

## Development

```
dotnet build Jellyfin.Plugin.StreamingCollections/Jellyfin.Plugin.StreamingCollections.csproj
```

CI builds a release zip on tagged commits (`v*`).

## License

MIT
