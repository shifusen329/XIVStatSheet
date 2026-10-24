using Dalamud.Configuration;
using System;
using System.IO;

namespace XIVStatSheet;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Empty means the default folder inside the plugin's config directory
    public string ExportDirectory { get; set; } = string.Empty;
    public bool WriteJson { get; set; } = true;
    public bool WriteMarkdown { get; set; } = true;

    public static string GetDefaultExportDirectory() =>
        Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "exports");

    public string ResolveExportDirectory() =>
        string.IsNullOrWhiteSpace(ExportDirectory) ? GetDefaultExportDirectory() : ExportDirectory;

    // The below exists just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
