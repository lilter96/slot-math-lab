namespace SlotMath.Core.Mechanics.Transforms;

/// <summary>Direction for ExpandTransform.</summary>
public enum ExpandDirection
{
    Row,
    Column
}

/// <summary>Direction for NudgeTransform.</summary>
public enum NudgeDirection
{
    Up,
    Down
}

/// <summary>
/// What data to extract from each matching cell — configures BoardCellAccumulatorTransform.
/// </summary>
public enum CellExtractMode
{
    /// <summary>Extract "row,col" coordinate strings. Use with CellMergeMode.Union for sticky positions.</summary>
    Position,

    /// <summary>Extract the cell's first symbol id. Use with CellMergeMode.Union for collection mechanics.</summary>
    Symbol,

    /// <summary>Count matching cells per board. Use with numeric merge modes for meters/accumulators.</summary>
    Count,
}

/// <summary>
/// How to merge this board's extracted data with previously accumulated state —
/// configures BoardCellAccumulatorTransform.
/// </summary>
public enum CellMergeMode
{
    /// <summary>Set union: accumulates all unique values seen across iterations. String[] state.</summary>
    Union,

    /// <summary>Replace: only keeps the current board's data; previous data discarded. String[]/int state.</summary>
    Replace,

    /// <summary>Numeric sum: adds current count to accumulated total. Integer state.</summary>
    Sum,

    /// <summary>Numeric max: keeps the highest count seen. Integer state.</summary>
    Max,
}

/// <summary>
/// How to apply accumulated state data back to the board — configures BoardCellApplyTransform.
/// </summary>
public enum CellApplyMode
{
    /// <summary>
    /// Read position list from state (string[] of "row,col"), set those cells to SymbolId.
    /// Used for sticky wilds, Hold-and-Win symbol persistence, etc.
    /// </summary>
    OverlaySymbol,

    /// <summary>
    /// Read position list from state, lock those cells (cell.IsLocked = true).
    /// Used for locked-cell mechanics, frozen positions.
    /// </summary>
    LockCells,
}
