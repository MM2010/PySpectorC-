using System.Diagnostics;
using System.Globalization;
using PySpector.Core.Models;

namespace PySpector.Cli.UI;

/// <summary>
/// Performance and findings statistics collector.
/// 1:1 mapping from stats.py.
/// Tracks timing, file metrics, rule metadata, issue counters, and resource usage.
/// </summary>
public sealed class StatsCollector
{
    private readonly Stopwatch _sw = new();
    private DateTime _startTime;

    public int FilesScanned { get; set; }
    public int FilesSkipped { get; set; }
    public int ParseErrors { get; set; }
    public int TotalLoc { get; set; }
    public int RulesCount { get; set; }
    public int RegexFindings { get; set; }
    public int AstFindings { get; set; }
    public int TaintFindings { get; set; }
    public int FinalIssues { get; set; }
    public int SeverityFiltered { get; set; }
    public int BaselineIgnored { get; set; }
    public double PeakMemoryMb { get; private set; }
    public int CpuLogicalCores { get; } = Environment.ProcessorCount;

    public void Start()
    {
        _startTime = DateTime.UtcNow;
        _sw.Restart();
    }

    public void Stop()
    {
        _sw.Stop();
        try
        {
            using var process = Process.GetCurrentProcess();
            PeakMemoryMb = process.PeakWorkingSet64 / (1024.0 * 1024.0);
        }
        catch { PeakMemoryMb = 0; }
    }

    public TimeSpan Elapsed => _sw.Elapsed;
    public double LocPerSec => TotalLoc / Math.Max(_sw.Elapsed.TotalSeconds, 0.001);

    public void RecordRules(string rulesToml)
    {
        try
        {
            var ruleset = Core.Parsing.TomlRuleParser.Parse(rulesToml);
            RulesCount = ruleset.Rules.Length;
            RegexFindings = 0;
            AstFindings = 0;
            TaintFindings = 0;
        }
        catch { RulesCount = 0; }
    }

    public string RenderTable()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"╔{"═".PadRight(68, '═')}╗");
        sb.AppendLine(CultureInfo.InvariantCulture, $"║{"PySpector Scan Statistics".PadLeft(44).PadRight(68)}║");
        sb.AppendLine(CultureInfo.InvariantCulture, $"╠{"═".PadRight(32, '═')}╦{"═".PadRight(35, '═')}╣");
        AppendRow(sb, "Files Scanned", FilesScanned.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Files Skipped", FilesSkipped.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Parse Errors", ParseErrors.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Total Lines of Code", TotalLoc.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Rules Loaded", RulesCount.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(CultureInfo.InvariantCulture, $"╠{"═".PadRight(32, '═')}╬{"═".PadRight(35, '═')}╣");
        AppendRow(sb, "Regex Findings", RegexFindings.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "AST Findings", AstFindings.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Taint Findings", TaintFindings.ToString(CultureInfo.InvariantCulture));
        AppendRow(sb, "Final Issues", FinalIssues.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(CultureInfo.InvariantCulture, $"╠{"═".PadRight(32, '═')}╬{"═".PadRight(35, '═')}╣");
        AppendRow(sb, "Elapsed Time", FormattableString.Invariant($"{Elapsed.TotalSeconds:F2}s"));
        AppendRow(sb, "LoC/sec", FormattableString.Invariant($"{LocPerSec:F0}"));
        AppendRow(sb, "Peak Memory", FormattableString.Invariant($"{PeakMemoryMb:F0} MB"));
        AppendRow(sb, "CPU Cores", CpuLogicalCores.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(CultureInfo.InvariantCulture, $"╚{"═".PadRight(32, '═')}╩{"═".PadRight(35, '═')}╝");
        return sb.ToString();
    }

    private static void AppendRow(System.Text.StringBuilder sb, string label, string value)
    {
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"║  {label.PadRight(28)}║  {value.PadRight(33)}║");
    }
}
