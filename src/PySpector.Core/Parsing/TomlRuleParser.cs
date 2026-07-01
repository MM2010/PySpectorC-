using System.Collections.Immutable;
using System.Text.RegularExpressions;
using PySpector.Core.Models;
using Tommy;

namespace PySpector.Core.Parsing;

/// <summary>
/// TOML rule deserializer — 1:1 mapping from rules.rs serde Deserialize.
/// Handles [defaults] and [[rule]] tables with placeholder substitution.
/// </summary>
public static class TomlRuleParser
{
    private const string PlaceholderSentinel = "__SHARED_PLACEHOLDERS__";

    /// <summary>Parse TOML rules text into a RuleSet.</summary>
    public static RuleSet Parse(string tomlText)
    {
        // Resolve shared placeholder before parsing
        var placeholderValue = ExtractPlaceholderValue(tomlText);
        if (placeholderValue is not null)
            tomlText = tomlText.Replace(PlaceholderSentinel, placeholderValue);

        using var reader = new StringReader(tomlText);
        var table = TOML.Parse(reader);

        var defaults = ParseDefaults(table);
        var rules = ParseRules(table);

        return new RuleSet { Defaults = defaults, Rules = rules };
    }

    private static Defaults ParseDefaults(TomlTable root)
    {
        if (!root.TryGetNode("defaults", out var defaultsNode) || defaultsNode is not TomlTable defaultsTable)
            return new Defaults();

        var excludePatterns = ImmutableArray<string>.Empty;
        var disabledIds = ImmutableArray<string>.Empty;

        if (defaultsTable.TryGetNode("exclude_file_patterns", out var exclNode) && exclNode is TomlArray exclArray)
            excludePatterns = exclArray.RawArray.Select(v => v.ToString()!).ToImmutableArray();

        if (defaultsTable.TryGetNode("disabled_rule_ids", out var disNode) && disNode is TomlArray disArray)
            disabledIds = disArray.RawArray.Select(v => v.ToString()!).ToImmutableArray();

        return new Defaults
        {
            ExcludeFilePatterns = excludePatterns,
            DisabledRuleIds = disabledIds,
        };
    }

    private static ImmutableArray<Rule> ParseRules(TomlTable root)
    {
        if (!root.TryGetNode("rule", out var rulesNode) || rulesNode is not TomlArray rulesArray)
            return [];

        return rulesArray.RawArray
            .OfType<TomlTable>()
            .Select(ParseRule)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToImmutableArray();
    }

    private static Rule? ParseRule(TomlTable ruleTable)
    {
        try
        {
            var id = GetString(ruleTable, "id") ?? string.Empty;
            if (string.IsNullOrEmpty(id)) return null;

            var severity = ParseSeverity(GetString(ruleTable, "severity") ?? "Low");

            return new Rule
            {
                Id = id,
                Description = GetString(ruleTable, "description") ?? string.Empty,
                Severity = severity,
                Confidence = GetString(ruleTable, "confidence") ?? "Medium",
                Remediation = GetString(ruleTable, "remediation") ?? string.Empty,
                Pattern = ParseRegex(ruleTable, "pattern"),
                ExcludePattern = ParseRegex(ruleTable, "exclude_pattern"),
                AstMatch = GetString(ruleTable, "ast_match"),
                FilePattern = GetString(ruleTable, "file_pattern"),
                ExcludeFilePattern = GetString(ruleTable, "exclude_file_pattern"),
                FileContentExclude = ParseRegex(ruleTable, "file_content_exclude"),
                Cwe = GetString(ruleTable, "cwe"),
            };
        }
        catch
        {
            return null;
        }
    }

    private static Severity ParseSeverity(string s) => s.ToLowerInvariant() switch
    {
        "critical" => Severity.Critical,
        "high" => Severity.High,
        "medium" => Severity.Medium,
        _ => Severity.Low,
    };

    private static Regex? ParseRegex(TomlTable table, string key)
    {
        if (!table.TryGetNode(key, out var node)) return null;
        var pattern = node.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(pattern) || pattern == "\"\"" || pattern == "''") return null;

        // P2: Try DFA-backed NonBacktracking first (matches Rust's regex crate performance).
        // Falls back to NFA Compiled if pattern uses capture groups or other incompatible features.
        try
        {
            return new Regex(pattern,
                RegexOptions.NonBacktracking | RegexOptions.CultureInvariant |
                RegexOptions.Compiled);
        }
        catch (NotSupportedException)
        {
            // Pattern uses captures/lookarounds — fall back to NFA
        }

        try { return new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
        catch { return null; }
    }

    private static string? GetString(TomlTable table, string key)
    {
        if (!table.TryGetNode(key, out var node)) return null;
        return (node as TomlString)?.Value ?? node.ToString();
    }

    /// <summary>Extract exclude_pattern_placeholder value from TOML text.</summary>
    private static string? ExtractPlaceholderValue(string tomlText)
    {
        var match = Regex.Match(tomlText,
            @"^\s*exclude_pattern_placeholder\s*=\s*""((?:[^""\\]|\\.)*)""",
            RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
}
