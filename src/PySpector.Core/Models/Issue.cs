using System.Security.Cryptography;
using System.Text;

namespace PySpector.Core.Models;

/// <summary>
/// 1:1 mapping from issues.rs Issue struct.
/// Uses readonly record struct for stack-friendly allocation.
/// </summary>
public readonly record struct Issue
{
    public string RuleId { get; init; }
    public string Description { get; init; }
    public string FilePath { get; init; }
    public int LineNumber { get; init; }
    public string Code { get; init; }
    public Severity Severity { get; init; }
    public string Confidence { get; init; }
    public string Remediation { get; init; }
    public string? Cwe { get; init; }

    public Issue(
        string ruleId,
        string description,
        string filePath,
        int lineNumber,
        string code,
        Severity severity,
        string confidence,
        string remediation,
        string? cwe = null)
    {
        RuleId = ruleId;
        Description = description;
        FilePath = filePath;
        LineNumber = lineNumber;
        Code = code.Trim();
        Severity = severity;
        Confidence = confidence;
        Remediation = remediation;
        Cwe = cwe;
    }

    /// <summary>
    /// Stable SHA256 fingerprint: rule_id|file_path|line_number|code.
    /// Matches Issue.get_fingerprint() in Rust core.
    /// </summary>
    public readonly string GetFingerprint()
    {
        var input = $"{RuleId}|{FilePath}|{LineNumber}|{Code.Trim()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
