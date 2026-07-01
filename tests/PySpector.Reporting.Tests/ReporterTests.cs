using PySpector.Core.Models;
using PySpector.Reporting;

namespace PySpector.Reporting.Tests;

public class ReporterTests
{
    private static readonly List<Issue> SampleIssues =
    [
        new("G121", "DB password exposed", "/app/db.py", 1, "postgresql://admin:hunter2@db.example.com:5432/db",
            Severity.Critical, "High", "Use env vars", "CWE-798"),
        new("G101B", "Hardcoded secret key", "/app/settings.py", 4, "SECRET_KEY = \"my-secret-key-change-me\"",
            Severity.High, "High", "Use os.environ.get", "CWE-798"),
        new("PY203", "Insecure SSL protocol", "/app/tls.py", 12, "ssl.PROTOCOL_TLSv1",
            Severity.High, "Medium", "Use PROTOCOL_TLS", "CWE-327"),
    ];

    private static readonly List<Issue> EmptyIssues = [];

    [Fact]
    public void ConsoleReporterEmptyIssuesReturnsNoIssuesMessage()
    {
        var reporter = new ConsoleReporter();
        var output = reporter.Generate(EmptyIssues);
        Assert.Contains("No issues found", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleReporterGroupsBySeverity()
    {
        var reporter = new ConsoleReporter();
        var output = reporter.Generate(SampleIssues);
        Assert.Contains("CRITICAL", output, StringComparison.Ordinal);
        Assert.Contains("HIGH", output, StringComparison.Ordinal);
        Assert.Contains("G121", output, StringComparison.Ordinal);
        Assert.Contains("G101B", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonReporterProducesValidJsonArray()
    {
        var reporter = new JsonReporter();
        var output = reporter.Generate(SampleIssues);
        Assert.StartsWith("[", output, StringComparison.Ordinal);
        Assert.EndsWith("]", output, StringComparison.Ordinal);
        Assert.Contains("\"rule_id\"", output, StringComparison.Ordinal);
        Assert.Contains("\"cwe\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonReporterEmptyIssuesProducesEmptyArray()
    {
        var reporter = new JsonReporter();
        var output = reporter.Generate(EmptyIssues);
        Assert.Contains("[", output, StringComparison.Ordinal);
        Assert.Contains("]", output, StringComparison.Ordinal);
        var trimmed = output.Trim();
        Assert.Equal("[]", trimmed);
    }

    [Fact]
    public void SarifReporterProducesValidSarif()
    {
        var reporter = new SarifReporter();
        var output = reporter.Generate(SampleIssues);
        Assert.Contains("\"version\"", output, StringComparison.Ordinal);
        Assert.Contains("\"2.1.0\"", output, StringComparison.Ordinal);
        Assert.Contains("\"runs\"", output, StringComparison.Ordinal);
        Assert.Contains("\"results\"", output, StringComparison.Ordinal);
        Assert.Contains("\"tool\"", output, StringComparison.Ordinal);
        Assert.Contains("PySpector", output, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlReporterProducesValidHtml()
    {
        var reporter = new HtmlReporter();
        var output = reporter.Generate(SampleIssues);
        Assert.Contains("<!DOCTYPE html>", output, StringComparison.Ordinal);
        Assert.Contains("<table>", output, StringComparison.Ordinal);
        Assert.Contains("<th>Severity</th>", output, StringComparison.Ordinal);
        Assert.Contains("<th>Rule ID</th>", output, StringComparison.Ordinal);
        Assert.Contains("3 issue(s) found", output, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlReporterEmptyIssuesShowsZero()
    {
        var reporter = new HtmlReporter();
        var output = reporter.Generate(EmptyIssues);
        Assert.Contains("0 issue(s) found", output, StringComparison.Ordinal);
    }
}
