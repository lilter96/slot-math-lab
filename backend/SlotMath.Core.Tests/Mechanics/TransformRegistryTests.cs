using SlotMath.Core.Mechanics;
using SlotMath.Core.Tests;

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
        var state = TestBoardState.From(new string?[][]
        {
            new string?[] { null, null, null, null, null },
            new string?[] { null, null, null, null, null },
            new string?[] { null, null, null, null, null },
        });
        var transform = new TestTransform();

        var r1 = transform.Apply(state);
        var r2 = transform.Apply(state);

        // Same input → same output (purity)
        Assert.Equal(GridState.Cells(r1), GridState.Cells(r2));

        // Original state untouched (no side effects)
        Assert.Equal(3, GridState.Rows(state));
    }

    [Fact]
    public void ITransform_CanModifyBoardAndState()
    {
        var state = TestBoardState.From(
            new string?[][]
            {
                new string?[] { null, null, null, null, null },
                new string?[] { null, null, null, null, null },
                new string?[] { null, null, null, null, null },
            },
            (CellAddingTransform.CounterKey, (object?)0));
        var transform = new CellAddingTransform();

        var newState = transform.Apply(state);

        // Board changed: cell (0,0) now holds the marker symbol.
        Assert.NotEqual(GridState.Cells(state), GridState.Cells(newState));
        Assert.Equal("★", GridState.Symbol(GridState.Cells(newState)[0]));

        // State changed: the counter advanced 0 → 1.
        Assert.Equal(1, newState[CellAddingTransform.CounterKey]);
    }

    [Fact]
    public void ITransform_WithNullState_ThrowsArgumentNullException()
    {
        var transform = new TestTransform();
        Assert.Throws<ArgumentNullException>(() => transform.Apply(null!));
    }

    [Fact]
    public void ITransform_PreservesUnrelatedState()
    {
        var state = TestBoardState.From(
            new string?[][]
            {
                new string?[] { "A", null, null, null, null },
                new string?[] { null, null, null, null, null },
                new string?[] { null, null, null, null, "B" },
            },
            ("unrelated", (object?)"keep-me"));

        var transform = new TestTransform();
        var newState = transform.Apply(state);

        // TestTransform returns the board unchanged …
        Assert.Equal(GridState.Cells(state), GridState.Cells(newState));
        // … and leaves unrelated state intact.
        Assert.Equal("keep-me", newState["unrelated"]);
    }

    // ── Test transform implementations ─────────────────────────────────

    private sealed class TestTransform : IFastPathTransform
    {
        public IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state)
        {
            ArgumentNullException.ThrowIfNull(state);
            return state;
        }
    }

    private sealed class CellAddingTransform : IFastPathTransform
    {
        public const string CounterKey = "counter";

        public IReadOnlyDictionary<string, object?> Apply(IReadOnlyDictionary<string, object?> state)
        {
            ArgumentNullException.ThrowIfNull(state);

            // Modify the board: set cell (0,0) to a marker symbol.
            var cells = GridState.Cells(state);
            var newCells = (object?[])cells.Clone();
            if (newCells.Length > 0)
                newCells[0] = "★";
            var withBoard = GridState.With(state, GridState.CellsKey, newCells);

            // Modify state: bump an integer counter.
            var counter = withBoard.TryGetValue(CounterKey, out var v) && v is int i ? i : 0;
            return GridState.With(withBoard, CounterKey, counter + 1);
        }
    }
}
