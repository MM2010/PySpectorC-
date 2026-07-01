using System.Reflection;

namespace PySpector.Core.Services;

/// <summary>
/// Configuration service — 1:1 mapping from config.py.
/// Loads TOML config and provides default rules.
/// </summary>
public static class ConfigService
{
    public static readonly IReadOnlyList<string> DefaultExclusions =
    [
        ".venv", "venv", ".git", "__pycache__", "build", "dist", "*.egg-info",
        "node_modules", "bower_components", "vendor",
        "*/tests/fixtures/*", "*/test/fixtures/*", "*_fixtures/*", "*/testdata/*",
        "**/test_*.py", "**/*_test.py",
    ];

    /// <summary>Load rules from embedded TOML resources. 1:1 from config.py get_default_rules().</summary>
    public static string GetDefaultRules(bool aiScan = false)
    {
        var assembly = Assembly.GetAssembly(typeof(ConfigService))
            ?? typeof(ConfigService).Assembly;

        // Try multiple resource name patterns
        var baseRules = LoadEmbeddedResource(assembly, "built-in-rules.toml")
            ?? LoadEmbeddedResource(assembly, "PySpector.Core.Rules.built-in-rules.toml");

        if (baseRules is null)
            throw new FileNotFoundException("Could not load built-in-rules.toml from embedded resources.");

        if (aiScan)
        {
            var aiRules = LoadEmbeddedResource(assembly, "built-in-rules-ai.toml")
                ?? LoadEmbeddedResource(assembly, "PySpector.Core.Rules.built-in-rules-ai.toml");
            if (aiRules is not null)
                return baseRules + "\n" + aiRules;
        }

        return baseRules;
    }

    private static string? LoadEmbeddedResource(Assembly assembly, string resourceName)
    {
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullName is null) return null;

        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
