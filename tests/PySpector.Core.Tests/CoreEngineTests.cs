using System.Collections.Immutable;
using PySpector.Core.Models;
using PySpector.Core.Parsing;
using PySpector.Core.Analysis;
using PySpector.Core.Graph;

namespace PySpector.Core.Tests;

public class CoreEngineTests
{
    [Fact]
    public void SeverityRankReturnsCorrectOrder()
    {
        Assert.Equal(4, Severity.Critical.Rank());
        Assert.Equal(3, Severity.High.Rank());
        Assert.Equal(2, Severity.Medium.Rank());
        Assert.Equal(1, Severity.Low.Rank());
    }

    [Fact]
    public void IssueGetFingerprintIsDeterministic()
    {
        var issue = new Issue("TEST001", "desc", "/test/file.py", 42, "code", Severity.High, "High", "fix");
        var fp1 = issue.GetFingerprint();
        var fp2 = issue.GetFingerprint();
        Assert.Equal(fp1, fp2);
        Assert.Equal(64, fp1.Length);
    }

    [Fact]
    public void TomlRuleParserParsesBuiltInRules()
    {
        var rulesToml = Services.ConfigService.GetDefaultRules();
        var ruleset = TomlRuleParser.Parse(rulesToml);
        Assert.True(ruleset.Rules.Length > 100, $"Expected >100 rules, got {ruleset.Rules.Length}");
        Assert.True(ruleset.Defaults.ExcludeFilePatterns.Length > 0);
    }

    [Fact]
    public void TomlRuleParserRulesHavePatternOrAst()
    {
        var rulesToml = Services.ConfigService.GetDefaultRules();
        var ruleset = TomlRuleParser.Parse(rulesToml);
        var withMatch = ruleset.Rules.Count(r => r.Pattern is not null || r.AstMatch is not null);
        Assert.True(withMatch > 50, $"Expected >50 rules with pattern/ast, got {withMatch}");
    }

    [Fact]
    public void ConfigAnalyzerDetectsHardcodedSecret()
    {
        var rulesToml = Services.ConfigService.GetDefaultRules();
        var ruleset = TomlRuleParser.Parse(rulesToml);
        var content = "SECRET_KEY = \"my-super-secret-password-here\"";
        var issues = ConfigAnalyzer.ScanFile("/app/config.py", content, ruleset);
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.RuleId == "G101B");
    }

    [Fact]
    public void ConfigAnalyzerDetectsDbPassword()
    {
        var rulesToml = Services.ConfigService.GetDefaultRules();
        var ruleset = TomlRuleParser.Parse(rulesToml);
        var content = "DATABASE_URL = \"postgresql://admin:hunter2@db.prod.example.com:5432/db\"";
        var issues = ConfigAnalyzer.ScanFile("/app/settings.py", content, ruleset);
        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.RuleId == "G121");
    }

    [Fact]
    public void ConfigAnalyzerSkipsComments()
    {
        var rulesToml = Services.ConfigService.GetDefaultRules();
        var ruleset = TomlRuleParser.Parse(rulesToml);
        var content = "# SECRET_KEY = \"commented-out-secret\"";
        var issues = ConfigAnalyzer.ScanFile("/app/comments.py", content, ruleset);
        Assert.Empty(issues);
    }

    [Fact]
    public void CommentStringDetectorIdentifiesComments()
    {
        Assert.True(CommentStringDetector.IsInCommentOrString("# this is a comment"));
        Assert.True(CommentStringDetector.IsInCommentOrString("   # indented comment"));
        Assert.False(CommentStringDetector.IsInCommentOrString("x = 'value'  # inline comment"));
    }

    [Fact]
    public void FileExclusionServiceMatchesGlobPatterns()
    {
        var exclusions = new[] { "*/tests/*", "*.pyc" };
        Assert.True(FileExclusionService.IsExcluded("/app/tests/test_main.py", exclusions));
        Assert.True(FileExclusionService.IsExcluded("C:\\app\\tests\\test_main.py", exclusions));
        Assert.False(FileExclusionService.IsExcluded("/app/src/main.py", exclusions));
    }

    [Fact]
    public void RuleIsExcludedRespectsDefaults()
    {
        var defaults = new Defaults
        {
            ExcludeFilePatterns = ["*tests*", "*docs*"],
        };
        var rule = new Rule { Id = "TEST" };
        Assert.True(rule.IsExcluded("/app/tests/test.py", "", defaults));
        Assert.True(rule.IsExcluded("/app/docs/readme.py", "", defaults));
        Assert.False(rule.IsExcluded("/app/src/main.py", "", defaults));
    }

    [Fact]
    public void CfgBuilderBuildsIfElseStructure()
    {
        var body = ImmutableArray.Create<AstNode>(
            new AstNode
            {
                NodeType = "If",
                Children = ImmutableDictionary.CreateRange([
                    KeyValuePair.Create("body", ImmutableArray.Create<AstNode>(
                        new AstNode { NodeType = "Expr", Lineno = 1 })),
                    KeyValuePair.Create("orelse", ImmutableArray.Create<AstNode>(
                        new AstNode { NodeType = "Expr", Lineno = 2 })),
                ]),
            });

        var ast = new AstNode
        {
            NodeType = "FunctionDef",
            Children = ImmutableDictionary.CreateRange([
                KeyValuePair.Create("body", body),
            ]),
        };

        var cfg = CfgBuilder.Build(ast);
        Assert.True(cfg.Blocks.Count >= 4, $"Expected >=4 blocks, got {cfg.Blocks.Count}");
    }

    [Fact]
    public void TaintOriginIsAttackerControlled()
    {
        Assert.True(TaintOrigin.HttpRequest.IsAttackerControlled());
        Assert.True(TaintOrigin.ShellSanitized.IsAttackerControlled());
        Assert.False(TaintOrigin.DeveloperDefined.IsAttackerControlled());
        Assert.True(TaintOrigin.HttpRequest.IsShellInjectable());
        Assert.False(TaintOrigin.ShellSanitized.IsShellInjectable());
    }

    [Fact]
    public void IssueDeduplicationFingerprintUnique()
    {
        var i1 = new Issue("R1", "d", "/f.py", 1, "code", Severity.High, "c", "r");
        var i2 = new Issue("R1", "d", "/f.py", 1, "code", Severity.High, "c", "r");
        var i3 = new Issue("R2", "d", "/f.py", 2, "code2", Severity.Low, "c", "r");

        Assert.Equal(i1.GetFingerprint(), i2.GetFingerprint());
        Assert.NotEqual(i1.GetFingerprint(), i3.GetFingerprint());
    }
}
