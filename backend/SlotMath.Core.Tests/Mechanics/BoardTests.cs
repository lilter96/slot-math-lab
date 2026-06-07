using SlotMath.Core.Mechanics;

namespace SlotMath.Core.Tests.Mechanics;

public class BoardTests
{
    // ── BoardCell ───────────────────────────────────────────────────────

    [Fact]
    public void BoardCell_Default_IsEmpty()
    {
        var cell = new BoardCell();
        Assert.True(cell.IsEmpty);
        Assert.Null(cell.Symbols);
        Assert.False(cell.IsLocked);
        Assert.Null(cell.Decorations);
    }

    [Fact]
    public void BoardCell_WithSingleSymbol_HasCorrectProperties()
    {
        var cell = new BoardCell { Symbols = new[] { "A" } };
        Assert.False(cell.IsEmpty);
        Assert.Single(cell.Symbols);
        Assert.Equal("A", cell.Symbols[0]);
        Assert.False(cell.IsLocked);
    }

    [Fact]
    public void BoardCell_WithMultiSymbol_SupportsMultipleIds()
    {
        var cell = new BoardCell { Symbols = new[] { "A", "WILD", "BONUS" } };
        Assert.Equal(3, cell.Symbols.Length);
        Assert.False(cell.IsEmpty);
    }

    [Fact]
    public void BoardCell_WithLocked_IsLockedTrue()
    {
        var cell = new BoardCell { Symbols = new[] { "A" }, IsLocked = true };
        Assert.True(cell.IsLocked);
    }

    [Fact]
    public void BoardCell_WithDecorations_StoresKeyValuePairs()
    {
        var decorations = new Dictionary<string, string>
        {
            ["color"] = "red",
            ["multiplier"] = "2"
        };
        var cell = new BoardCell { Symbols = new[] { "A" }, Decorations = decorations };
        Assert.NotNull(cell.Decorations);
        Assert.Equal("red", cell.Decorations!["color"]);
        Assert.Equal("2", cell.Decorations!["multiplier"]);
    }

    [Fact]
    public void BoardCell_EmptyCell_CanBeCreatedViaEmptyArray()
    {
        var cell = new BoardCell { Symbols = Array.Empty<string>() };
        Assert.True(cell.IsEmpty);
    }

