using System;
using System.Collections.Generic;
using Jellyfin.Plugin.StreamingCollections.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StreamingCollections;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Streaming Collections";

    public override Guid Id => Guid.Parse("b2c3d4e5-6f70-4a8b-9c0d-1e2f3a4b5c6d");

    public override string Description =>
        "Tags movies and shows by the streaming service they're available on and builds matching Jellyfin collections. Does not move files.";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        }
    };
}
