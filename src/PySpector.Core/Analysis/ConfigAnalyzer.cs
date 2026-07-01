using System.Text.RegularExpressions;
using PySpector.Core.Models;

namespace PySpector.Core.Analysis;

public static partial class ConfigAnalyzer
{
    [GeneratedRegex("\r?\n")]
    private static partial Regex NewLineRegex();

    public static List<Issue> ScanFile(string filePath, string content, RuleSet ruleset)
    {
        var issues = new List<Issue>();
        var span = content.AsSpan();
        var rules = new List<(Rule Rule, Regex Pattern)>(ruleset.Rules.Length);

        foreach (var rule in ruleset.Rules)
        {
            if (rule.Pattern is null) continue;
            if (rule.FilePattern is not null && !FileSystemMatch(rule.FilePattern, filePath)) continue;
            if (rule.IsExcluded(filePath, content, ruleset.Defaults)) continue;
            rules.Add((rule, rule.Pattern));
        }

        if (rules.Count == 0) return issues;

        int num = 0;
        foreach (var range in NewLineRegex().EnumerateSplits(span))
        {
            num++;
            var line = span[range];
            if (CommentStringDetector.IsInCommentOrString(line)) continue;

            foreach (var (rule, pattern) in rules)
            {
                if (!pattern.EnumerateMatches(line).MoveNext()) continue;
                var s = line.ToString();
                if (rule.ExcludePattern is not null && rule.ExcludePattern.IsMatch(s)) continue;
                issues.Add(new Issue(rule.Id, rule.Description, filePath, num, s,
                    rule.Severity, rule.Confidence, rule.Remediation, rule.Cwe));
            }
        }

        return issues;
    }

    private static bool FileSystemMatch(string pattern, string filePath)
    {
        var n = filePath.Replace('\\', '/');
        if (pattern.Contains('*'))
        {
            var r = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
                RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return r.IsMatch(n);
        }
        return n.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
