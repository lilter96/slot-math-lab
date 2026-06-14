using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Tests.Mechanics;

// ═══════════════════════════════════════════════════════════════════════════
//  Invariant 2 / G12 — the standard library ships ZERO implementations of the
//  plugin contracts IEvaluator / ITransform.
//
//  These two interfaces are reserved for level-(c) plugins (sandboxed, admin-
//  registered, sampled-regime).  Every standard-library mechanic is either a
//  pure subgraph (data) or a trusted C# fast-path implementing the DISTINCT
//  IFastPathEvaluator / IFastPathTransform contracts.
//
//  This test scans the SlotMath.Core assembly by reflection and asserts no
//  concrete type implements the plugin contracts — the literal G12 DoD gate.
// ═══════════════════════════════════════════════════════════════════════════

public class InvariantTwoScanTests
{
    private static Type[] ConcreteImplementorsInCore(Type contract)
    {
        // typeof(IEvaluator).Assembly is SlotMath.Core — the standard library.
        var core = typeof(IEvaluator).Assembly;
        return core.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(contract.IsAssignableFrom)
            .ToArray();
    }

    [Fact]
    public void StandardLibrary_ShipsZeroIEvaluatorImplementations()
    {
        var impls = ConcreteImplementorsInCore(typeof(IEvaluator));
        Assert.True(impls.Length == 0,
            "Invariant 2 violation: SlotMath.Core ships IEvaluator implementations " +
            "(plugin-only contract): " + string.Join(", ", impls.Select(t => t.FullName)));
    }

    [Fact]
    public void StandardLibrary_ShipsZeroITransformImplementations()
    {
        var impls = ConcreteImplementorsInCore(typeof(ITransform));
        Assert.True(impls.Length == 0,
            "Invariant 2 violation: SlotMath.Core ships ITransform implementations " +
            "(plugin-only contract): " + string.Join(", ", impls.Select(t => t.FullName)));
    }

    [Fact]
    public void FastPathContracts_AreDistinctFromPluginContracts()
    {
        // The fast-path contracts must not extend the plugin contracts, or the
        // scan above would be trivially satisfiable while still coupling them.
        Assert.False(typeof(IEvaluator).IsAssignableFrom(typeof(IFastPathEvaluator)));
        Assert.False(typeof(IFastPathEvaluator).IsAssignableFrom(typeof(IEvaluator)));
        Assert.False(typeof(ITransform).IsAssignableFrom(typeof(IFastPathTransform)));
        Assert.False(typeof(IFastPathTransform).IsAssignableFrom(typeof(ITransform)));
    }

    [Fact]
    public void FastPathEvaluators_ImplementTheFastPathContract_NotThePluginContract()
    {
        // The sanctioned fast-paths (Lines/Ways/Cluster) implement the trusted
        // fast-path contract and must NOT implement the plugin contract.
        Type[] fastPaths =
        [
            typeof(SlotMath.Core.Mechanics.Evaluators.LinesEvaluator),
            typeof(SlotMath.Core.Mechanics.Evaluators.WaysEvaluator),
            typeof(SlotMath.Core.Mechanics.Evaluators.ClusterEvaluator),
        ];

        foreach (var t in fastPaths)
        {
            Assert.True(typeof(IFastPathEvaluator).IsAssignableFrom(t),
                $"{t.Name} should implement IFastPathEvaluator");
            Assert.False(typeof(IEvaluator).IsAssignableFrom(t),
                $"{t.Name} must NOT implement the plugin contract IEvaluator");
        }
    }
}
