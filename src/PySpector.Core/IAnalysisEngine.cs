using PySpector.Core.Models;

namespace PySpector.Core;

/// <summary>
/// Strategy interface for the core analysis engine.
/// Enables swapping between C# native and Rust FFI implementations.
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>
    /// Run a full SAST scan. 1:1 mapping from lib.rs run_scan.
    /// </summary>
    /// <param name="rootPath">Root directory path to scan.</param>
    /// <param name="rulesToml">TOML rule definitions as string.</param>
    /// <param name="config">Scan configuration with exclusions, severity, etc.</param>
    /// <param name="pythonFiles">Pre-parsed Python files with AST data.</param>
    /// <returns>List of detected security issues.</returns>
    IReadOnlyList<Issue> RunScan(
        string rootPath,
        string rulesToml,
        ScanConfig config,
        IReadOnlyList<PythonFile> pythonFiles);
}

/// <summary>Scan configuration — corresponds to Python config dict.</summary>
public sealed record ScanConfig
{
    public IReadOnlyList<string> Exclude { get; init; } = [];
    public string Severity { get; init; } = "LOW";
}
