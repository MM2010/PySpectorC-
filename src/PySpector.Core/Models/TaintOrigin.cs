namespace PySpector.Core.Models;

/// <summary>
/// Provenance of a tainted value — 1:1 mapping from taint_analysis.rs TaintOrigin enum.
/// The provenance lattice (least trusted → most trusted):
///   HttpRequest → ShellSanitized → OperatorConfig → DeveloperDefined / SystemGenerated
/// </summary>
public enum TaintOrigin : byte
{
    /// <summary>Attacker-controlled: HTTP requests, CLI args.</summary>
    HttpRequest = 0,

    /// <summary>Attacker-controlled but shlex.quote() applied. Safe for shell injection.</summary>
    ShellSanitized = 1,

    /// <summary>Attacker-controlled but html.escape() applied. Safe for HTML XSS.</summary>
    HtmlSanitized = 2,

    /// <summary>Attacker-controlled but SQL sanitizer applied. Safe for SQL injection.</summary>
    SqlSanitized = 3,

    /// <summary>Operator-controlled: env vars, config files.</summary>
    OperatorConfig = 4,

    /// <summary>Developer-defined: string literals, class attributes.</summary>
    DeveloperDefined = 5,

    /// <summary>System-generated: tempfile, uuid, urandom.</summary>
    SystemGenerated = 6,

    /// <summary>Legacy external taint marker.</summary>
    External = 7,

    /// <summary>Taint from a specific function parameter.</summary>
    Param = 8,
}

public static class TaintOriginExtensions
{
    /// <summary>
    /// True if this origin is attacker-controlled and should trigger sink findings.
    /// ShellSanitized is still attacker-controlled for non-shell sinks.
    /// </summary>
    public static bool IsAttackerControlled(this TaintOrigin origin) =>
        origin is TaintOrigin.HttpRequest or TaintOrigin.External or TaintOrigin.ShellSanitized;

    /// <summary>
    /// True only for HttpRequest/External — not ShellSanitized.
    /// Used by shell injection sinks: shlex.quote is a valid mitigation.
    /// </summary>
    public static bool IsShellInjectable(this TaintOrigin origin) =>
        origin is TaintOrigin.HttpRequest or TaintOrigin.External;

    /// <summary>
    /// True if this origin should still trigger SQL sinks.
    /// ShellSanitized is still SQL-injectable (shlex.quote doesn't sanitize SQL).
    /// </summary>
    public static bool IsSqlInjectable(this TaintOrigin origin) =>
        origin is TaintOrigin.HttpRequest or TaintOrigin.External or TaintOrigin.ShellSanitized;
}
