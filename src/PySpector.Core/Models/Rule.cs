using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace PySpector.Core.Models;

/// <summary>1:1 mapping from rules.rs Defaults struct.</summary>
public sealed record Defaults
{
    public ImmutableArray<string> ExcludeFilePatterns { get; init; } = [];
    public ImmutableArray<string> DisabledRuleIds { get; init; } = [];
}

/// <summary>1:1 mapping from rules.rs Rule struct.</summary>
public sealed record Rule
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public Severity Severity { get; init; }
    public string Confidence { get; init; } = "Medium";
    public string Remediation { get; init; } = string.Empty;
    public Regex? Pattern { get; init; }
    public Regex? ExcludePattern { get; init; }
    public string? AstMatch { get; init; }
    public string? FilePattern { get; init; }
    public string? ExcludeFilePattern { get; init; }
    public Regex? FileContentExclude { get; init; }
    public string? Cwe { get; init; }

    /// <summary>
    /// Returns true if the file should be excluded based on path patterns OR
    /// file content (file_content_exclude). 1:1 from rules.rs Rule::is_file_excluded.
    /// </summary>
    public bool IsExcluded(string filePath, string fileContent, Defaults defaults)
    {
        var fileName = Path.GetFileName(filePath);

        // Check global exclusions
        foreach (var pattern in defaults.ExcludeFilePatterns)
        {
            if (GlobMatch(pattern, filePath, fileName))
                return true;
        }

        // Check rule-level file pattern exclusion
        if (ExcludeFilePattern is not null)
        {
            if (GlobMatch(ExcludeFilePattern, filePath, fileName))
                return true;
        }

        // Check file content exclusion regex
        if (FileContentExclude is not null && FileContentExclude.IsMatch(fileContent))
            return true;

        return false;
    }

    /// <summary>Simple glob matching: matches against full path and filename independently.</summary>
    private static bool GlobMatch(string pattern, string filePath, string fileName)
    {
        // If pattern contains wildcards, convert to regex, else use substring match
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var regex = new Regex(
                "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return regex.IsMatch(filePath) || regex.IsMatch(fileName);
        }
        return filePath.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
