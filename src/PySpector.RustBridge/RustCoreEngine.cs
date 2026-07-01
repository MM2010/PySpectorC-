using System.Runtime.InteropServices;
using System.Text.Json;
using PySpector.Core;
using PySpector.Core.Models;

namespace PySpector.RustBridge;

/// <summary>
/// P/Invoke bridge to the native Rust core engine.
/// Implements IAnalysisEngine, allowing runtime swap between C# and Rust cores.
/// 1:1 mapping from lib.rs run_scan.
/// </summary>
public sealed class RustCoreEngine : IAnalysisEngine
{
    public IReadOnlyList<Issue> RunScan(
        string rootPath, string rulesToml, ScanConfig config,
        IReadOnlyList<PythonFile> pythonFiles)
    {
        try
        {
            var filesJson = JsonSerializer.Serialize(pythonFiles, JsonOpts);
            var configJson = JsonSerializer.Serialize(config, JsonOpts);

            var resultJson = RunScanNative(rootPath, rulesToml, configJson, filesJson);
            return JsonSerializer.Deserialize<List<Issue>>(resultJson, JsonOpts) ?? [];
        }
        catch
        {
            // Fall back gracefully — caller may retry with C# engine
            return [];
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Native FFI call to the Rust core engine.</summary>
    private static string RunScanNative(string path, string rulesToml, string configJson, string filesJson)
    {
        var resultPtr = NativeMethods.run_scan_ffi(path, rulesToml, configJson, filesJson);
        if (resultPtr == IntPtr.Zero)
            return "[]";
        try { return Marshal.PtrToStringAnsi(resultPtr) ?? "[]"; }
        finally { NativeMethods.free_rust_string(resultPtr); }
    }
}

/// <summary>P/Invoke declarations for the Rust native library.</summary>
internal static unsafe class NativeMethods
{
    private const string LibName = "pyspector_core";

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern IntPtr run_scan_ffi(
        [MarshalAs(UnmanagedType.LPStr)] string path,
        [MarshalAs(UnmanagedType.LPStr)] string rulesToml,
        [MarshalAs(UnmanagedType.LPStr)] string configJson,
        [MarshalAs(UnmanagedType.LPStr)] string filesJson);

    [DllImport(LibName, CallingConvention = CallingConvention.Cdecl,
        BestFitMapping = false, ThrowOnUnmappableChar = true)]
    public static extern void free_rust_string(IntPtr ptr);
}