    [Fact]
    public void BoardCell_Equality_SameValuesAreEqual()
    {
        var a = new BoardCell
        {
            Symbols = new[] { "A", "B" },
            IsLocked = true,
            Decorations = new Dictionary<string, string> { ["x"] = "1" }
        };
        var b = new BoardCell
        {
            Symbols = new[] { "A", "B" },
            IsLocked = true,
            Decorations = new Dictionary<string, string> { ["x"] = "1" }
        };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void BoardCell_Equality_DifferentSymbolsAreNotEqual()
    {
        var a = new BoardCell { Symbols = new[] { "A" } };
        var b = new BoardCell { Symbols = new[] { "B" } };
        Assert.NotEqual(a, b);
    }

    // ── Board creation & indexing ───────────────────────────────────────

    [Fact]
    public void Board_Create_WithDimensions_SetsRowsAndCols()
    {
        var board = new Board(3, 5);
        Assert.Equal(3, board.Rows);
        Assert.Equal(5, board.Cols);
    }

    [Fact]
    public void Board_DefaultCells_AreEmpty()
    {
        var board = new Board(3, 5);
        for (int r = 0; r < board.Rows; r++)
            for (int c = 0; c < board.Cols; c++)
                Assert.True(board[r, c].IsEmpty);
    }

    [Fact]
    public void Board_SetAndGet_CellViaIndexer()
    {
        var board = new Board(3, 5);
        var cell = new BoardCell { Symbols = new[] { "A" }, IsLocked = true };
        var updated = board.SetCell(1, 2, cell);
        Assert.False(updated[1, 2].IsEmpty);
        Assert.True(updated[1, 2].IsLocked);
        Assert.Equal("A", updated[1, 2].Symbols![0]);
        // Original is unchanged (immutability)
        Assert.True(board[1, 2].IsEmpty);
    }

    [Fact]
    public void Board_Indexer_OutOfRange_Throws()
    {
        var board = new Board(3, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[0, -1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[0, 5]);
    }

    [Fact]
    public void Board_VariableDimensions_SupportsNonSquareGrids()
    {
        var board = new Board(1, 10); // 1-row, 10-col (horizontal strip)
        Assert.Equal(1, board.Rows);
        Assert.Equal(10, board.Cols);

        var tall = new Board(10, 3);  // 10-row, 3-col (vertical)
        Assert.Equal(10, tall.Rows);
        Assert.Equal(3, tall.Cols);
    }

    // ── Multi-symbol cells on board ─────────────────────────────────────

    [Fact]
    public void Board_MultiSymbolCells_AreSupported()
    {
        var board = new Board(3, 5);
        var multiCell = new BoardCell { Symbols = new[] { "A", "WILD" } };
        var updated = board.SetCell(0, 0, multiCell);
        Assert.Equal(2, updated[0, 0].Symbols!.Length);
        Assert.Contains("A", updated[0, 0].Symbols!);
        Assert.Contains("WILD", updated[0, 0].Symbols!);
    }

    // ── Locked cells ────────────────────────────────────────────────────

    [Fact]
    public void Board_LockedCells_RetainLockedState()
    {
        var board = new Board(3, 5);
        var lockedCell = new BoardCell { Symbols = new[] { "SCATTER" }, IsLocked = true };
        var updated = board.SetCell(2, 3, lockedCell);
        Assert.True(updated[2, 3].IsLocked);
        Assert.False(updated[2, 2].IsLocked);
    }

    // ── Decorated cells ─────────────────────────────────────────────────

    [Fact]
    public void Board_DecoratedCells_StoreArbitraryProperties()
    {
        var board = new Board(3, 5);
        var decoratedCell = new BoardCell
        {
            Symbols = new[] { "MULT" },
            Decorations = new Dictionary<string, string>
            {
                ["multiplier"] = "3",
                ["revealed"] = "false"
            }
        };
        var updated = board.SetCell(0, 0, decoratedCell);
        Assert.Equal("3", updated[0, 0].Decorations!["multiplier"]);
        Assert.Equal("false", updated[0, 0].Decorations!["revealed"]);
    }

    // ── Board equality & hashing (for memoization) ──────────────────────

    [Fact]
    public void Board_Equality_SameContentAreEqual()
    {
        var a = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "WILD" }, IsLocked = true });

