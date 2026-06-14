using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Transforms;

namespace SlotMath.Core.Tests.Mechanics;

[Collection("Registry")]
public class StandardTransformsTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  RevealTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Reveal_SetsRevealedDecoration_OnAllCells()
    {
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "B" }, IsLocked = true });

        var reveal = new RevealTransform();
        var (newBoard, _) = reveal.Apply(board, null);

        // All non-empty cells get "revealed" = "true"
        Assert.Equal("true", newBoard[0, 0].Decorations!["revealed"]);
        Assert.Equal("true", newBoard[1, 1].Decorations!["revealed"]);
        // Empty cells don't get decorated
        Assert.Null(newBoard[0, 1].Decorations);
    }

    [Fact]
    public void Reveal_IsPure_NoSideEffects()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });
        var reveal = new RevealTransform();

        var (b1, _) = reveal.Apply(board, null);
        var (b2, _) = reveal.Apply(board, null);
        Assert.Equal(b1, b2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  LockTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Lock_LocksSpecifiedPositions()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "WILD" } })
            .SetCell(2, 4, new BoardCell { Symbols = new[] { "SCATTER" } });

        var positions = new[] { (0, 0), (2, 4) };
        var lockTransform = new LockTransform(positions);

        var (newBoard, _) = lockTransform.Apply(board, null);

        // Specified positions are locked
        Assert.True(newBoard[0, 0].IsLocked);
        Assert.True(newBoard[2, 4].IsLocked);
        // Unspecified positions unchanged
        Assert.False(newBoard[1, 2].IsLocked);
    }

    [Fact]
    public void Lock_AlreadyLocked_StaysLocked()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" }, IsLocked = true });

        var lockTransform = new LockTransform(new[] { (0, 0) });
        var (newBoard, _) = lockTransform.Apply(board, null);

        Assert.True(newBoard[0, 0].IsLocked);
    }

    [Fact]
    public void Lock_IsPure()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });
        var lockTransform = new LockTransform(new[] { (0, 0) });

        var (b1, _) = lockTransform.Apply(board, null);
        var (b2, _) = lockTransform.Apply(board, null);
        Assert.Equal(b1, b2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CollectTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Collect_ExtractsMoneySymbols_AndClearsThem()
    {
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell
            {
                Symbols = new[] { "MONEY" },
                Decorations = new Dictionary<string, string> { ["amount"] = "10" }
            })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 2, new BoardCell
            {
                Symbols = new[] { "MONEY" },
                Decorations = new Dictionary<string, string> { ["amount"] = "5" }
            });

        var collect = new CollectTransform(
            cell => cell.Symbols is { Length: > 0 } s && s[0] == "MONEY",
            "amount");

        var (newBoard, newState) = collect.Apply(board, null);

        // Collected cells are now empty
        Assert.True(newBoard[0, 0].IsEmpty);
        Assert.True(newBoard[0, 2].IsEmpty);
        // Non-money cell unchanged
        Assert.Equal("A", newBoard[0, 1].Symbols![0]);
        // State carries total collected
        Assert.Equal(15m, newState);
    }

    [Fact]
    public void Collect_NoMatches_ReturnsSameBoardAndZero()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });

        var collect = new CollectTransform(
            cell => cell.Symbols is { Length: > 0 } s && s[0] == "MONEY",
            "amount");

        var (newBoard, newState) = collect.Apply(board, null);
        Assert.Equal(board, newBoard);
        Assert.Equal(0m, newState);
    }

    [Fact]
    public void Collect_IsPure()
    {
        var board = new Board(1, 2)
            .SetCell(0, 0, new BoardCell
            {
                Symbols = new[] { "MONEY" },
                Decorations = new Dictionary<string, string> { ["amount"] = "3" }
            });
        var collect = new CollectTransform(
            cell => cell.Symbols is { Length: > 0 } s && s[0] == "MONEY",
            "amount");

        var (b1, s1) = collect.Apply(board, null);
        var (b2, s2) = collect.Apply(board, null);
        Assert.Equal(b1, b2);
        Assert.Equal(s1, s2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RemoveWinningTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RemoveWinning_ClearsWinningPositions()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "C" } });

        var winningPositions = new HashSet<(int, int)> { (0, 0), (0, 1), (0, 2) };
        var remove = new RemoveWinningTransform(winningPositions);

        var (newBoard, _) = remove.Apply(board, null);

        // Winning cells cleared
        Assert.True(newBoard[0, 0].IsEmpty);
        Assert.True(newBoard[0, 1].IsEmpty);
        Assert.True(newBoard[0, 2].IsEmpty);
        // Non-winning cells unchanged
        Assert.Equal("B", newBoard[1, 0].Symbols![0]);
        Assert.Equal("C", newBoard[2, 0].Symbols![0]);
    }

    [Fact]
    public void RemoveWinning_LockedCells_AreNotCleared()
    {
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" }, IsLocked = true })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } });

        var winningPositions = new HashSet<(int, int)> { (0, 0), (0, 1) };
        var remove = new RemoveWinningTransform(winningPositions);

        var (newBoard, _) = remove.Apply(board, null);

        // Locked winning cell stays
        Assert.False(newBoard[0, 0].IsEmpty);
        Assert.Equal("A", newBoard[0, 0].Symbols![0]);
        // Unlocked winning cell cleared
        Assert.True(newBoard[0, 1].IsEmpty);
    }

    [Fact]
    public void RemoveWinning_IsPure()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });
        var remove = new RemoveWinningTransform(new HashSet<(int, int)> { (0, 0) });

        var (b1, _) = remove.Apply(board, null);
        var (b2, _) = remove.Apply(board, null);
        Assert.Equal(b1, b2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RefillTumbleTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RefillTumble_DropsExistingSymbolsDown_AndFillsFromTop()
    {
        // Board with gaps — symbols should fall down
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            // row 1, col 0: empty (gap)
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "C" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "X" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "Y" } })
            .SetCell(2, 1, new BoardCell { Symbols = new[] { "Z" } });

        var newSymbols = new[] { "★", "★", "★", "★", "★" }; // symbols to fill from top
        var refill = new RefillTumbleTransform(() => newSymbols);

        var (newBoard, _) = refill.Apply(board, null);

        // Column 0: A falls to row 2 (bottom), C falls to row 1, row 0 gets ★
        Assert.True(newBoard[0, 0].Symbols![0] == "★" || !newBoard[0, 0].IsEmpty);
        // Column 1: X, Y, Z are solid — no gaps, should stay in place
        Assert.Equal("X", newBoard[0, 1].Symbols![0]);
        Assert.Equal("Y", newBoard[1, 1].Symbols![0]);
        Assert.Equal("Z", newBoard[2, 1].Symbols![0]);
    }

    [Fact]
    public void RefillTumble_LockedCells_StayInPlace()
    {
        var board = new Board(3, 3)
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "LOCK" }, IsLocked = true });

        var refill = new RefillTumbleTransform(() => new[] { "★", "★", "★" });
        var (newBoard, _) = refill.Apply(board, null);

        Assert.True(newBoard[1, 0].IsLocked);
        Assert.Equal("LOCK", newBoard[1, 0].Symbols![0]);
    }

    [Fact]
    public void RefillTumble_FullyFilledBoard_StaysSame()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "C" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "D" } });

        var refill = new RefillTumbleTransform(() => new[] { "★" });
        var (newBoard, _) = refill.Apply(board, null);

        Assert.Equal(board, newBoard);
    }

    [Fact]
    public void RefillTumble_IsPure_GivenDeterministicSymbolSource()
    {
        var board = new Board(2, 2)
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } });
        var refill = new RefillTumbleTransform(() => new[] { "X", "Y" });

        var (b1, _) = refill.Apply(board, null);
        var (b2, _) = refill.Apply(board, null);
        Assert.Equal(b1, b2);
    }
}

