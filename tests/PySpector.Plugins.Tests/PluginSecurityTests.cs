using PySpector.Core.Models;
using PySpector.Plugins;

namespace PySpector.Plugins.Tests;

public class PluginSecurityTests
{
    [Fact]
    public void ValidateSourceRejectsProcessStart()
    {
        var source = """
            using PySpector.Plugins;
            using System.Diagnostics;
            public class BadPlugin : IPySpectorPlugin
            {
                public PluginMetadata Metadata => new("bad", "1.0", "attacker", "bad");
                public bool Initialize(IReadOnlyDictionary<string, object?> config) => true;
                public PluginResult ProcessFindings(IReadOnlyList<Issue> f, string p, IReadOnlyDictionary<string, object?>? k = null)
                {
                    Process.Start("calc.exe");
                    return new PluginResult(true, "ok");
                }
            }
            """;
        var (isValid, error) = PluginSecurity.ValidateSource(source);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Contains("Process.Start", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSourceRejectsDllImport()
    {
        var source = """
            using System.Runtime.InteropServices;
            using PySpector.Plugins;
            using PySpector.Core.Models;
            public class BadPlugin : IPySpectorPlugin
            {
                [DllImport("kernel32.dll")]
                public static extern int Beep(int freq, int dur);
                public PluginMetadata Metadata => new("bad", "1.0", "attacker", "bad");
                public bool Initialize(IReadOnlyDictionary<string, object?> config) => true;
                public PluginResult ProcessFindings(IReadOnlyList<Issue> f, string p, IReadOnlyDictionary<string, object?>? k = null)
                    => new PluginResult(true, "ok");
            }
            """;
        var (isValid, error) = PluginSecurity.ValidateSource(source);
        Assert.False(isValid);
        Assert.Contains("DllImport", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSourceAcceptsSafePlugin()
    {
        var source = """
            using System.Collections.Generic;
            using PySpector.Plugins;
            using PySpector.Core.Models;
            public class SafePlugin : IPySpectorPlugin
            {
                public PluginMetadata Metadata => new("safe", "1.0", "dev", "Safe plugin", Category: "analysis");
                public bool Initialize(IReadOnlyDictionary<string, object?> config) { _config = config; return true; }
                public PluginResult ProcessFindings(IReadOnlyList<Issue> findings, string scanPath, IReadOnlyDictionary<string, object?>? kwargs = null)
                    => new PluginResult(true, $"Processed {findings.Count} findings");
                private IReadOnlyDictionary<string, object?>? _config;
            }
            """;
        var (isValid, error) = PluginSecurity.ValidateSource(source);
        Assert.True(isValid, error ?? "Expected valid, got error");
    }

    [Fact]
    public void PluginMetadataHasCorrectDefaults()
    {
        var meta = new PluginMetadata("test", "1.0", "author", "desc");
        Assert.Equal("test", meta.Name);
        Assert.Equal("1.0", meta.Version);
        Assert.Equal("general", meta.Category);
        Assert.Empty(meta.Requires);
    }

    [Fact]
    public void PluginResultSuccessHasCorrectShape()
    {
        var result = new PluginResult(true, "Done", 42, ["/tmp/output.txt"]);
        Assert.True(result.Success);
        Assert.Equal("Done", result.Message);
        Assert.Equal(42, result.Data);
        Assert.Single(result.OutputFiles);
    }

    [Fact]
    public void PluginManagerStartsEmpty()
    {
        var plugins = PluginManager.ListPlugins();
        Assert.NotNull(plugins);
    }

    [Fact]
    public void PluginManagerRegisterAndUnregister()
    {
        PluginManager.RegisterPlugin("test-plugin", "1.0", "/tmp/test.cs", trusted: false);
        Assert.Contains(PluginManager.ListPlugins(), p => p.Name == "test-plugin");
        Assert.False(PluginManager.IsTrusted("test-plugin"));

        PluginManager.SetTrusted("test-plugin", true);
        Assert.True(PluginManager.IsTrusted("test-plugin"));

        PluginManager.UnregisterPlugin("test-plugin");
        Assert.DoesNotContain(PluginManager.ListPlugins(), p => p.Name == "test-plugin");
    }
}
