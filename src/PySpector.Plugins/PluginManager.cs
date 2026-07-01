using System.Text.Json;
using System.Text.Json.Serialization;

namespace PySpector.Plugins;

/// <summary>
/// Plugin registry and manager — 1:1 from plugin_system.py PluginManager.
/// Tracks installed plugins, their trusted status, and persisted config.
/// </summary>
public static class PluginManager
{
    private static readonly string PluginConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".pyspector", "plugins");

    private static readonly string RegistryPath = Path.Combine(PluginConfigDir, "registry.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static PluginRegistry _registry = LoadRegistry();

    public static IReadOnlyList<PluginEntry> ListPlugins() => _registry.Plugins;

    public static void RegisterPlugin(string name, string version, string sourceFile, bool trusted = false)
    {
        var entry = _registry.Plugins.FirstOrDefault(p => p.Name == name);
        if (entry is not null)
        {
            _registry.Plugins.Remove(entry);
        }
        _registry.Plugins.Add(new PluginEntry(name, version, sourceFile, trusted));
        SaveRegistry();
    }

    public static void SetTrusted(string name, bool trusted)
    {
        var entry = _registry.Plugins.FirstOrDefault(p => p.Name == name);
        if (entry is not null)
        {
            _registry.Plugins.Remove(entry);
            _registry.Plugins.Add(entry with { Trusted = trusted });
            SaveRegistry();
        }
    }

    public static void UnregisterPlugin(string name)
    {
        _registry.Plugins.RemoveAll(p => p.Name == name);
        SaveRegistry();
    }

    public static bool IsTrusted(string name) =>
        _registry.Plugins.FirstOrDefault(p => p.Name == name)?.Trusted ?? false;

    private static PluginRegistry LoadRegistry()
    {
        try
        {
            Directory.CreateDirectory(PluginConfigDir);
            if (File.Exists(RegistryPath))
            {
                var json = File.ReadAllText(RegistryPath);
                return JsonSerializer.Deserialize<PluginRegistry>(json, JsonOpts) ?? new();
            }
        }
        catch { /* return empty registry on failure */ }
        return new();
    }

    private static void SaveRegistry()
    {
        try
        {
            Directory.CreateDirectory(PluginConfigDir);
            var json = JsonSerializer.Serialize(_registry, JsonOpts);
            File.WriteAllText(RegistryPath, json);
        }
        catch { /* best-effort persistence */ }
    }
}

internal sealed record PluginRegistry
{
    [property: JsonPropertyName("plugins")]
    public List<PluginEntry> Plugins { get; set; } = [];
}

public sealed record PluginEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("source_file")] string SourceFile,
    [property: JsonPropertyName("trusted")] bool Trusted);
