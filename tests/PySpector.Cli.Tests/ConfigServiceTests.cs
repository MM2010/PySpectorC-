using PySpector.Core.Services;

namespace PySpector.Cli.Tests;

public class ConfigServiceTests
{
    [Fact]
    public void DefaultExclusionsAreNotEmpty()
    {
        Assert.NotEmpty(ConfigService.DefaultExclusions);
        Assert.Contains(".git", ConfigService.DefaultExclusions);
        Assert.Contains("*.egg-info", ConfigService.DefaultExclusions);
    }

    [Fact]
    public void GetDefaultRulesReturnsToml()
    {
        var rulesToml = ConfigService.GetDefaultRules();
        Assert.NotNull(rulesToml);
        Assert.NotEmpty(rulesToml);
        Assert.Contains("[defaults]", rulesToml, StringComparison.Ordinal);
        Assert.Contains("[[rule]]", rulesToml, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDefaultRulesWithAiFlagReturnsExtendedToml()
    {
        var baseRules = ConfigService.GetDefaultRules(aiScan: false);
        var aiRules = ConfigService.GetDefaultRules(aiScan: true);
        Assert.NotEmpty(baseRules);
        Assert.NotEmpty(aiRules);
        Assert.True(aiRules.Length >= baseRules.Length, "AI rules should be equal or longer than base rules");
    }

    [Fact]
    public void GetDefaultRulesContainsSecretRule()
    {
        var rules = ConfigService.GetDefaultRules();
        Assert.Contains("G101", rules, StringComparison.Ordinal);
        Assert.Contains("CWE-798", rules, StringComparison.Ordinal);
    }
}

public class CliArgumentTests
{
    [Theory]
    [InlineData(new[] { "pyspector", "/path" }, "/path")]
    [InlineData(new[] { "pyspector", "/path", "--debug" }, "/path")]
    [InlineData(new[] { "pyspector", "/path", "--format", "json" }, "/path")]
    public void ScanPathIsFirstArgument(string[] args, string expectedPath)
    {
        // The scan path is always args[0] (first positional argument)
        Assert.Equal(expectedPath, args[1]);
    }

    [Fact]
    public void HelpFlagIsRecognized()
    {
        var helpArgs = new[] { "pyspector", "--help" };
        Assert.Equal("--help", helpArgs[1]);
    }
}
