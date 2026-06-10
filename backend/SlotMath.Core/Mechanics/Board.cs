namespace SlotMath.Core.Mechanics;

/// <summary>
/// A mutable-as-construction, logically-immutable slot board.
///
/// Every mutation returns a new Board. The board supports:
/// - Variable dimensions (any Rows × Cols)
/// - Multi-symbol, empty, locked, and decorated cells
/// - Growable (AddRow, AddColumn) and shrinkable (RemoveRow, RemoveColumn)
/// - Structural equality and hashing for use as a memoization key
/// </summary>
public sealed class Board : IEquatable<Board>
{
    private readonly BoardCell[,] _cells;

    /// <summary>Number of rows.</summary>
    public int Rows => _cells.GetLength(0);

    /// <summary>Number of columns.</summary>
    public int Cols => _cells.GetLength(1);

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>
    /// Create a board with the given dimensions, all cells empty.
    /// </summary>
    public Board(int rows, int cols)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cols);
        _cells = new BoardCell[rows, cols];
    }

    /// <summary>
    /// Internal constructor from an existing cell array (used by mutation methods).
    /// </summary>
    private Board(BoardCell[,] cells)
    {
        _cells = cells;
    }

    /// <summary>
    /// Create a board directly from a cell array in one step.  The array is
    /// taken over by the board — callers must not mutate it afterwards.
    /// Avoids the O(cells²) cost of building a board through repeated
    /// <see cref="SetCell"/> calls.
    /// </summary>
    public static Board FromCells(BoardCell[,] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.GetLength(0) == 0 || cells.GetLength(1) == 0)
            throw new ArgumentException("Board must have at least one row and column.", nameof(cells));
        return new Board(cells);
    }

    // ── Indexer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Get the cell at (row, col). Throws on out-of-range.
    /// </summary>
    public BoardCell this[int row, int col]
    {
        get
        {
            if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols)
                throw new ArgumentOutOfRangeException(
                    $"Cell ({row},{col}) is out of range for board {Rows}×{Cols}");
            return _cells[row, col];
        }
    }

    // ── Mutation (returns new Board) ──────────────────────────────────────

    /// <summary>
    /// Returns a new board with the cell at (row, col) replaced by <paramref name="cell"/>.
    /// </summary>
    public Board SetCell(int row, int col, BoardCell cell)
    {
        if ((uint)row >= (uint)Rows || (uint)col >= (uint)Cols)
            throw new ArgumentOutOfRangeException(
                $"Cell ({row},{col}) is out of range for board {Rows}×{Cols}");
        var newCells = (BoardCell[,])_cells.Clone();
        newCells[row, col] = cell;
        return new Board(newCells);
    }

    // ── Grow / Shrink ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a new board with a row inserted at <paramref name="position"/>.
    /// Cells at and after the position are shifted down.
    /// </summary>
    public Board AddRow(int position, BoardCell[] newRow)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (position > Rows)
            throw new ArgumentOutOfRangeException(nameof(position),
                $"Cannot insert row at {position} in board with {Rows} rows");
        if (newRow.Length != Cols)
            throw new ArgumentException(
                $"New row has {newRow.Length} cells, expected {Cols}");

        var newCells = new BoardCell[Rows + 1, Cols];
        for (var r = 0; r < position; r++)
            for (var c = 0; c < Cols; c++)
                newCells[r, c] = _cells[r, c];

        for (var c = 0; c < Cols; c++)
            newCells[position, c] = newRow[c];

        for (var r = position; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                newCells[r + 1, c] = _cells[r, c];

        return new Board(newCells);
    }

    /// <summary>
    /// Returns a new board with the row at <paramref name="position"/> removed.
    /// </summary>
    public Board RemoveRow(int position)
    {
        if ((uint)position >= (uint)Rows)
            throw new ArgumentOutOfRangeException(nameof(position),
                $"Cannot remove row {position} from board with {Rows} rows");
        if (Rows <= 1)
            throw new InvalidOperationException("Cannot remove the last row");

        var newCells = new BoardCell[Rows - 1, Cols];
        for (var r = 0; r < position; r++)
            for (var c = 0; c < Cols; c++)
                newCells[r, c] = _cells[r, c];

        for (var r = position + 1; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                newCells[r - 1, c] = _cells[r, c];

        return new Board(newCells);
    }

    /// <summary>
    /// Returns a new board with a column inserted at <paramref name="position"/>.
    /// </summary>
    public Board AddColumn(int position, BoardCell[] newColumn)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (position > Cols)
            throw new ArgumentOutOfRangeException(nameof(position),
                $"Cannot insert column at {position} in board with {Cols} columns");
        if (newColumn.Length != Rows)
            throw new ArgumentException(
                $"New column has {newColumn.Length} cells, expected {Rows}");

        var newCells = new BoardCell[Rows, Cols + 1];
        for (var c = 0; c < position; c++)
            for (var r = 0; r < Rows; r++)
                newCells[r, c] = _cells[r, c];

        for (var r = 0; r < Rows; r++)
            newCells[r, position] = newColumn[r];

        for (var c = position; c < Cols; c++)
            for (var r = 0; r < Rows; r++)
                newCells[r, c + 1] = _cells[r, c];

        return new Board(newCells);
    }

    /// <summary>
    /// Returns a new board with the column at <paramref name="position"/> removed.
    /// </summary>
    public Board RemoveColumn(int position)
    {
        if ((uint)position >= (uint)Cols)
            throw new ArgumentOutOfRangeException(nameof(position),
                $"Cannot remove column {position} from board with {Cols} columns");
        if (Cols <= 1)
            throw new InvalidOperationException("Cannot remove the last column");

        var newCells = new BoardCell[Rows, Cols - 1];
        for (var c = 0; c < position; c++)
            for (var r = 0; r < Rows; r++)
                newCells[r, c] = _cells[r, c];

        for (var c = position + 1; c < Cols; c++)
            for (var r = 0; r < Rows; r++)
                newCells[r, c - 1] = _cells[r, c];

        return new Board(newCells);
    }

    // ── Row / Column / All helpers ────────────────────────────────────────

    /// <summary>Get all cells in the given row.</summary>
    public BoardCell[] GetRow(int row)
    {
        if ((uint)row >= (uint)Rows) throw new ArgumentOutOfRangeException(nameof(row));
        var result = new BoardCell[Cols];
        for (var c = 0; c < Cols; c++) result[c] = _cells[row, c];
        return result;
    }

    /// <summary>Get all cells in the given column.</summary>
    public BoardCell[] GetColumn(int col)
    {
        if ((uint)col >= (uint)Cols) throw new ArgumentOutOfRangeException(nameof(col));
        var result = new BoardCell[Rows];
        for (var r = 0; r < Rows; r++) result[r] = _cells[r, col];
        return result;
    }

    /// <summary>Returns a new board with the specified row replaced.</summary>
    public Board SetRow(int row, BoardCell[] newRow)
    {
        if ((uint)row >= (uint)Rows) throw new ArgumentOutOfRangeException(nameof(row));
        if (newRow.Length != Cols) throw new ArgumentException($"Expected {Cols} cells, got {newRow.Length}");
        var newCells = (BoardCell[,])_cells.Clone();
        for (var c = 0; c < Cols; c++) newCells[row, c] = newRow[c];
        return new Board(newCells);
    }

    /// <summary>Returns a new board with the specified column replaced.</summary>
    public Board SetColumn(int col, BoardCell[] newColumn)
    {
        if ((uint)col >= (uint)Cols) throw new ArgumentOutOfRangeException(nameof(col));
        if (newColumn.Length != Rows) throw new ArgumentException($"Expected {Rows} cells, got {newColumn.Length}");
        var newCells = (BoardCell[,])_cells.Clone();
        for (var r = 0; r < Rows; r++) newCells[r, col] = newColumn[r];
        return new Board(newCells);
    }

    /// <summary>
    /// Enumerate all cells in row-major order with their positions.
    /// </summary>
    public IEnumerable<(int Row, int Col, BoardCell Cell)> AllCells()
    {
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                yield return (r, c, _cells[r, c]);
    }

    /// <summary>Deep clone.</summary>
    public Board Clone()
    {
        var newCells = new BoardCell[Rows, Cols];
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
            {
                var src = _cells[r, c];
                // Deep-copy decorations if present
                if (src.Decorations is { Count: > 0 } d)
                {
                    newCells[r, c] = src with { Decorations = new Dictionary<string, string>(d) };
                }
                else
                {
                    // Also deep-copy the Symbols array
                    var syms = src.Symbols is { Length: > 0 } s
                        ? s.ToArray()
                        : src.Symbols;
                    newCells[r, c] = src with { Symbols = syms };
                }
            }
        return new Board(newCells);
    }

    // ── Equality ──────────────────────────────────────────────────────────

    public bool Equals(Board? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Rows != other.Rows || Cols != other.Cols) return false;

        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                if (!_cells[r, c].Equals(other._cells[r, c]))
                    return false;

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as Board);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Rows);
        hash.Add(Cols);
        for (var r = 0; r < Rows; r++)
            for (var c = 0; c < Cols; c++)
                hash.Add(_cells[r, c]);
        return hash.ToHashCode();
    }

    public static bool operator ==(Board? left, Board? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Board? left, Board? right) => !(left == right);
}
