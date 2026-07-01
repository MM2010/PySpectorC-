using PySpector.Core.Analysis;
using PySpector.Core.Models;
using PySpector.Core.Parsing;

namespace PySpector.Core;

/// <summary>
/// C# native implementation of the core SAST engine.
/// Wraps AnalysisOrchestrator to implement IAnalysisEngine.
/// </summary>
public sealed class CSharpCoreEngine : IAnalysisEngine
{
    public IReadOnlyList<Issue> RunScan(
        string rootPath,
        string rulesToml,
        ScanConfig config,
        IReadOnlyList<PythonFile> pythonFiles)
    {
        var ruleset = TomlRuleParser.Parse(rulesToml);
        return AnalysisOrchestrator.RunAnalysis(rootPath, config.Exclude, ruleset, pythonFiles);
    }
}

/// <summary>
/// Factory for creating the appropriate engine implementation.
/// Supports swapping between C# native and Rust FFI at runtime.
/// </summary>
public static class EngineFactory
{
    public static IAnalysisEngine Create(string? engineType = null)
    {
        return engineType?.ToLowerInvariant() switch
        {
            "rust" => CreateRustEngine(),
            _ => new CSharpCoreEngine(),
        };
    }

    private static CSharpCoreEngine CreateRustEngine()
    {
        // Try to load Rust bridge dynamically
#if RUST_BRIDGE
        try
        {
            var bridgeType = Type.GetType("PySpector.RustBridge.RustCoreEngine, PySpector.RustBridge");
            if (bridgeType is not null && Activator.CreateInstance(bridgeType) is IAnalysisEngine engine)
                return engine;
        }
        catch { /* Fall back to C# engine */ }
#endif
        return new CSharpCoreEngine();
    }
}
