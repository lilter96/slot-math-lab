using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Transforms;

namespace SlotMath.Core.Tests.Mechanics;

/// <summary>
/// Proves that the transform registry is truly open: registering a brand-new
/// ITransform implementation requires zero changes to the interpreters,
/// the Slot monad, the compiler, or any other engine code.
///
/// This test defines a novel transform (SwapNeighborsTransform) entirely
/// within the test file, registers it, and exercises it through the standard
/// ITransform interface and TransformRegistry — proving the open/closed
/// principle holds.
/// </summary>
[Collection("Registry")]
public class OpenRegistryProofTests : IDisposable
{
    public OpenRegistryProofTests()
    {
        TransformRegistry.Clear();
        // Register a novel transform that didn't exist when the engine was built
        TransformRegistry.Register("swap-neighbors", new SwapNeighborsTransform());
    }

    public void Dispose()
    {
        TransformRegistry.Clear();
    }

    [Fact]
    public void NovelTransform_IsDiscoverable_ViaRegistry()
    {
        var transform = TransformRegistry.TryGet("swap-neighbors");
        Assert.NotNull(transform);
        Assert.IsType<SwapNeighborsTransform>(transform);
    }

    [Fact]
    public void NovelTransform_AppliesCorrectly_ThroughStandardInterface()
    {
        var transform = TransformRegistry.TryGet("swap-neighbors")!;

        var state = TestBoardState.From(new string?[][]
        {
            new string?[] { "A", "B", null },
            new string?[] { "C", "D", null },
        });

        // Apply through the standard interface — no engine change needed
        var newState = transform.Apply(state);
        var newCols = GridState.Cols(newState);
        var newCells = GridState.Cells(newState);

        // Pairs are swapped: (0,0)↔(0,1) → A↔B, and (1,0)↔(1,1) → C↔D
        Assert.Equal("B", GridState.Symbol(newCells[GridState.Index(0, 0, newCols)]));
        Assert.Equal("A", GridState.Symbol(newCells[GridState.Index(0, 1, newCols)]));
        Assert.Equal("D", GridState.Symbol(newCells[GridState.Index(1, 0, newCols)]));
        Assert.Equal("C", GridState.Symbol(newCells[GridState.Index(1, 1, newCols)]));
        // Non-paired cell unchanged (still empty)
        Assert.True(GridState.IsEmpty(newCells[GridState.Index(0, 2, newCols)]));
    }

    [Fact]
    public void NovelTransform_IsPure_NoDrawNoIO()
    {
        var state = TestBoardState.From(new string?[][] { new string?[] { "X", "Y" } });
        var transform = new SwapNeighborsTransform();

        // Multiple applications with same input produce identical output (purity)
        var s1 = transform.Apply(state);
        var s2 = transform.Apply(state);

        var c1 = GridState.Cells(s1);
        var c2 = GridState.Cells(s2);
        Assert.Equal("Y", GridState.Symbol(c1[0]));
        Assert.Equal("X", GridState.Symbol(c1[1]));
        Assert.Equal(GridState.Symbol(c1[0]), GridState.Symbol(c2[0]));
        Assert.Equal(GridState.Symbol(c1[1]), GridState.Symbol(c2[1]));

        // Original state untouched (immutable)
        Assert.Equal("X", GridState.Symbol(GridState.Cells(state)[0]));
        Assert.Equal("Y", GridState.Symbol(GridState.Cells(state)[1]));
    }

    [Fact]
    public void NovelTransform_WorksWithOtherTransforms_InPipeline()
    {
        // Compose two novel transforms in a pipeline — no engine change needed.
        var state = TestBoardState.From(new string?[][] { new string?[] { "A", "B", "C" } });

        var swap = TransformRegistry.TryGet("swap-neighbors")!;
        TransformRegistry.Register("mirror-transform", new MirrorTransform());
        var mirror = TransformRegistry.TryGet("mirror-transform")!;

        // swap: [A,B,C] → [B,A,C]; mirror: [B,A,C] → [C,A,B]
        var afterSwap = swap.Apply(state);
        var afterMirror = mirror.Apply(afterSwap);

        var cols = GridState.Cols(afterMirror);
        var cells = GridState.Cells(afterMirror);
        Assert.Equal("C", GridState.Symbol(cells[GridState.Index(0, 0, cols)]));
        Assert.Equal("A", GridState.Symbol(cells[GridState.Index(0, 1, cols)]));
        Assert.Equal("B", GridState.Symbol(cells[GridState.Index(0, 2, cols)]));
    }

    [Fact]
    public void Registry_AcceptsMultipleNovelTransforms_WithoutEngineChanges()
    {
        TransformRegistry.Register("mirror-transform", new MirrorTransform());

        var swap = TransformRegistry.TryGet("swap-neighbors")!;
        var mirror = TransformRegistry.TryGet("mirror-transform")!;

        Assert.NotNull(swap);
        Assert.NotNull(mirror);
        Assert.NotSame(swap, mirror);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
//  Novel transforms defined entirely within the test — no engine changes
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// A novel transform that swaps adjacent cells in pairs: (col 0↔col 1),
/// (col 2↔col 3), etc.  This transform did not exist when the engine,
/// interpreters, or compiler were built.
/// </summary>
public sealed class SwapNeighborsTransform : IFastPathTransform
{
    public IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var cells = GridState.Cells(state);
        var cols = GridState.Cols(state);
        if (cols <= 0) cols = cells.Length;
        var rows = cols > 0 ? cells.Length / cols : 1;

        var next = (object?[])cells.Clone();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols - 1; c += 2)
            {
                var li = GridState.Index(r, c, cols);
                var ri = GridState.Index(r, c + 1, cols);
                (next[li], next[ri]) = (next[ri], next[li]);
            }
        }

        return GridState.With(state, GridState.CellsKey, next);
    }
}

/// <summary>
/// A second novel transform that mirrors the board horizontally.
/// </summary>
public sealed class MirrorTransform : IFastPathTransform
{
    public IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var cells = GridState.Cells(state);
        var cols = GridState.Cols(state);
        if (cols <= 0) cols = cells.Length;
        var rows = cols > 0 ? cells.Length / cols : 1;

        var next = (object?[])cells.Clone();
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols / 2; c++)
            {
                var leftCol = c;
                var rightCol = cols - 1 - c;
                var li = GridState.Index(r, leftCol, cols);
                var ri = GridState.Index(r, rightCol, cols);
                (next[li], next[ri]) = (next[ri], next[li]);
            }
        }

        return GridState.With(state, GridState.CellsKey, next);
    }
}
