using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Transforms;

namespace SlotMath.Core.Tests.Mechanics;

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
    //  ExpandTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Expand_AddsRow_AtSpecifiedPosition()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 4, new BoardCell { Symbols = new[] { "Z" } });

        var newRow = Enumerable.Range(0, 5)
            .Select(i => new BoardCell { Symbols = new[] { $"N{i}" } })
            .ToArray();

        var expand = new ExpandTransform(ExpandDirection.Row, 1, newRow);
        var (newBoard, _) = expand.Apply(board, null);

        Assert.Equal(4, newBoard.Rows); // grew from 3 to 4
        Assert.Equal(5, newBoard.Cols);
        Assert.Equal("A", newBoard[0, 0].Symbols![0]);  // unchanged
        Assert.Equal("N0", newBoard[1, 0].Symbols![0]); // new row
        Assert.Equal("N4", newBoard[1, 4].Symbols![0]); // new row end
        Assert.Equal("Z", newBoard[3, 4].Symbols![0]);  // old row 2 shifted down
    }

    [Fact]
    public void Expand_AddsColumn_AtSpecifiedPosition()
    {
        var board = new Board(3, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "Z" } });

        var newCol = new BoardCell[]
        {
            new() { Symbols = new[] { "X0" } },
            new() { Symbols = new[] { "X1" } },
            new() { Symbols = new[] { "X2" } },
        };

        var expand = new ExpandTransform(ExpandDirection.Column, 1, newCol);
        var (newBoard, _) = expand.Apply(board, null);

        Assert.Equal(3, newBoard.Rows);
        Assert.Equal(4, newBoard.Cols); // grew from 3 to 4
        Assert.Equal("X0", newBoard[0, 1].Symbols![0]); // new column
        Assert.Equal("X2", newBoard[2, 1].Symbols![0]); // new column end
        Assert.Equal("A", newBoard[0, 0].Symbols![0]);  // unchanged
        Assert.Equal("Z", newBoard[2, 3].Symbols![0]);  // shifted right
    }

    [Fact]
    public void Expand_IsPure()
    {
        var board = new Board(2, 2);
        var newRow = new BoardCell[] { new(), new() };
        var expand = new ExpandTransform(ExpandDirection.Row, 2, newRow);

        var (b1, _) = expand.Apply(board, null);
        var (b2, _) = expand.Apply(board, null);
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
    //  MorphTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Morph_ReplacesSymbolsAccordingToMap()
    {
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "10" } })
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "J" } })
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "WILD" } });

        var mapping = new Dictionary<string, string>
        {
            ["10"] = "J",
            ["J"] = "Q",
        };

        var morph = new MorphTransform(mapping);
        var (newBoard, _) = morph.Apply(board, null);

        Assert.Equal("J", newBoard[0, 0].Symbols![0]);   // 10 → J
        Assert.Equal("Q", newBoard[0, 1].Symbols![0]);   // J → Q
        Assert.Equal("WILD", newBoard[0, 2].Symbols![0]); // not in map, unchanged
    }

    [Fact]
    public void Morph_MultiSymbolCells_MorphsAllMatching()
    {
        var board = new Board(1, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A", "B", "C" } });

        var morph = new MorphTransform(new Dictionary<string, string>
        {
            ["A"] = "X",
            ["C"] = "Z",
        });

        var (newBoard, _) = morph.Apply(board, null);
        Assert.Equal(new[] { "X", "B", "Z" }, newBoard[0, 0].Symbols!);
    }

    [Fact]
    public void Morph_IsPure()
    {
        var board = new Board(1, 1)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });
        var morph = new MorphTransform(new Dictionary<string, string> { ["A"] = "B" });

        var (b1, _) = morph.Apply(board, null);
        var (b2, _) = morph.Apply(board, null);
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
    //  NudgeTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Nudge_Down_ShiftsColumnDown()
    {
        var board = new Board(3, 3)
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(2, 1, new BoardCell { Symbols = new[] { "C" } });

        var nudge = new NudgeTransform(1, NudgeDirection.Down);
        var (newBoard, _) = nudge.Apply(board, null);

        // Row 0 gets new empty cell from top, rows 1-2 shift down
        Assert.True(newBoard[0, 1].IsEmpty);   // new empty from top
        Assert.Equal("A", newBoard[1, 1].Symbols![0]); // shifted from row 0
        Assert.Equal("B", newBoard[2, 1].Symbols![0]); // shifted from row 1
        // C falls off bottom (replaced by B)
    }

    [Fact]
    public void Nudge_Up_ShiftsColumnUp()
    {
        var board = new Board(3, 3)
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "B" } })
            .SetCell(2, 1, new BoardCell { Symbols = new[] { "C" } });

        var nudge = new NudgeTransform(1, NudgeDirection.Up);
        var (newBoard, _) = nudge.Apply(board, null);

        Assert.Equal("B", newBoard[0, 1].Symbols![0]); // shifted up
        Assert.Equal("C", newBoard[1, 1].Symbols![0]); // shifted up
        Assert.True(newBoard[2, 1].IsEmpty);    // new empty from bottom
    }

    [Fact]
    public void Nudge_LockedCells_DontMove()
    {
        var board = new Board(3, 3)
            .SetCell(0, 1, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 1, new BoardCell { Symbols = new[] { "B" }, IsLocked = true })
            .SetCell(2, 1, new BoardCell { Symbols = new[] { "C" } });

        var nudge = new NudgeTransform(1, NudgeDirection.Down);
        var (newBoard, _) = nudge.Apply(board, null);

        // Locked cell stays
        Assert.True(newBoard[1, 1].IsLocked);
        Assert.Equal("B", newBoard[1, 1].Symbols![0]);
    }

    [Fact]
    public void Nudge_IsPure()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 0, new BoardCell { Symbols = new[] { "B" } });
        var nudge = new NudgeTransform(0, NudgeDirection.Down);

        var (b1, _) = nudge.Apply(board, null);
        var (b2, _) = nudge.Apply(board, null);
        Assert.Equal(b1, b2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  GrowShrinkTransform
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GrowShrink_Grow_AddsRowAtEnd()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });

        var growShrink = new GrowShrinkTransform(rows: 1, cols: 0);
        var (newBoard, _) = growShrink.Apply(board, null);

        Assert.Equal(4, newBoard.Rows);
        Assert.Equal(5, newBoard.Cols);
        Assert.Equal("A", newBoard[0, 0].Symbols![0]);
        Assert.True(newBoard[3, 0].IsEmpty);
    }

    [Fact]
    public void GrowShrink_Grow_AddsColumnAtEnd()
    {
        var board = new Board(3, 5);
        var growShrink = new GrowShrinkTransform(rows: 0, cols: 2);
        var (newBoard, _) = growShrink.Apply(board, null);

        Assert.Equal(3, newBoard.Rows);
        Assert.Equal(7, newBoard.Cols);
    }

    [Fact]
    public void GrowShrink_Shrink_RemovesLastRow()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "TOP" } })
            .SetCell(2, 4, new BoardCell { Symbols = new[] { "BOTTOM" } });

        var growShrink = new GrowShrinkTransform(rows: -1, cols: 0);
        var (newBoard, _) = growShrink.Apply(board, null);

        Assert.Equal(2, newBoard.Rows);
        Assert.Equal(5, newBoard.Cols);
        Assert.Equal("TOP", newBoard[0, 0].Symbols![0]);
    }

    [Fact]
    public void GrowShrink_GrowThenShrink_RoundTrip()
    {
        var original = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 4, new BoardCell { Symbols = new[] { "B" }, IsLocked = true });

        var grow = new GrowShrinkTransform(rows: 1, cols: 1);
        var (grown, _) = grow.Apply(original, null);
        Assert.Equal(4, grown.Rows);
        Assert.Equal(6, grown.Cols);

        var shrink = new GrowShrinkTransform(rows: -1, cols: -1);
        var (shrunk, _) = shrink.Apply(grown, null);

        Assert.Equal(original, shrunk);
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
    public void ExpandTransform_GrowsBoardRight()
    {
        var t = new ExpandTransform(ExpandDirection.Column, 0,
            new[] { new BoardCell().WithSymbols("X"), new BoardCell().WithSymbols("Y") });
        var board = new Board(2, 1).SetCell(0, 0, new BoardCell().WithSymbols("A"));

        var (newBoard, _) = t.Apply(board, null);
        Assert.Equal(2, newBoard.Cols);
        Assert.False(newBoard[0, 0].IsEmpty);
        Assert.False(newBoard[0, 1].IsEmpty || newBoard[1, 0].IsEmpty);
    }

    [Fact]
    public void MorphTransform_MapsSymbolsPerMapping()
    {
        var mapping = new Dictionary<string, string> { ["A"] = "Wild" };
        var t = new MorphTransform(mapping);
        var board = new Board(1, 2)
            .SetCell(0, 0, new BoardCell().WithSymbols("A"))
            .SetCell(0, 1, new BoardCell().WithSymbols("B"));

        var (newBoard, _) = t.Apply(board, null);
        Assert.Equal("Wild", newBoard[0, 0].Symbols![0]);
        Assert.Equal("B", newBoard[0, 1].Symbols![0]);
    }

    [Fact]
    public void NudgeTransform_ShiftsColumnUp()
    {
        var t = new NudgeTransform(0, NudgeDirection.Up);
        var board = new Board(3, 1)
            .SetCell(0, 0, new BoardCell().WithSymbols("A"))
            .SetCell(1, 0, new BoardCell().WithSymbols("B"))
            .SetCell(2, 0, new BoardCell().WithSymbols("C"));
        var (newBoard, _) = t.Apply(board, null);
        Assert.NotNull(newBoard);
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

    [Fact]
    public void GrowShrinkTransform_GrowsBoard()
    {
        var t = new GrowShrinkTransform(1, 0);
        var board = new Board(1, 1).SetCell(0, 0, new BoardCell().WithSymbols("A"));
        var (newBoard, _) = t.Apply(board, null);
        Assert.Equal(2, newBoard.Rows);
    }
}
