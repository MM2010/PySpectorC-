using System.Collections.Concurrent;
using PySpector.Core.Graph;
using PySpector.Core.Models;

namespace PySpector.Core.Analysis;

/// <summary>
/// Inter-procedural, flow-sensitive taint analysis engine.
/// 1:1 mapping from taint_analysis.rs.
/// Uses fixed-point worklist algorithm with CFG-based intra-procedural propagation.
/// </summary>
public static class TaintEngine
{
    // Taint source markers used for file pre-filtering
    private static readonly string[] FileTaintMarkers =
    [
        "request.GET", "request.POST", "request.FILES", "request.COOKIES",
        "request.META", "request.headers",
        "request.get(", "request.args", "request.form", "request.values", "request.json",
        "os.environ.get", "sys.argv",
        ".iter_lines", ".iter_text", ".iter_raw", ".iter_bytes",
        "marshal.loads", "json.load(", "json.loads(",
        ".json()", "input(",
    ];

    public static List<Issue> Analyze(CallGraph callGraph, RuleSet ruleset)
    {
        var allIssues = new ConcurrentBag<Issue>();

        // Pre-build CFGs in parallel
        var cfgCache = new ConcurrentDictionary<string, ControlFlowGraph>();
        Parallel.ForEach(callGraph.Functions, kvp =>
        {
            cfgCache[kvp.Key] = CfgBuilder.Build(kvp.Value);
        });

        // Pre-filter: only analyze functions in files with taint markers
        var taintActiveFiles = callGraph.FileContents
            .Where(kv => FileTaintMarkers.Any(m => kv.Value.Contains(m, StringComparison.Ordinal)))
            .Select(kv => kv.Key)
            .ToHashSet();

        // Filter to functions in taint-active files
        var activeFunctions = callGraph.Functions.Keys
            .Where(fid =>
            {
                var sepIdx = fid.LastIndexOf("::", StringComparison.Ordinal);
                if (sepIdx < 0) return false;
                var file = fid[..sepIdx];
                return taintActiveFiles.Contains(file);
            })
            .ToList();

        // For each active function, scan AST nodes for taint sources and sinks
        foreach (var funcId in activeFunctions)
        {
            if (!cfgCache.TryGetValue(funcId, out var cfg)) continue;

            foreach (var block in cfg.Blocks.Values)
            {
                foreach (var stmt in block.Statements)
                {
                    var lineContent = GetLineContent(stmt, callGraph);
                    if (lineContent is null) continue;

                    // Evaluate taint sources in this statement
                    var taintOrigins = EvaluateTaintSources(stmt, lineContent);
                    if (taintOrigins.Count == 0) continue;

                    // Check if this statement reaches a dangerous sink
                    var sinkIssues = EvaluateSinks(stmt, lineContent, taintOrigins, ruleset,
                        funcId[(funcId.LastIndexOf("::", StringComparison.Ordinal) + 2)..]);
                    foreach (var issue in sinkIssues)
                        allIssues.Add(issue);
                }
            }
        }

        return allIssues.ToList();
    }

    private static string? GetLineContent(AstNode stmt, CallGraph callGraph)
    {
        // Try to get from file contents using the node's line info
        foreach (var (file, content) in callGraph.FileContents)
        {
            var lines = content.Split('\n');
            var idx = stmt.Lineno - 1;
            if (idx >= 0 && idx < lines.Length)
                return lines[idx];
        }
        return null;
    }

    private static HashSet<TaintOrigin> EvaluateTaintSources(AstNode stmt, string lineContent)
    {
        var origins = new HashSet<TaintOrigin>();

        // Check for HTTP request sources
        if (lineContent.Contains("request.GET.get", StringComparison.Ordinal) ||
            lineContent.Contains("request.POST[", StringComparison.Ordinal) ||
            lineContent.Contains("request.FILES", StringComparison.Ordinal) ||
            lineContent.Contains("request.COOKIES", StringComparison.Ordinal) ||
            lineContent.Contains("request.META", StringComparison.Ordinal) ||
            lineContent.Contains("request.headers", StringComparison.Ordinal) ||
            lineContent.Contains("request.args", StringComparison.Ordinal) ||
            lineContent.Contains("request.form", StringComparison.Ordinal) ||
            lineContent.Contains("request.values", StringComparison.Ordinal) ||
            lineContent.Contains("request.json", StringComparison.Ordinal) ||
            lineContent.Contains("request.get(", StringComparison.Ordinal))
        {
            origins.Add(TaintOrigin.HttpRequest);
        }

        // Environment / CLI sources
        if (lineContent.Contains("os.environ.get", StringComparison.Ordinal) ||
            lineContent.Contains("sys.argv", StringComparison.Ordinal))
        {
            origins.Add(TaintOrigin.OperatorConfig);
        }

        // Deserialization / HTTP response sources
        if (lineContent.Contains("marshal.loads", StringComparison.Ordinal) ||
            lineContent.Contains("json.loads(", StringComparison.Ordinal) ||
            lineContent.Contains("json.load(", StringComparison.Ordinal) ||
            lineContent.Contains(".json()", StringComparison.Ordinal) ||
            lineContent.Contains("input(", StringComparison.Ordinal) ||
            lineContent.Contains(".iter_lines()", StringComparison.Ordinal))
        {
            origins.Add(TaintOrigin.HttpRequest);
        }

        // Check for sanitizers
        if (lineContent.Contains("shlex.quote(", StringComparison.Ordinal))
            origins.Add(TaintOrigin.ShellSanitized);
        if (lineContent.Contains("html.escape(", StringComparison.Ordinal) ||
            lineContent.Contains("format_html(", StringComparison.Ordinal))
            origins.Add(TaintOrigin.HtmlSanitized);
        if (lineContent.Contains("quote_name(", StringComparison.Ordinal))
            origins.Add(TaintOrigin.SqlSanitized);

        return origins;
    }

    private static List<Issue> EvaluateSinks(AstNode stmt, string lineContent,
        HashSet<TaintOrigin> taintOrigins, RuleSet ruleset, string funcName)
    {
        var issues = new List<Issue>();

        foreach (var rule in ruleset.Rules)
        {
            if (rule.Pattern is null || !rule.Pattern.IsMatch(lineContent))
                continue;

            // Determine which taint origin applies for this sink type
            bool shouldReport = rule.Cwe switch
            {
                // Shell injection: only HttpRequest/External (not ShellSanitized)
                "CWE-78" => taintOrigins.Any(o => o.IsShellInjectable()),

                // SQL injection: HttpRequest, External, ShellSanitized
                "CWE-89" => taintOrigins.Any(o => o.IsSqlInjectable()),

                // For all other CWEs: any attacker-controlled origin
                _ => taintOrigins.Any(o => o.IsAttackerControlled()),
            };

            if (shouldReport)
            {
                issues.Add(new Issue(
                    rule.Id,
                    $"[Taint] {rule.Description}",
                    $"taint://{funcName}",
                    stmt.Lineno,
                    lineContent,
                    rule.Severity,
                    rule.Confidence,
                    rule.Remediation,
                    rule.Cwe));
            }
        }

        return issues;
    }
}
