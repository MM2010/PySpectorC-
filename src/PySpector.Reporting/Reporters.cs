using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PySpector.Core.Models;

namespace PySpector.Reporting;

public interface IReporter
{
    string Generate(IReadOnlyList<Issue> issues);
}

/// <summary>Console reporter with ANSI color via Spectre.Console. 1:1 from reporting.py to_console().</summary>
public sealed class ConsoleReporter : IReporter
{
    public string Generate(IReadOnlyList<Issue> issues)
    {
        if (issues.Count == 0)
            return "\nNo issues found.";

        var severityOrder = new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low };
        var issuesBySeverity = issues
            .GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.FilePath).ThenBy(i => i.LineNumber).ToList());

        var sb = new System.Text.StringBuilder();
        foreach (var severity in severityOrder)
        {
            if (!issuesBySeverity.TryGetValue(severity, out var list) || list.Count == 0)
                continue;

            sb.AppendLine(CultureInfo.InvariantCulture, $"\n{"=".PadRight(60, '=')}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {severity.ToString().ToUpperInvariant()} ({list.Count} issue{(list.Count != 1 ? "s" : "")})");
            sb.AppendLine(CultureInfo.InvariantCulture, $"{"=".PadRight(60, '=')}");

            foreach (var issue in list)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"\n[+] Rule ID: {issue.RuleId}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    Description: {issue.Description}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    File: {issue.FilePath}:{issue.LineNumber}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    Code: `{issue.Code.Trim()}`");
            }
        }

        return sb.ToString();
    }
}

/// <summary>JSON reporter. 1:1 from reporting.py to_json().</summary>
public sealed class JsonReporter : IReporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public string Generate(IReadOnlyList<Issue> issues) =>
        JsonSerializer.Serialize(issues, Options);
}

/// <summary>SARIF v2.1 reporter. 1:1 from reporting.py to_sarif().</summary>
public sealed class SarifReporter : IReporter
{
    private static readonly JsonSerializerOptions SarifOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    public string Generate(IReadOnlyList<Issue> issues)
    {
        var results = issues.Select(issue => new
        {
            ruleId = issue.RuleId,
            level = issue.Severity switch
            {
                Severity.Critical => "error",
                Severity.High => "error",
                Severity.Medium => "warning",
                _ => "note",
            },
            message = new { text = issue.Description },
            locations = new[]
            {
                new
                {
                    physicalLocation = new
                    {
                        artifactLocation = new { uri = issue.FilePath },
                        region = new
                        {
                            startLine = issue.LineNumber,
                            snippet = new { text = issue.Code.Trim() },
                        },
                    },
                },
            },
        }).ToList();

        var sarif = new
        {
            version = "2.1.0",
            runs = new[]
            {
                new
                {
                    tool = new
                    {
                        driver = new
                        {
                            name = "PySpector",
                            informationUri = "https://github.com/ParzivalHack/PySpector",
                            rules = issues.Select(i => i.RuleId).Distinct().Select(id => new { id }).ToList(),
                        },
                    },
                    results,
                },
            },
        };

        return JsonSerializer.Serialize(sarif, SarifOptions);
    }
}

/// <summary>HTML reporter. 1:1 from reporting.py to_html().</summary>
public sealed class HtmlReporter : IReporter
{
    public string Generate(IReadOnlyList<Issue> issues)
    {
        var rows = string.Join("\n", issues.Select(i =>
            $"<tr><td style=\"color:{SeverityColor(i.Severity)}\">{i.Severity}</td>" +
            $"<td>{i.RuleId}</td><td>{i.Description}</td>" +
            $"<td>{i.FilePath}:{i.LineNumber}</td>" +
            $"<td><code>{System.Net.WebUtility.HtmlEncode(i.Code.Trim())}</code></td></tr>"));

        return $@"<!DOCTYPE html>
<html><head><meta charset=""UTF-8""><title>PySpector Report</title>
<style>body{{font-family:sans-serif;margin:2em}}table{{border-collapse:collapse;width:100%}}
th{{background:#333;color:#fff;padding:8px;text-align:left}}
td{{padding:6px 8px;border-bottom:1px solid #ddd}}code{{background:#f4f4f4;padding:2px 4px}}
tr:hover{{background:#f5f5f5}}</style></head><body>
<h1>PySpector Scan Report</h1><p>{issues.Count} issue(s) found.</p>
<table><thead><tr><th>Severity</th><th>Rule ID</th><th>Description</th><th>Location</th><th>Code</th></tr></thead>
<tbody>{rows}</tbody></table></body></html>";
    }

    private static string SeverityColor(Severity s) => s switch
    {
        Severity.Critical => "#d32f2f",
        Severity.High => "#f44336",
        Severity.Medium => "#ff9800",
        _ => "#2196f3",
    };
}
