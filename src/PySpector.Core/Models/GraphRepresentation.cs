namespace PySpector.Core.Models;

/// <summary>Block ID type — matches Rust usize.</summary>
public readonly record struct BlockId(int Value)
{
    public static implicit operator BlockId(int value) => new(value);
    public static implicit operator int(BlockId id) => id.Value;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Edge type in CFG. 1:1 from representation.rs EdgeType.</summary>
public enum EdgeType : byte
{
    Unconditional = 0,
    ConditionalTrue = 1,
    ConditionalFalse = 2,
}

/// <summary>Basic block in a control flow graph. 1:1 from representation.rs BasicBlock.</summary>
public sealed class BasicBlock
{
    public BlockId Id { get; }
    public List<AstNode> Statements { get; } = [];
    public HashSet<BlockId> Predecessors { get; } = [];
    public Dictionary<BlockId, EdgeType> Successors { get; } = [];

    public BasicBlock(BlockId id) => Id = id;
}

/// <summary>Control flow graph for a single function. 1:1 from representation.rs ControlFlowGraph.</summary>
public sealed class ControlFlowGraph
{
    public Dictionary<BlockId, BasicBlock> Blocks { get; } = [];
    public BlockId Entry { get; set; }
    public HashSet<BlockId> Exits { get; } = [];

    public ControlFlowGraph()
    {
        var entryBlock = new BasicBlock(new BlockId(0));
        Blocks[entryBlock.Id] = entryBlock;
        Entry = entryBlock.Id;
    }

    public BasicBlock AddBlock()
    {
        var newId = new BlockId(Blocks.Count);
        var block = new BasicBlock(newId);
        Blocks[newId] = block;
        return block;
    }

    public void AddEdge(BlockId from, BlockId to, EdgeType edgeType)
    {
        if (Blocks.TryGetValue(from, out var fromBlock))
            fromBlock.Successors[to] = edgeType;
        if (Blocks.TryGetValue(to, out var toBlock))
            toBlock.Predecessors.Add(from);
    }
}
