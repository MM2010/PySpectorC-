using PySpector.Core.Models;

namespace PySpector.Plugins;

/// <summary>Plugin metadata — 1:1 from plugin_system.py PluginMetadata.</summary>
public sealed record PluginMetadata(
    string Name,
    string Version,
    string Author,
    string Description,
    List<string>? Requires = null,
    string Category = "general")
{
    public List<string> Requires { get; init; } = Requires ?? [];
}

/// <summary>Result returned by a plugin after processing findings.</summary>
public sealed record PluginResult(
    bool Success,
    string Message,
    object? Data = null,
    List<string>? OutputFiles = null)
{
    public List<string> OutputFiles { get; init; } = OutputFiles ?? [];
}

/// <summary>
/// Base interface for all PySpector plugins.
/// 1:1 from plugin_system.py PySpectorPlugin (ABC).
/// </summary>
public interface IPySpectorPlugin
{
    PluginMetadata Metadata { get; }
    bool Initialize(IReadOnlyDictionary<string, object?> config);
    PluginResult ProcessFindings(IReadOnlyList<Issue> findings, string scanPath,
        IReadOnlyDictionary<string, object?>? kwargs = null);
    void Cleanup() { }
}
