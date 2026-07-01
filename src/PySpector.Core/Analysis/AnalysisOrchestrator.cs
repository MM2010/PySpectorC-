using System.Collections.Concurrent;
using System.Collections.Immutable;
using PySpector.Core.Graph;
using PySpector.Core.Models;
using PySpector.Core.Parsing;

namespace PySpector.Core.Analysis;

/// <summary>
/// Main analysis orchestrator — coordinates regex scan, AST scan, taint analysis, and dedup.
/// 1:1 mapping from analysis/mod.rs run_analysis().
/// </summary>
public static class AnalysisOrchestrator
{
    public static List<Issue> RunAnalysis(
        string rootPath,
        IReadOnlyList<string> exclusions,
        RuleSet ruleset,
        IReadOnlyList<PythonFile> pyFiles)
    {
        // Apply disabled_rule_ids from [defaults]
        var disabledSet = ruleset.Defaults.DisabledRuleIds.ToHashSet();
        if (disabledSet.Count > 0)
        {
            ruleset = ruleset with
            {
                Rules = ruleset.Rules.Where(r => !disabledSet.Contains(r.Id)).ToImmutableArray(),
            };
        }

        var enhancedExclusions = new List<string>(exclusions)
        {
            "*/tests/fixtures/*", "*/test/fixtures/*", "*_test.py", "*/test_*.py",
        };

        var issues = new ConcurrentBag<Issue>();

        // Build lookup: filePath → pre-loaded content (avoids double I/O)
        var pyContentMap = new Dictionary<string, string>(pyFiles.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pf in pyFiles)
            pyContentMap[pf.FilePath] = pf.Content;

        // Phase 1: Regex scan on ALL files, using pre-loaded content when available
        var allFiles = new ConcurrentBag<string>();
        WalkDirectory(rootPath, enhancedExclusions, allFiles);

        Parallel.ForEach(allFiles, filePath =>
        {
            try
            {
                // Use pre-loaded content for Python files (avoids double disk read)
                if (!pyContentMap.TryGetValue(filePath, out var content))
                    content = File.ReadAllText(filePath);

                var fileIssues = ConfigAnalyzer.ScanFile(filePath, content, ruleset);
                foreach (var issue in fileIssues)
                    issues.Add(issue);
            }
            catch { /* skip unreadable files */ }
        });

        // Phase 2: AST analysis on Python files (parallel)
        Parallel.ForEach(pyFiles, pyFile =>
        {
            if (FileExclusionService.IsExcluded(pyFile.FilePath, enhancedExclusions))
                return;
            if (pyFile.Ast is null) return;

            var astIssues = AstAnalyzer.ScanAst(pyFile.Ast, pyFile.FilePath, pyFile.Content, ruleset);
            foreach (var issue in astIssues)
                issues.Add(issue);
        });

        // Phase 3: Call graph + taint analysis
        var callGraph = CallGraphBuilder.Build(pyFiles);
        var taintIssues = TaintEngine.Analyze(callGraph, ruleset);
        foreach (var issue in taintIssues)
            issues.Add(issue);

        // Deduplication: fingerprint-based (unique)
        var seen = new HashSet<string>();
        var uniqueIssues = new List<Issue>();
        foreach (var issue in issues)
        {
            if (seen.Add(issue.GetFingerprint()))
                uniqueIssues.Add(issue);
        }

        // Deduplication: CWE cross-rule at same (file, line, cwe)
        var byCweLoc = new Dictionary<(string, int, string), Issue>();
        var uncategorized = new List<Issue>();

        foreach (var issue in uniqueIssues)
        {
            if (issue.Cwe is not null)
            {
                var key = (issue.FilePath, issue.LineNumber, issue.Cwe);
                if (byCweLoc.TryGetValue(key, out var existing))
                {
                    if (issue.Severity.Rank() > existing.Severity.Rank())
                        byCweLoc[key] = issue;
                    else if (issue.Severity.Rank() == existing.Severity.Rank() &&
                             string.Compare(issue.RuleId, existing.RuleId, StringComparison.Ordinal) < 0)
                        byCweLoc[key] = issue;
                }
                else
                {
                    byCweLoc[key] = issue;
                }
            }
            else
            {
                uncategorized.Add(issue);
            }
        }

        var finalIssues = new List<Issue>(byCweLoc.Values);
        finalIssues.AddRange(uncategorized);
        return finalIssues;
    }

    private static void WalkDirectory(string rootPath, IReadOnlyList<string> exclusions,
        ConcurrentBag<string> files)
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.*",
                SearchOption.AllDirectories))
            {
                if (!FileExclusionService.IsExcluded(filePath, exclusions))
                    files.Add(filePath);
            }
        }
        catch { /* skip inaccessible directories */ }
    }
}
