using PySpector.Core.Models;

namespace PySpector.Core.Analysis;

/// <summary>
/// AST tree walk pattern matcher. 1:1 mapping from ast_analysis.rs.
/// Pre-filters applicable rules per file, then walks the AST recursively.
/// </summary>
public static class AstAnalyzer
{
    public static List<Issue> ScanAst(AstNode ast, string filePath, string content, RuleSet ruleset)
    {
        // Pre-filter: only rules with ast_match that are not excluded for this file
        var astRules = ruleset.Rules
            .Where(r => r.AstMatch is not null)
            .Where(r => !r.IsExcluded(filePath, content, ruleset.Defaults))
            .ToList();

        if (astRules.Count == 0)
            return [];

        var issues = new List<Issue>();
        WalkAst(ast, filePath, content, astRules, issues);
        return issues;
    }

    private static void WalkAst(AstNode node, string filePath, string content,
        List<Rule> rules, List<Issue> issues)
    {
        foreach (var rule in rules)
        {
            if (CheckNodeMatch(node, rule.AstMatch!))
            {
                var lines = content.Split('\n');
                var lineIdx = Math.Max(0, node.Lineno - 1);
                var lineContent = lineIdx < lines.Length ? lines[lineIdx] : string.Empty;

                // Respect line-level exclude_pattern
                if (rule.ExcludePattern is not null && rule.ExcludePattern.IsMatch(lineContent))
                    continue;

                issues.Add(new Issue(
                    rule.Id,
                    rule.Description,
                    filePath,
                    node.Lineno,
                    lineContent,
                    rule.Severity,
                    rule.Confidence,
                    rule.Remediation,
                    rule.Cwe));
            }
        }

        // Recurse into all children
        if (node.Children is not null)
        {
            foreach (var childList in node.Children.Values)
            {
                foreach (var child in childList)
                {
                    WalkAst(child, filePath, content, rules, issues);
                }
            }
        }
    }

    /// <summary>
    /// Check if an AST node matches a pattern like "Call(func=Attribute(attr=load))".
    /// 1:1 from ast_analysis.rs check_node_match().
    /// </summary>
    internal static bool CheckNodeMatch(AstNode node, string matchPattern)
    {
        string nodeTypeMatch;
        string? propsStr = null;

        var openParen = matchPattern.IndexOf('(');
        if (openParen >= 0)
        {
            nodeTypeMatch = matchPattern[..openParen];
            var closeParen = matchPattern.LastIndexOf(')');
            if (closeParen > openParen)
                propsStr = matchPattern[(openParen + 1)..closeParen];
        }
        else
        {
            nodeTypeMatch = matchPattern;
        }

        if (node.NodeType != nodeTypeMatch) return false;

        if (propsStr is not null)
        {
            foreach (var prop in propsStr.Split(','))
            {
                var trimmed = prop.Trim();
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx < 0) continue;

                var path = trimmed[..eqIdx].Trim();
                var expectedValue = trimmed[(eqIdx + 1)..].Trim();
                if (!NodeHasProperty(node, path.Split('.'), expectedValue))
                    return false;
            }
        }

        return true;
    }

    private static bool NodeHasProperty(AstNode node, string[] path, string expectedValue)
    {
        if (path.Length == 0) return false;

        var currentPart = path[0];
        var remainingPath = path[1..];

        if (remainingPath.Length == 0)
        {
            // Terminal: check fields
            if (node.Fields is not null && node.Fields.TryGetValue(currentPart, out var fieldValue))
            {
                return fieldValue switch
                {
                    null => false,
                    { ValueKind: System.Text.Json.JsonValueKind.String } =>
                        fieldValue.Value.GetString() == expectedValue,
                    { ValueKind: System.Text.Json.JsonValueKind.True } =>
                        expectedValue.Equals("true", StringComparison.OrdinalIgnoreCase),
                    { ValueKind: System.Text.Json.JsonValueKind.False } =>
                        expectedValue.Equals("false", StringComparison.OrdinalIgnoreCase),
                    { ValueKind: System.Text.Json.JsonValueKind.Number } =>
                        fieldValue.Value.GetRawText() == expectedValue,
                    _ => false,
                };
            }
            return false;
        }

        // Navigate children
        var children = node.GetChildren(currentPart);
        if (remainingPath.Length > 0 && remainingPath[0] == "*")
        {
            var pathAfterWildcard = remainingPath[1..];
            foreach (var child in children)
            {
                if (NodeHasProperty(child, pathAfterWildcard, expectedValue))
                    return true;
            }
        }
        else if (children.Length > 0)
        {
            return NodeHasProperty(children[0], remainingPath, expectedValue);
        }

        return false;
    }
}
