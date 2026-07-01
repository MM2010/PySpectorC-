using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PySpector.Core;
using PySpector.Core.Analysis;
using PySpector.Core.Graph;
using PySpector.Core.Models;

namespace PySpector.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class CoreBenchmarks
{
    private RuleSet _ruleset = null!;
    private readonly List<PythonFile> _pyFiles = [];

    private string? _cachedRulesToml;

    [GlobalSetup]
    public void Setup()
    {
        _cachedRulesToml = Core.Services.ConfigService.GetDefaultRules();
        var rulesToml = _cachedRulesToml;
        _ruleset = Core.Parsing.TomlRuleParser.Parse(rulesToml);

        for (int i = 0; i < 100; i++)
        {
            var secret = "sk-ant-api03-" + new string('x', 85);
            var content = $$"""
                import os
                import json

                def process_{{i}}(data):
                    key = "{{secret}}"
                    return os.path.join(data, "..")
                """;
            _pyFiles.Add(new PythonFile($"/test/module_{i}.py", content));
        }
    }

    [Benchmark]
    public int ConfigScan100Files()
    {
        var issues = new List<Issue>();
        foreach (var f in _pyFiles)
            issues.AddRange(ConfigAnalyzer.ScanFile(f.FilePath, f.Content, _ruleset));
        return issues.Count;
    }

    [Benchmark]
    public CallGraph CallGraphBuild()
    {
        return CallGraphBuilder.Build(_pyFiles);
    }

    [Benchmark]
    public RuleSet TomlRuleParse()
    {
        return Core.Parsing.TomlRuleParser.Parse(_cachedRulesToml!);
    }
}

public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkRunner.Run<CoreBenchmarks>(args: args);
}