        var b = new Board(2, 3)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(1, 2, new BoardCell { Symbols = new[] { "WILD" }, IsLocked = true });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Board_Equality_DifferentContentAreNotEqual()
    {
        var a = new Board(2, 3).SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });
        var b = new Board(2, 3).SetCell(0, 0, new BoardCell { Symbols = new[] { "B" } });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Board_Equality_DifferentDimensionsAreNotEqual()
    {
        Assert.NotEqual(new Board(3, 5), new Board(4, 5));
        Assert.NotEqual(new Board(3, 5), new Board(3, 6));
    }

    // ── Growable board ──────────────────────────────────────────────────

    [Fact]
    public void Board_Grow_AddRow_AtEnd()
    {
        var board = new Board(3, 5);
        var newCells = new BoardCell[]
        {
            new() { Symbols = new[] { "A" } },
            new() { Symbols = new[] { "B" } },
            new() { Symbols = new[] { "C" } },
            new() { Symbols = new[] { "D" } },
            new() { Symbols = new[] { "E" } },
        };
        var grown = board.AddRow(3, newCells); // add at end
        Assert.Equal(4, grown.Rows);
        Assert.Equal(5, grown.Cols);
        Assert.Equal("A", grown[3, 0].Symbols![0]);
        Assert.Equal("E", grown[3, 4].Symbols![0]);
    }

    [Fact]
    public void Board_Grow_AddRow_InMiddle_ShiftsExisting()
    {
        var board = new Board(3, 5)
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "OLD" } });
        var newCells = new BoardCell[]
        {
            new() { Symbols = new[] { "NEW" } },
            new() { Symbols = new[] { "NEW" } },
            new() { Symbols = new[] { "NEW" } },
            new() { Symbols = new[] { "NEW" } },
            new() { Symbols = new[] { "NEW" } },
        };
        var grown = board.AddRow(1, newCells); // insert at row 1
        Assert.Equal(4, grown.Rows);
        Assert.Equal("NEW", grown[1, 0].Symbols![0]);
        // Old row 2 is now row 3
        Assert.Equal("OLD", grown[3, 0].Symbols![0]);
    }

    [Fact]
    public void Board_Grow_AddColumn_AtEnd()
    {
        var board = new Board(3, 5);
        var newCells = new BoardCell[]
        {
            new() { Symbols = new[] { "X1" } },
            new() { Symbols = new[] { "X2" } },
            new() { Symbols = new[] { "X3" } },
        };
        var grown = board.AddColumn(5, newCells);
        Assert.Equal(3, grown.Rows);
        Assert.Equal(6, grown.Cols);
        Assert.Equal("X1", grown[0, 5].Symbols![0]);
        Assert.Equal("X3", grown[2, 5].Symbols![0]);
    }

    // ── Shrinkable board ────────────────────────────────────────────────

    [Fact]
    public void Board_Shrink_RemoveRow()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "TOP" } })
            .SetCell(2, 0, new BoardCell { Symbols = new[] { "BOTTOM" } });
        var shrunk = board.RemoveRow(1); // remove middle row
        Assert.Equal(2, shrunk.Rows);
        Assert.Equal(5, shrunk.Cols);
        Assert.Equal("TOP", shrunk[0, 0].Symbols![0]);
        // Old row 2 is now row 1
        Assert.Equal("BOTTOM", shrunk[1, 0].Symbols![0]);
    }

    [Fact]
    public void Board_Shrink_RemoveColumn()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "LEFT" } })
            .SetCell(0, 4, new BoardCell { Symbols = new[] { "RIGHT" } });
        var shrunk = board.RemoveColumn(0); // remove first column
        Assert.Equal(3, shrunk.Rows);
        Assert.Equal(4, shrunk.Cols);
        Assert.Equal("RIGHT", shrunk[0, 3].Symbols![0]);
    }

    [Fact]
    public void Board_GrowShrink_RoundTrip_YieldsEquivalentBoard()
    {
        var original = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(2, 4, new BoardCell { Symbols = new[] { "B" }, IsLocked = true });

        var grown = original.AddRow(3, Enumerable.Range(0, 5)
            .Select(_ => new BoardCell()).ToArray());
        Assert.Equal(4, grown.Rows);

        var shrunk = grown.RemoveRow(3);
        Assert.Equal(3, shrunk.Rows);
        Assert.Equal(original, shrunk);
    }

    // ── Board enumeration (for transforms) ──────────────────────────────

    [Fact]
    public void Board_GetRow_ReturnsCorrectCells()
    {
        var board = new Board(3, 5)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } })
            .SetCell(0, 4, new BoardCell { Symbols = new[] { "E" } });
        var row = board.GetRow(0);
        Assert.Equal(5, row.Length);
        Assert.Equal("A", row[0].Symbols![0]);
        Assert.True(row[1].IsEmpty);
        Assert.Equal("E", row[4].Symbols![0]);
    }

    [Fact]
    public void Board_GetColumn_ReturnsCorrectCells()
    {
        var board = new Board(3, 5)
            .SetCell(0, 2, new BoardCell { Symbols = new[] { "TOP" } })
            .SetCell(2, 2, new BoardCell { Symbols = new[] { "BOT" } });
        var col = board.GetColumn(2);
        Assert.Equal(3, col.Length);
        Assert.Equal("TOP", col[0].Symbols![0]);
        Assert.True(col[1].IsEmpty);
        Assert.Equal("BOT", col[2].Symbols![0]);
    }

    [Fact]
    public void Board_AllCells_EnumeratesRowMajor()
    {
        var board = new Board(2, 3);
        var cells = board.AllCells().ToList();
        Assert.Equal(6, cells.Count);
        // (0,0), (0,1), (0,2), (1,0), (1,1), (1,2)
        Assert.Equal(0, cells[0].Row);
        Assert.Equal(0, cells[0].Col);
        Assert.Equal(0, cells[2].Row);
        Assert.Equal(2, cells[2].Col);
        Assert.Equal(1, cells[3].Row);
        Assert.Equal(0, cells[3].Col);
        Assert.Equal(1, cells[5].Row);
        Assert.Equal(2, cells[5].Col);
    }

    // ── Clone / With helpers ────────────────────────────────────────────

    [Fact]
    public void Board_Clone_CreatesEqualButIndependentBoard()
    {
        var original = new Board(2, 2)
            .SetCell(0, 0, new BoardCell { Symbols = new[] { "A" } });
        var clone = original.Clone();
        Assert.Equal(original, clone);
        Assert.NotSame(original, clone);

        var mutated = clone.SetCell(1, 1, new BoardCell { Symbols = new[] { "B" } });
        Assert.NotEqual(original, mutated);
    }
}

