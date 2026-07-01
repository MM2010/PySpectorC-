using PySpector.Core.Models;

namespace PySpector.Core.Graph;

/// <summary>
/// Builds an inter-procedural call graph from parsed Python files.
/// 1:1 mapping from call_graph_builder.rs.
/// Pass 1: find all function definitions → O(N) name index.
/// Pass 2: resolve call sites using O(1) index lookup.
/// </summary>
public static class CallGraphBuilder
{
    private static readonly HashSet<string> TestExclusionPatterns =
    [
        "/test", "\\test", "test_", "_test.py", "/tests/", "\\tests\\",
        "/conftest", "\\conftest", "/fixture", "\\fixture", "/mock",
        "/docs/", "\\docs\\", "/docs_src/", "/examples/", "\\examples\\",
        "/example/", "\\example\\", "/tutorial/", "/tutorials/",
        "/samples/", "/demo/", "/scripts/", "\\scripts\\",
        "/pydoc_data/", "\\pydoc_data\\",
    ];

    public static CallGraph Build(IReadOnlyList<PythonFile> pyFiles)
    {
        var productionFiles = pyFiles
            .Where(f => !IsTestFile(f.FilePath))
            .ToList();

        var callGraph = new CallGraph();

        // Pass 1: find all function definitions
        var funcNodes = new Dictionary<string, AstNode>();
        foreach (var file in productionFiles)
        {
            if (file.Ast is null) continue;
            callGraph.FileContents[file.FilePath] = file.Content;

            var funcsInFile = new List<AstNode>();
            FindFunctions(file.Ast, funcsInFile);

            foreach (var funcNode in funcsInFile)
            {
                var funcName = GetNameFromNode(funcNode);
                if (funcName is not null)
                {
                    var funcId = $"{file.FilePath}::{funcName}";
                    funcNodes[funcId] = funcNode;
                }
            }
        }

        callGraph.Functions.EnsureCapacity(funcNodes.Count);
        foreach (var kv in funcNodes)
            callGraph.Functions[kv.Key] = kv.Value;

        // Build name index: bare_name → [funcId, ...]
        var nameIndex = new Dictionary<string, List<string>>();
        foreach (var funcId in callGraph.Functions.Keys)
        {
            var bare = funcId[(funcId.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            if (!nameIndex.TryGetValue(bare, out var list))
                nameIndex[bare] = list = [];
            list.Add(funcId);

            // Also index method suffix: "ClassName.method" → "method"
            var dotIdx = bare.LastIndexOf('.');
            if (dotIdx >= 0)
            {
                var method = bare[(dotIdx + 1)..];
                if (method != bare)
                {
                    if (!nameIndex.TryGetValue(method, out var mList))
                        nameIndex[method] = mList = [];
                    mList.Add(funcId);
                }
            }
        }

        // Pass 2: resolve call sites using O(1) index
        foreach (var (funcId, funcNode) in callGraph.Functions)
        {
            var callSites = new List<AstNode>();
            FindCallSites(funcNode, callSites);
            var calls = new HashSet<string>();

            foreach (var callNode in callSites)
            {
                var calleeName = GetFullCallName(callNode);
                if (string.IsNullOrEmpty(calleeName)) continue;

                if (nameIndex.TryGetValue(calleeName, out var targets))
                    calls.UnionWith(targets);

                var dotIdx = calleeName.LastIndexOf('.');
                if (dotIdx >= 0)
                {
                    var method = calleeName[(dotIdx + 1)..];
                    if (method != calleeName && nameIndex.TryGetValue(method, out var mTargets))
                        calls.UnionWith(mTargets);
                }
            }
            callGraph.Graph[funcId] = calls;
        }

        return callGraph;
    }

    private static bool IsTestFile(string filePath)
    {
        var lower = filePath.ToLowerInvariant();
        foreach (var pattern in TestExclusionPatterns)
        {
            if (lower.Contains(pattern, StringComparison.Ordinal))
            {
                // Exclude .py mock files in /mock but not directory names that happen to contain "mock"
                if (pattern == "/mock" && lower.EndsWith(".py", StringComparison.Ordinal))
                    return true;
                if (pattern != "/mock")
                    return true;
            }
        }
        return false;
    }

    internal static void FindFunctions(AstNode node, List<AstNode> functions)
    {
        if (node.NodeType is "FunctionDef" or "AsyncFunctionDef")
            functions.Add(node);

        if (node.Children is not null)
        {
            foreach (var childList in node.Children.Values)
                foreach (var child in childList)
                    FindFunctions(child, functions);
        }
    }

    internal static void FindCallSites(AstNode node, List<AstNode> sites)
    {
        if (node.NodeType == "Call")
            sites.Add(node);

        if (node.Children is not null)
        {
            foreach (var childList in node.Children.Values)
                foreach (var child in childList)
                    FindCallSites(child, sites);
        }
    }

    internal static string? GetNameFromNode(AstNode node)
    {
        if (node.Fields is not null)
        {
            foreach (var key in new[] { "name", "id" })
            {
                if (node.Fields.TryGetValue(key, out var val) && val?.ValueKind == System.Text.Json.JsonValueKind.String)
                    return val.Value.GetString();
            }
        }
        return null;
    }

    internal static string GetFullCallName(AstNode callNode)
    {
        var func = callNode.GetFirstChild("func");
        if (func is null) return string.Empty;

        if (func.NodeType == "Name")
            return GetNameFromNode(func) ?? string.Empty;

        if (func.NodeType == "Attribute")
        {
            var parts = new List<string>();
            var current = func;
            while (current.NodeType == "Attribute")
            {
                if (current.Fields is not null &&
                    current.Fields.TryGetValue("attr", out var attr) &&
                    attr?.ValueKind == System.Text.Json.JsonValueKind.String)
                    parts.Add(attr.Value.GetString()!);

                current = current.GetFirstChild("value");
                if (current is null) break;
            }
            if (current is not null)
            {
                var baseName = GetNameFromNode(current);
                if (baseName is not null) parts.Add(baseName);
            }
            parts.Reverse();
            return string.Join(".", parts);
        }

        return string.Empty;
    }
}
