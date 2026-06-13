using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Model;

namespace SlotMath.Core.Tests.Compiler;

// ═══════════════════════════════════════════════════════════════════════════
//  v3.1 validation hardening — loop caps (D6), circular subgraph refs &
//  nesting depth (D7), with precise coded errors naming the node (D20).
// ═══════════════════════════════════════════════════════════════════════════

public class CompilerValidationTests
{
    // ── Loop iteration caps (D6) ──────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)] // > CapMax
    public void LoopCap_OutOfRange_IsRejected_WithCodeAndNodeId(int cap)
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes =
            [
                new LoopNode { Id = "loop1", MaxIterations = cap },
                new MetricsSinkNode { Id = "sink" },
            ],
        };

        var errors = GraphValidator.Validate(config, pluginHost: null);

        Assert.Contains(errors, e => e.Code == ErrorCodes.OverMaxLoopCap && e.NodeId == "loop1");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000)]
    [InlineData(100_000)] // == CapMax
    public void LoopCap_InRange_HasNoLoopCapError(int cap)
    {
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Nodes =
            [
                new LoopNode { Id = "loop1", MaxIterations = cap },
                new MetricsSinkNode { Id = "sink" },
            ],
        };

        var errors = GraphValidator.Validate(config, pluginHost: null);

        Assert.DoesNotContain(errors, e => e.Code == ErrorCodes.OverMaxLoopCap);
        Assert.Equal((long)cap <= SlotMathConstants.Loop.CapMax, cap <= 100_000);
    }

    // ── Circular subgraph references (D7/G14) ─────────────────────────────

    [Fact]
    public void CircularSubgraphReference_IsRejected_WithPreciseCode()
    {
        // selfref references itself → A → A.
        var selfref = new CustomMechanic
        {
            Name = "selfref",
            Nodes = [new LibraryNode { Id = "again", MechanicName = "selfref" }],
        };
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Mechanics = new Dictionary<string, CustomMechanic> { ["selfref"] = selfref },
            Nodes = [new LibraryNode { Id = "top", MechanicName = "selfref" }],
        };

        var errors = GraphValidator.ValidateSubgraphReferences(config);

        Assert.Contains(errors, e => e.Code == ErrorCodes.CircularSubgraphReference);
    }

    [Fact]
    public void MutualSubgraphReference_AtoBtoA_IsRejected()
    {
        var a = new CustomMechanic { Name = "a", Nodes = [new LibraryNode { Id = "ra", MechanicName = "b" }] };
        var b = new CustomMechanic { Name = "b", Nodes = [new LibraryNode { Id = "rb", MechanicName = "a" }] };
        var config = new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Mechanics = new Dictionary<string, CustomMechanic> { ["a"] = a, ["b"] = b },
            Nodes = [new LibraryNode { Id = "top", MechanicName = "a" }],
        };

        var errors = GraphValidator.ValidateSubgraphReferences(config);

        Assert.Contains(errors, e => e.Code == ErrorCodes.CircularSubgraphReference);
    }

    // ── Subgraph nesting depth (D7) ───────────────────────────────────────

    private static GraphConfig ChainConfig(int levels)
    {
        var mechanics = new Dictionary<string, CustomMechanic>();
        for (var i = 0; i < levels; i++)
        {
            Node[] nodes = i < levels - 1
                ? [new LibraryNode { Id = $"n{i}", MechanicName = $"m{i + 1}" }]
                : [];
            mechanics[$"m{i}"] = new CustomMechanic { Name = $"m{i}", Nodes = nodes };
        }
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Mechanics = mechanics,
            Nodes = [new LibraryNode { Id = "top", MechanicName = "m0" }],
        };
    }

    [Fact]
    public void SubgraphNesting_OverCap_IsRejected()
    {
        // 18 mechanics chained ⇒ depth 18 > 16.
        var errors = GraphValidator.ValidateSubgraphReferences(ChainConfig(18));
        Assert.Contains(errors, e => e.Code == ErrorCodes.SubgraphNestingTooDeep && e.NodeId == "top");
    }

    [Fact]
    public void SubgraphNesting_AtCap_IsAllowed()
    {
        // 16 mechanics chained ⇒ depth 16 == cap, allowed.
        var errors = GraphValidator.ValidateSubgraphReferences(ChainConfig(16));
        Assert.DoesNotContain(errors, e => e.Code == ErrorCodes.SubgraphNestingTooDeep);
        Assert.DoesNotContain(errors, e => e.Code == ErrorCodes.CircularSubgraphReference);
    }

    [Fact]
    public void AcyclicShallowReferences_ProduceNoSubgraphErrors()
    {
        var errors = GraphValidator.ValidateSubgraphReferences(ChainConfig(3));
        Assert.Empty(errors);
    }
}
