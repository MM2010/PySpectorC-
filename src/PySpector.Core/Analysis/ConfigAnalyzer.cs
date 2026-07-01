using System.Text.RegularExpressions;
using PySpector.Core.Models;

namespace PySpector.Core.Analysis;

/// <summary>
/// Regex-based config/source file scanner. 1:1 mapping from config_analysis.rs.
/// Uses ReadOnlySpan for zero-allocation line iteration and regex matching.
/// </summary>
public static partial class ConfigAnalyzer
{
    // Pre-compiled regex pattern for line splitting — zero allocation
    [GeneratedRegex("\r?\n")]
    private static partial Regex NewLineRegex();

    public static List<Issue> ScanFile(string filePath, string content, RuleSet ruleset)
    {
        var issues = new List<Issue>();
        var contentSpan = content.AsSpan();

        // Pre-filter rules: build a list of applicable rules once
        var applicableRules = new List<(Rule Rule, Regex Pattern)>(ruleset.Rules.Length);
        foreach (var rule in ruleset.Rules)
        {
            if (rule.Pattern is null) continue;
            if (rule.FilePattern is not null && !FileSystemMatch(rule.FilePattern, filePath)) continue;
            if (rule.IsExcluded(filePath, content, ruleset.Defaults)) continue;
            applicableRules.Add((rule, rule.Pattern));
        }

        if (applicableRules.Count == 0) return issues;

        int lineNumber = 0;
        foreach (var lineRange in NewLineRegex().EnumerateSplits(contentSpan))
        {
            lineNumber++;
            var line = contentSpan[lineRange];

            if (CommentStringDetector.IsInCommentOrString(line))
                continue;

            foreach (var (rule, pattern) in applicableRules)
            {
                // Zero-allocation regex match via EnumerateMatches
                if (!pattern.EnumerateMatches(line).MoveNext())
                    continue;

                var lineStr = line.ToString();

                // Check line-level exclude pattern
                if (rule.ExcludePattern is not null && rule.ExcludePattern.IsMatch(lineStr))
                    continue;

                issues.Add(new Issue(
                    rule.Id,
                    rule.Description,
                    filePath,
                    lineNumber,
                    lineStr,
                    rule.Severity,
                    rule.Confidence,
                    rule.Remediation,
                    rule.Cwe));
            }
        }

        return issues;
    }

    private static bool FileSystemMatch(string pattern, string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        if (pattern.Contains('*'))
        {
            var escaped = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            // Simple glob-to-regex: no captures, DFA-safe
            var regex = new Regex(escaped,
                RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return regex.IsMatch(normalized);
        }
        return normalized.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