public class Board_EdgeCases
{
    [Fact]
    public void Board_GetRow_Column()
    {
        var board = new Board(2, 3)
            .SetCell(0, 0, new BoardCell().WithSymbols("A"))
            .SetCell(0, 1, new BoardCell().WithSymbols("B"))
            .SetCell(1, 0, new BoardCell().WithSymbols("C"));

        var row = board.GetRow(0);
        Assert.Equal(3, row.Length);
        Assert.Equal("A", row[0].Symbols![0]);

        var col = board.GetColumn(1);
        Assert.Equal(2, col.Length);
        Assert.Equal("B", col[0].Symbols![0]);
    }

    [Fact]
    public void Board_OutOfRange_Throws()
    {
        var board = new Board(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => board[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => board.GetRow(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.GetColumn(5));
    }

    [Fact]
    public void Board_SetRow_SetColumn()
    {
        var board = new Board(2, 2);
        var newRow = new[] { new BoardCell().WithSymbols("X"), new BoardCell().WithSymbols("Y") };
        board = board.SetRow(0, newRow);
        Assert.Equal("X", board[0, 0].Symbols![0]);
        Assert.Equal("Y", board[0, 1].Symbols![0]);
    }

    [Fact]
    public void BoardCell_Decorations_WithDecoration()
    {
        var cell = new BoardCell().WithSymbols("A")
            .WithDecoration("multiplier", "2")
            .WithLocked(true);

        Assert.True(cell.IsLocked);
        Assert.Equal("2", cell.GetDecoration("multiplier"));
        Assert.Null(cell.GetDecoration("nonexistent"));
    }

    [Fact]
    public void Board_Clone_PreservesData()
    {
        var board = new Board(2, 2)
            .SetCell(0, 0, new BoardCell().WithSymbols("A").WithDecoration("x", "1"));
        var clone = board.Clone();
        Assert.Equal(board, clone);
        Assert.Equal("A", clone[0, 0].Symbols![0]);
        Assert.Equal("1", clone[0, 0].GetDecoration("x"));
    }

    [Fact]
    public void Board_Grow_Shrink()
    {
        var board = new Board(2, 2);
        board = board.AddRow(1, new[] { new BoardCell(), new BoardCell() });
        Assert.Equal(3, board.Rows);

        board = board.AddColumn(1, new[] { new BoardCell(), new BoardCell(), new BoardCell() });
        Assert.Equal(3, board.Cols);

        board = board.RemoveRow(2);
        Assert.Equal(2, board.Rows);

        board = board.RemoveColumn(2);
        Assert.Equal(2, board.Cols);
    }
}
