using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PySpector.Plugins;

/// <summary>
/// Plugin security sandbox — analyzes plugin C# source code for dangerous API calls.
/// 1:1 from plugin_system.py PluginSecurity.
/// </summary>
public static class PluginSecurity
{
    private static readonly HashSet<string> FatalNamespaces =
    [
        "System.Diagnostics.Process",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.IO.File",
        "System.IO.Directory",
        "System.Net.Sockets",
        "System.Net.WebClient",
        "System.Management",
        "Microsoft.Win32.Registry",
    ];

    private static readonly HashSet<string> FatalMethods =
    [
        "Process.Start", "DllImport", "Marshal", "Type.GetType",
        "Assembly.Load", "Assembly.LoadFrom", "Assembly.LoadFile",
        "AppDomain.CreateInstance", "Activator.CreateInstance",
        "CodeDom", "CompilerParameters",
    ];

    private static readonly HashSet<string> WarningMethods =
    [
        "File.Write", "File.Delete", "File.Move", "File.Copy",
        "Directory.Delete", "Directory.Move",
        "HttpClient", "WebRequest", "Socket",
    ];

    /// <summary>Validate plugin source code. Returns (isValid, errorMessage).</summary>
    public static (bool IsValid, string? Error) ValidateSource(string sourceCode)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetCompilationUnitRoot();

            var errors = new ConcurrentBag<string>();

            // Check using directives for fatal namespaces
            foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
            {
                var ns = usingDirective.Name?.ToString() ?? "";
                foreach (var fatal in FatalNamespaces)
                {
                    if (ns.StartsWith(fatal, StringComparison.Ordinal))
                        errors.Add($"FATAL: Using forbidden namespace '{ns}' (matches '{fatal}')");
                }
            }

            // Check method invocations for fatal API calls
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var expr = invocation.Expression.ToString();
                foreach (var fatal in FatalMethods)
                {
                    if (expr.Contains(fatal, StringComparison.Ordinal))
                        errors.Add($"FATAL: Forbidden API call '{expr}' detected");
                }
                foreach (var warn in WarningMethods)
                {
                    if (expr.Contains(warn, StringComparison.Ordinal))
                        errors.Add($"WARNING: Potentially dangerous API call '{expr}' detected");
                }
            }

            // Check for DllImport attributes
            foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                if (attr.Name.ToString().Contains("DllImport", StringComparison.Ordinal))
                    errors.Add("FATAL: DllImport attribute detected — native code execution forbidden");
            }

            if (errors.IsEmpty) return (true, null);

            return (false, string.Join("; ", errors));
        }
        catch (Exception ex)
        {
            return (false, $"Plugin validation error: {ex.Message}");
        }
    }

    /// <summary>Compile and load a plugin from source, validating security first.</summary>
    public static (IPySpectorPlugin? Plugin, string? Error) LoadFromSource(
        string sourceCode, string pluginName)
    {
        // Security validation
        var (isValid, secError) = ValidateSource(sourceCode);
        if (!isValid) return (null, $"Security check failed: {secError}");

        try
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .ToList();

            var compilation = CSharpCompilation.Create(
                $"Plugin_{pluginName}",
                [tree],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var ms = new MemoryStream();
            var result = compilation.Emit(ms);
            if (!result.Success)
            {
                var diagErrors = string.Join("; ", result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
                return (null, $"Compilation failed: {diagErrors}");
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());

            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IPySpectorPlugin).IsAssignableFrom(t) && !t.IsInterface);

            if (pluginType is null)
                return (null, "No type implementing IPySpectorPlugin found in plugin source.");

            var plugin = (IPySpectorPlugin)Activator.CreateInstance(pluginType)!;
            return (plugin, null);
        }
        catch (Exception ex)
        {
            return (null, $"Plugin load error: {ex.Message}");
        }
    }
}
