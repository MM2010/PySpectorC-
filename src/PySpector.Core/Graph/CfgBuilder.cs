using PySpector.Core.Models;

namespace PySpector.Core.Graph;

/// <summary>
/// Builds a Control Flow Graph for a single function's AST.
/// 1:1 mapping from cfg_builder.rs.
/// Handles If, For, While, Break, With, Try/TryStar constructs.
/// </summary>
public static class CfgBuilder
{
    public static ControlFlowGraph Build(AstNode functionNode)
    {
        var cfg = new ControlFlowGraph();
        var body = functionNode.GetChildren("body");

        if (body.Length > 0)
        {
            var loopExits = new HashSet<BlockId>();
            BuildFromStatements(cfg, body, cfg.Entry, loopExits);
        }
        return cfg;
    }

    private static BlockId BuildFromStatements(
        ControlFlowGraph cfg,
        System.Collections.Immutable.ImmutableArray<AstNode> stmts,
        BlockId currentBlockId,
        HashSet<BlockId> loopExits)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt.NodeType)
            {
                case "If":
                {
                    // Add If node to current block for condition taint analysis
                    if (cfg.Blocks.TryGetValue(currentBlockId, out var block))
                        block.Statements.Add(stmt);

                    var ifBodyBlockId = cfg.AddBlock().Id;
                    var mergeBlockId = cfg.AddBlock().Id;

                    var orelse = stmt.GetChildren("orelse");
                    var elseBodyBlockId = orelse.Length > 0 ? cfg.AddBlock().Id : mergeBlockId;

                    cfg.AddEdge(currentBlockId, ifBodyBlockId, EdgeType.ConditionalTrue);
                    cfg.AddEdge(currentBlockId, elseBodyBlockId, EdgeType.ConditionalFalse);

                    var ifBody = stmt.GetChildren("body");
                    if (ifBody.Length > 0)
                    {
                        var finalIfBlock = BuildFromStatements(cfg, ifBody, ifBodyBlockId, loopExits);
                        cfg.AddEdge(finalIfBlock, mergeBlockId, EdgeType.Unconditional);
                    }

                    if (orelse.Length > 0)
                    {
                        var finalElseBlock = BuildFromStatements(cfg, orelse, elseBodyBlockId, loopExits);
                        cfg.AddEdge(finalElseBlock, mergeBlockId, EdgeType.Unconditional);
                    }

                    currentBlockId = mergeBlockId;
                    break;
                }

                case "For":
                case "While":
                {
                    if (cfg.Blocks.TryGetValue(currentBlockId, out var block))
                        block.Statements.Add(stmt);

                    var loopBodyId = cfg.AddBlock().Id;
                    var afterLoopId = cfg.AddBlock().Id;

                    cfg.AddEdge(currentBlockId, loopBodyId, EdgeType.Unconditional);
                    loopExits.Add(afterLoopId);

                    var loopBody = stmt.GetChildren("body");
                    if (loopBody.Length > 0)
                    {
                        var finalLoopBlock = BuildFromStatements(cfg, loopBody, loopBodyId, loopExits);
                        cfg.AddEdge(finalLoopBlock, loopBodyId, EdgeType.Unconditional);
                    }

                    loopExits.Remove(afterLoopId);
                    cfg.AddEdge(currentBlockId, afterLoopId, EdgeType.Unconditional);
                    currentBlockId = afterLoopId;
                    break;
                }

                case "Break":
                {
                    if (loopExits.Count > 0)
                    {
                        // Connect to innermost loop exit (last added)
                        var exitId = loopExits.Last();
                        cfg.AddEdge(currentBlockId, exitId, EdgeType.Unconditional);
                    }
                    currentBlockId = cfg.AddBlock().Id;
                    break;
                }

                case "With":
                {
                    if (cfg.Blocks.TryGetValue(currentBlockId, out var block))
                        block.Statements.Add(stmt);

                    var withBody = stmt.GetChildren("body");
                    if (withBody.Length > 0)
                        currentBlockId = BuildFromStatements(cfg, withBody, currentBlockId, loopExits);
                    break;
                }

                case "Try":
                case "TryStar":
                {
                    var tryBody = stmt.GetChildren("body");
                    if (tryBody.Length > 0)
                        currentBlockId = BuildFromStatements(cfg, tryBody, currentBlockId, loopExits);

                    var elseBody = stmt.GetChildren("orelse");
                    if (elseBody.Length > 0)
                        currentBlockId = BuildFromStatements(cfg, elseBody, currentBlockId, loopExits);
                    break;
                }

                default:
                {
                    if (cfg.Blocks.TryGetValue(currentBlockId, out var block))
                        block.Statements.Add(stmt);
                    break;
                }
            }
        }

        cfg.Exits.Add(currentBlockId);
        return currentBlockId;
    }
}
