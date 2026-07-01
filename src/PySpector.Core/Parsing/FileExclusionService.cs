using System.Text.RegularExpressions;

namespace PySpector.Core.Parsing;

/// <summary>
/// File exclusion utilities — 1:1 mapping from analysis/mod.rs is_excluded().
/// Matches glob patterns against file paths and components.
/// </summary>
public static class FileExclusionService
{
    /// <summary>
    /// Returns true if path matches any exclusion pattern (fnmatch/glob-style).
    /// Patterns match against full path, filename, and each path component.
    /// </summary>
    public static bool IsExcluded(string filePath, IReadOnlyList<string> exclusions)
    {
        var normalized = filePath.Replace('\\', '/');
        var fileName = Path.GetFileName(filePath);

        foreach (var ex in exclusions)
        {
            if (MatchPattern(ex, normalized, fileName))
                return true;
        }
        return false;
    }

    private static bool MatchPattern(string pattern, string fullPath, string fileName)
    {
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            // Convert glob to regex
            var regex = GlobToRegex(pattern);
            return regex.IsMatch(fullPath) || regex.IsMatch(fileName);
        }

        // Simple substring match
        return fullPath.Contains(pattern, StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> _regexCache = new();

    private static Regex GlobToRegex(string pattern)
    {
        return _regexCache.GetOrAdd(pattern, p =>
        {
            var escaped = Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", ".");
            return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        });
    }
}