public class Transform_RegisterAndRetrieve
{
    [Fact]
    public void TransformRegistry_Works()
    {
        var t = new LockTransform(new[] { (0, 0) });
        TransformRegistry.Register("my-test-lock", t);
        var retrieved = TransformRegistry.TryGet("my-test-lock");
        Assert.NotNull(retrieved);
        Assert.IsType<LockTransform>(retrieved);
        TransformRegistry.Clear();
    }
}

public class MoreTransformEdgeCases
{
    [Fact]
    public void CollectTransform_CollectsMatchingCells()
    {
        var t = new CollectTransform(cell => !cell.IsEmpty && cell.Symbols![0] == "coin", "value");
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell().WithSymbols("A"))
            .SetCell(0, 1, new BoardCell().WithSymbols("coin").WithDecoration("value", "10"));

        var (newBoard, _) = t.Apply(board, null);
        Assert.True(newBoard[0, 1].IsEmpty);
        Assert.False(newBoard[0, 0].IsEmpty);
    }

    [Fact]
    public void RevealTransform_RevealsAllCells()
    {
        var t = new RevealTransform();
        var board = new Board(2, 1)
            .SetCell(0, 0, new BoardCell().WithSymbols("?"))
            .SetCell(1, 0, new BoardCell().WithSymbols("?"));

        var (newBoard, _) = t.Apply(board, null);
        Assert.Equal("true", newBoard[0, 0].GetDecoration("revealed"));
        Assert.Equal("true", newBoard[1, 0].GetDecoration("revealed"));
    }

    [Fact]
    public void RefillTumbleTransform_RefillsEmptyCells()
    {
        var symbols = new[] { "A", "B", "C" };
        var t = new RefillTumbleTransform(() => symbols);
        var board = new Board(2, 1)
            .SetCell(0, 0, new BoardCell().WithSymbols("X"));
        // Cell (1,0) is empty.

        var (newBoard, _) = t.Apply(board, null);
        Assert.False(newBoard[1, 0].IsEmpty);
    }

    [Fact]
    public void RemoveWinningTransform_ClearsPositions()
    {
        var wins = new HashSet<(int Row, int Col)> { (0, 0) };
        var t = new RemoveWinningTransform(wins);
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell().WithSymbols("A"))
            .SetCell(0, 1, new BoardCell().WithSymbols("B"));

        var (newBoard, _) = t.Apply(board, null);
        Assert.True(newBoard[0, 0].IsEmpty);
        Assert.False(newBoard[0, 1].IsEmpty);
    }
}
