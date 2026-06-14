using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Tests.Mechanics;

[Collection("Registry")]
public class TransformRegistryTests : IDisposable
{
    // Ensure clean state for every test
    public TransformRegistryTests()
    {
        TransformRegistry.Clear();
    }

    public void Dispose()
    {
        TransformRegistry.Clear();
    }

    [Fact]
    public void Registry_StartsEmpty()
    {
        Assert.Empty(TransformRegistry.All);
    }

    [Fact]
    public void Registry_Register_AddsTransform()
    {
        var transform = new TestTransform();
        TransformRegistry.Register("test-transform", transform);
        Assert.Contains("test-transform", TransformRegistry.All.Keys);
        Assert.Same(transform, TransformRegistry.All["test-transform"]);
    }

    [Fact]
    public void Registry_Register_DuplicateName_Throws()
    {
        TransformRegistry.Register("dup-transform", new TestTransform());
        Assert.Throws<InvalidOperationException>(() =>
            TransformRegistry.Register("dup-transform", new TestTransform()));
    }

    [Fact]
    public void Registry_Clear_RemovesAllTransforms()
    {
        TransformRegistry.Register("t1", new TestTransform());
        TransformRegistry.Clear();
        Assert.Empty(TransformRegistry.All);
    }

    [Fact]
    public void Registry_TryGet_ReturnsNullForMissing()
    {
        Assert.Null(TransformRegistry.TryGet("nonexistent"));
    }

    [Fact]
    public void Registry_TryGet_ReturnsRegisteredTransform()
    {
        var t = new TestTransform();
        TransformRegistry.Register("my-transform", t);
        Assert.Same(t, TransformRegistry.TryGet("my-transform"));
    }

    [Fact]
    public void ITransform_Apply_IsPureFunction()
    {
        var board = new Board(3, 5);
        var transform = new TestTransform();

        var (b1, s1) = transform.Apply(board, null);
        var (b2, s2) = transform.Apply(board, null);

        // Same input → same output (purity)
        Assert.Equal(b1, b2);
        Assert.Equal(s1, s2);

        // Original board untouched (no side effects)
        Assert.Equal(3, board.Rows);
    }

    [Fact]
    public void ITransform_CanModifyBoardAndState()
    {
        var board = new Board(3, 5);
        var transform = new CellAddingTransform();

        var (newBoard, newState) = transform.Apply(board, "counter:0");
        Assert.NotEqual(board, newBoard);
        Assert.Equal("counter:1", newState);
    }

    [Fact]
    public void ITransform_WithNullBoard_ThrowsArgumentNullException()
    {
        var transform = new TestTransform();
        Assert.Throws<ArgumentNullException>(() => transform.Apply(null!, null));
    }

    [Fact]
    public void ITransform_PreservesUnrelatedState()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 4, new BoardCell { Symbols = new[] { "B" }, IsLocked = true });

        var transform = new TestTransform();
        var (newBoard, _) = transform.Apply(board, null);

        // TestTransform returns the board unchanged
        Assert.Equal(board, newBoard);
    }

    // ── Test transform implementations ─────────────────────────────────

    private sealed class TestTransform : IFastPathTransform
    {
        public (Board NewBoard, object? NewState) Apply(Board board, object? state)
        {
            ArgumentNullException.ThrowIfNull(board);
            return (board, state);
        }
    }

    private sealed class CellAddingTransform : IFastPathTransform
    {
        public (Board NewBoard, object? NewState) Apply(Board board, object? state)
        {
            ArgumentNullException.ThrowIfNull(board);
            var newBoard = board.SetCell(0, 0, new BoardCell { Symbols = new[] { "★" } });
            var newState = state is string s && s.StartsWith("counter:")
                ? $"counter:{int.Parse(s.Split(':')[1]) + 1}"
                : "counter:1";
            return (newBoard, newState);
        }
    }
}
