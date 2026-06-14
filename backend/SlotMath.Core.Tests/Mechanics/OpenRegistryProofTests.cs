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

        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "C" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "D" } });

        // Apply through the standard interface — no engine change needed
        var (newBoard, _) = transform.Apply(board, null);

        // Pairs are swapped: (0,0)↔(0,1) → A↔B, and (1,0)↔(1,1) → C↔D
        Assert.Equal("B", newBoard[0, 0].Symbols![0]);
        Assert.Equal("A", newBoard[0, 1].Symbols![0]);
        Assert.Equal("D", newBoard[1, 0].Symbols![0]);
        Assert.Equal("C", newBoard[1, 1].Symbols![0]);
        // Non-paired cell unchanged
        Assert.True(newBoard[0, 2].IsEmpty);
    }

    [Fact]
    public void NovelTransform_IsPure_NoDrawNoIO()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "X" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "Y" } });

        var transform = new SwapNeighborsTransform();

        // Multiple applications with same input produce identical output (purity)
        var (b1, s1) = transform.Apply(board, "test-state");
        var (b2, s2) = transform.Apply(board, "test-state");

        Assert.Equal(b1, b2);
        Assert.Equal(s1, s2);

        // Original board untouched
        Assert.Equal("X", board[0, 0].Symbols![0]);
        Assert.Equal("Y", board[0, 1].Symbols![0]);
    }

    [Fact]
    public void NovelTransform_WorksWithOtherTransforms_InPipeline()
    {
        // Compose the novel transform with a standard library transform
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "B" } });

        var swap = TransformRegistry.TryGet("swap-neighbors")!;
        var lockTransform = new LockTransform(new[] { (0, 0) });

        // Apply swap first, then lock
        (Board afterSwap, object? _) = swap.Apply(board, null);
        (Board afterLock, object? _) = lockTransform.Apply(afterSwap, null);

        // After swap: (0,0)=B, (0,1)=A; then lock (0,0)
        Assert.True(afterLock[0, 0].IsLocked);
        Assert.Equal("B", afterLock[0, 0].Symbols![0]);
        Assert.False(afterLock[0, 1].IsLocked);
        Assert.Equal("A", afterLock[0, 1].Symbols![0]);
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
    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        for (var r = 0; r < result.Rows; r++)
        {
            for (var c = 0; c < result.Cols - 1; c += 2)
            {
                var left = result[r, c];
                var right = result[r, c + 1];
                result = result.SetCell(r, c, right);
                result = result.SetCell(r, c + 1, left);
            }
        }

        return (result, state);
    }
}

/// <summary>
/// A second novel transform that mirrors the board horizontally.
/// </summary>
public sealed class MirrorTransform : IFastPathTransform
{
    public (Board NewBoard, object? NewState) Apply(Board board, object? state)
    {
        ArgumentNullException.ThrowIfNull(board);

        var result = board;
        for (var r = 0; r < result.Rows; r++)
        {
            for (var c = 0; c < result.Cols / 2; c++)
            {
                var rightCol = result.Cols - 1 - c;
                var left = result[r, c];
                var right = result[r, rightCol];
                result = result.SetCell(r, c, right);
                result = result.SetCell(r, rightCol, left);
            }
        }

        return (result, state);
    }
}
