using System.Collections.Immutable;

namespace PySpector.Core.Models;

/// <summary>Container for parsed rules and defaults. 1:1 mapping from rules.rs RuleSet.</summary>
public sealed record RuleSet
{
    public Defaults Defaults { get; init; } = new();
    public ImmutableArray<Rule> Rules { get; init; } = [];
}
