using System.Security.Cryptography;
using System.Text;

namespace PySpector.Core.Models;

/// <summary>1:1 mapping from issues.rs Severity enum.</summary>
public enum Severity : byte
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public static class SeverityExtensions
{
    /// <summary>Critical=4, High=3, Medium=2, Low=1 — matches severity_rank() in analysis/mod.rs.</summary>
    public static byte Rank(this Severity severity) => severity switch
    {
        Severity.Critical => 4,
        Severity.High => 3,
        Severity.Medium => 2,
        Severity.Low => 1,
        _ => 0,
    };
}
