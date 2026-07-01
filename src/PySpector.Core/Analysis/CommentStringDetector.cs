namespace PySpector.Core.Analysis;

/// <summary>
/// Detects if a line of code is inside a comment or string literal.
/// 1:1 mapping from config_analysis.rs is_in_comment_or_string().
/// Uses ReadOnlySpan&lt;char&gt; for zero-allocation substring checks.
/// </summary>
public static class CommentStringDetector
{
    public static bool IsInCommentOrString(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimStart();

        // Skip comments
        if (trimmed.StartsWith("#".AsSpan()))
            return true;

        // Skip docstrings and standalone string literals
        if (trimmed.StartsWith("\"\"\"".AsSpan()) && trimmed.EndsWith("\"\"\"".AsSpan()) && trimmed.Length > 6)
            return true;
        if (trimmed.StartsWith("'''".AsSpan()) && trimmed.EndsWith("'''".AsSpan()) && trimmed.Length > 6)
            return true;
        if (trimmed.StartsWith("\"".AsSpan()) && trimmed.EndsWith("\"".AsSpan()) && !trimmed.Contains(" = ".AsSpan(), StringComparison.Ordinal))
            return true;
        if (trimmed.StartsWith("'".AsSpan()) && trimmed.EndsWith("'".AsSpan()) && !trimmed.Contains(" = ".AsSpan(), StringComparison.Ordinal))
            return true;

        // Check for docstring markers without assignment or function call
        if ((trimmed.Contains("\"\"\"".AsSpan(), StringComparison.Ordinal) ||
             trimmed.Contains("'''".AsSpan(), StringComparison.Ordinal)) &&
            !trimmed.Contains("=".AsSpan(), StringComparison.Ordinal) &&
            !trimmed.Contains("(".AsSpan(), StringComparison.Ordinal))
            return true;

        return false;
    }

    /// <summary>Convenience overload for string input.</summary>
    public static bool IsInCommentOrString(string line) => IsInCommentOrString(line.AsSpan());
}
