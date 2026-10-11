using System.Globalization;
using System.Numerics;
using SlotMath.Core.Compiler;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Mechanics.Evaluators;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

/// <summary>
///     Reel-strip draws and the built-in evaluators (GraphCompiler.CompileDraw, CompileMap). An evaluator is engine
///     code and is run as it is, on the values a term takes, never restated: what is added here is only the way its
///     result splits into parts that read few cells each. A payline reads one cell of each reel; a paytable entry
///     under "ways" reads, of each reel, how many of its cells match. The parts add up to what the evaluator
///     returns for the whole board, so the expected payout is the sum of theirs whatever the size of the board's
///     product space.
/// </summary>
internal sealed partial class SymbolicExecutor
{
    // Decimal sums of wins this far below the type's range cannot overflow where the evaluator's own sum does not.
    private const decimal LargestPart = 1e18m;
    private const int MostParts = 100_000;
    private readonly Dictionary<string, IFastPathEvaluator> _evaluators = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Node, string Kind, int Index), NativeFunction> _natives = new();

    private void CheckMap(MapNode map)
    {
        var wins = map.TransformId is not null ||
                   (map.Inputs.Values.Any(port => port.Type == PortType.Board) &&
                    map.Outputs.Values.Any(port => port.Type == PortType.Wins));
        if (map.TransformId is { } id)
        {
            if (id.StartsWith("plugin:", StringComparison.Ordinal))
            {
                throw Declined($"node '{map.Id}' runs a plugin");
            }

            IFastPathEvaluator? evaluator;
            try
            {
                evaluator = GraphCompiler.BuiltInEvaluator(_config, id, map);
            }
            catch (Exception exception) when (exception is CompilationException or InvalidOperationException)
            {
                throw Declined($"node '{map.Id}' names an evaluator this config cannot bind");
            }

            _evaluators[map.Id] = evaluator ??
                                  throw Declined($"node '{map.Id}' is a registered evaluator or transform");
        }

        // Wins are a list of records to whatever reads them as data; here they are known by their amount only.
        if (wins && Reach(map.Id).Any(reached => _nodes[reached] is PutStateNode))
        {
            throw Declined($"the wins of node '{map.Id}' can be stored as data");
        }
    }

    private ReelStrip[] Strips(DrawNode draw)
    {
        if (_config.ReelSets.FirstOrDefault() is not { } reels)
        {
            throw Declined($"draw '{draw.Id}' has neither weights nor reel strips");
        }

        ReelStrip[] strips = reels.StripIds.Select(id => _config.ReelStrips.FirstOrDefault(strip => strip.Id == id))
            .Where(strip => strip is not null).Select(strip => strip!).ToArray();
        if (strips.Length == 0 || strips.Any(strip => strip.Symbols.Length == 0))
        {
            throw Declined($"draw '{draw.Id}' reads an empty reel strip");
        }

        return strips;
    }

    /// <summary>
    ///     One uniform draw over the product of the reels' stops is one uniform draw per reel; a cell is a table
    ///     over its reel's stop.
    /// </summary>
    private Term Spin(DrawNode draw, ref Dictionary<string, Field> state)
    {
        ReelStrip[] strips = Strips(draw);
        var rows = _config.BoardConfig?.Rows ?? 3;
        var cols = strips.Length;
        var occurrence = _drawCount[draw.Id] = _drawCount.GetValueOrDefault(draw.Id) + 1;
        var cells = new Term[System.Math.Max(rows, 0) * cols];
        for (var col = 0; col < cols; col++)
        {
            var symbols = strips[col].Symbols;
            _f.Budget.Tick((long)symbols.Length * (1 + System.Math.Max(rows, 0)));
            var weights = new BigInteger[symbols.Length];
            Array.Fill(weights, BigInteger.One);
            Rv rv = _f.NewRv($"{_scope}{draw.Id}#{occurrence}/{col}", weights);
            for (var row = 0; row < rows; row++)
            {
                var shown = new ExprValue[symbols.Length];
                for (var stop = 0; stop < shown.Length; stop++)
                {
                    shown[stop] = symbols[(stop + row) % symbols.Length] is { } symbol
                        ? ExprValue.String(symbol)
                        : ExprValue.Null;
                }

                cells[GridState.Index(row, col, cols)] = _f.Table(rv, shown);
            }
        }

        Term board = _f.Arr(cells);
        state = new Dictionary<string, Field>(state, StringComparer.Ordinal)
        {
            [draw.BoardStateKey ?? GridState.CellsKey] = new(board, false),
            [GridState.RowsKey] = new(_f.Const(ExprValue.Number(rows)), false),
            [GridState.ColsKey] = new(_f.Const(ExprValue.Number(cols)), false)
        };
        return board;
    }

    private Term Map(MapNode map, Term input, Dictionary<string, Field> state)
    {
        // The expression ports are evaluated whatever the node then does; their product scales every win.
        Term? multiplier = null;
        foreach ((var name, Port port) in map.Inputs)
        {
            if (port.DefaultValue is null || name == "in")
            {
                continue;
            }

            var where = $"port '{name}' of node '{map.Id}'";
            Term value = Statement(port.DefaultValue, state, where);
            if (!value.IsNum || _inference.BoundOf(value) is not { Step.Denominator.IsOne: true })
            {
                throw Declined($"{where} is not provably a whole number");
            }

            multiplier = multiplier is null ? value : _f.Arith(BinaryOp.Mul, multiplier, value);
        }

        if (!_evaluators.TryGetValue(map.Id, out IFastPathEvaluator? evaluator))
        {
            // No evaluator: a board-to-wins node yields no wins, anything else passes its input on.
            return map.Inputs.Values.Any(port => port.Type == PortType.Board) &&
                   map.Outputs.Values.Any(port => port.Type == PortType.Wins)
                ? _f.ZeroT
                : input;
        }

        Term board = Read(state, GridState.CellsKey, map);
        Term rows = Read(state, GridState.RowsKey, map);
        Term cols = Read(state, GridState.ColsKey, map);
        Term amount = evaluator switch
        {
            LinesEvaluator => Lines(map, board, cols),
            WaysEvaluator => Ways(map, board, rows, cols),
            _ => null
        } ?? Part(_f.Native(Native(map.Id, "board", 0, () => new NativeFunction($"{map.TransformId}:{map.Id}",
            arguments => Amount(evaluator.Evaluate(new Dictionary<string, object?>(3, StringComparer.Ordinal)
            {
                [GridState.CellsKey] = arguments[0].ToStateObject(),
                [GridState.RowsKey] = arguments[1].ToStateObject(),
                [GridState.ColsKey] = arguments[2].ToStateObject()
            })))), [board, rows, cols]), map);
        _f.FailSeen = false;
        return multiplier is null ? amount : _f.Arith(BinaryOp.Mul, multiplier, amount);
    }

    private Term Read(Dictionary<string, Field> state, string key, MapNode map)
    {
        if (!state.TryGetValue(key, out Field field))
        {
            return _f.NullT;
        }

        return field.MaybeAbsent
            ? throw Declined($"node '{map.Id}' reads field '{key}', which is not written on every path")
            : field.Value;
    }

    /// <summary>
    ///     One part of an evaluator's amount. It must fail on no reachable outcome, as the evaluator must not; each
    ///     part answers for itself, so that the question is never asked of their sum.
    /// </summary>
    private Term Part(Term part, MapNode map)
    {
        if (part is ConstTerm { IsErr: true } && _guard == _f.TrueT)
        {
            throw Declined($"node '{map.Id}' fails on every round");
        }

        if (part.CanErr)
        {
            Obligations.Add(new Obligation(_guard, part, $"node '{map.Id}'"));
        }

        return part;
    }

    private NativeFunction Native(string node, string kind, int index, Func<NativeFunction> create)
    {
        if (!_natives.TryGetValue((node, kind, index), out NativeFunction? function))
        {
            _natives[(node, kind, index)] = function = create();
        }

        return function;
    }

    /// <summary>
    ///     GraphCompiler.SumWins, where the scaled total is whole. Where it is not the compiler rounds the total, and
    ///     a total rounded is not its parts rounded: that is an error here, and the evaluation is declined.
    /// </summary>
    private ExprValue Amount(Win[] wins)
    {
        var total = 0m;
        foreach (Win win in wins)
        {
            total += win.TotalWin;
        }

        var scaled = total * (decimal)_winScale;
        return scaled == decimal.Truncate(scaled) && decimal.Abs(scaled) <= LargestPart
            ? ExprValue.Number(new BigInteger(scaled))
            : V.Err;
    }

    /// <summary>The most one win of the paytable pays in sub-credit units, when every payout reads as a number.</summary>
    private BigInteger? Largest(IEnumerable<PaytableEntry> entries)
    {
        var most = 0m;
        foreach (PaytableEntry entry in entries)
        {
            foreach (var text in entry.Payouts)
            {
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var payout) ||
                    decimal.Abs(payout) > LargestPart)
                {
                    return null;
                }

                most = System.Math.Max(most, payout);
            }
        }

        try
        {
            return new BigInteger(decimal.Ceiling(most * (decimal)_winScale));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static Term[]? Cells(Term board) => board switch
    {
        OpTerm { Code: Op.Arr } array => array.Args,
        _ => null
    };

    private static int? Size(Term term) =>
        term is ConstTerm { Value.IsInlineInteger: true } constant && constant.Value.InlineInteger is >= 0 and <= 4096
            ? (int)constant.Value.InlineInteger
            : null;

    /// <summary>
    ///     LinesEvaluator pays each payline by itself, from the one cell the line has on each reel: the board's amount
    ///     is the sum over the lines of the same evaluator on that line alone.
    /// </summary>
    private Term? Lines(MapNode map, Term board, Term columns)
    {
        if (Cells(board) is not { } cells || Size(columns) is not { } cols)
        {
            return null;
        }

        Paytable paytable = _config.Paytables[0];
        PaylineSet lines = _config.PaylineSets[0];
        if (lines.Paylines.Length > MostParts)
        {
            return null;
        }

        var wild = _config.Symbols.SingleOrDefault(symbol => symbol.Kind == SymbolKind.Wild)?.Id;
        BigInteger? most = Largest(paytable.Entries);
        var parts = new List<(ExprValue, Term)>(lines.Paylines.Length);
        foreach (Payline line in lines.Paylines)
        {
            var positions = line.Positions;
            if (positions.Length == 0)
            {
                continue;
            }

            // The line as a board of one row: its cells in reel order, read at row zero.
            var length = positions.Length;
            NativeFunction function = Native(map.Id, "line", length, () =>
            {
                var alone = new LinesEvaluator(paytable,
                    new PaylineSet
                    {
                        Id = lines.Id,
                        Paylines =
                        [
                            new Payline
                            {
                                Positions = new int[length]
                            }
                        ]
                    }, wild);
                return new NativeFunction($"line:{map.Id}", arguments =>
                {
                    var row = new object?[length];
                    for (var i = 0; i < length; i++)
                    {
                        row[i] = arguments[i].ToStateObject();
                    }

                    return Amount(alone.Evaluate(new Dictionary<string, object?>(2, StringComparer.Ordinal)
                    {
                        [GridState.CellsKey] = row,
                        [GridState.ColsKey] = length
                    }));
                })
                {
                    Most = most
                };
            });

            var on = new Term[length];
            for (var col = 0; col < length; col++)
            {
                var index = unchecked(positions[col] * cols + col);
                on[col] = index >= 0 && index < cells.Length ? cells[index] : _f.NullT;
            }

            _f.Budget.Tick(length);
            if (_f.Fault == StructuredFault.EvaluatorPartDropped && parts.Count == 0 && lines.Paylines.Length > 1)
            {
                parts.Add((V.Zero, _f.ZeroT));
                continue;
            }

            parts.Add((V.One, Part(_f.Native(function, on), map)));
        }

        return _f.Lin(V.Zero, parts);
    }

    /// <summary>
    ///     WaysEvaluator pays each paytable entry by itself, from how many cells of each reel show the entry's symbol
    ///     or the wild: the board's amount is the sum over the entries of the same evaluator on a board with those
    ///     counts.
    /// </summary>
    private Term? Ways(MapNode map, Term board, Term rowCount, Term columns)
    {
        if (Cells(board) is not { } cells || Size(rowCount) is not { } rows || Size(columns) is not { } cols ||
            rows * cols != cells.Length || cells.Length == 0)
        {
            return null;
        }

        Paytable paytable = _config.Paytables[0];
        if (paytable.Entries.Length > MostParts)
        {
            return null;
        }

        var wild = _config.Symbols.SingleOrDefault(symbol => symbol.Kind == SymbolKind.Wild)?.Id;
        var parts = new List<(ExprValue, Term)>(paytable.Entries.Length);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (PaytableEntry entry in paytable.Entries)
        {
            var symbol = entry.SymbolId;
            if (symbol is null)
            {
                return null;
            }

            if (!seen.TryGetValue(symbol, out var ordinal))
            {
                seen[symbol] = ordinal = seen.Count;
            }

            // A cell that stands for a match on the counted board: the symbol, or the wild where the symbol is no
            // symbol at all; nothing matches when both are empty.
            var match = !string.IsNullOrEmpty(symbol) ? symbol : !string.IsNullOrEmpty(wild) ? wild : null;
            NativeFunction count = Native(map.Id, "count", ordinal, () => new NativeFunction($"count:{map.Id}",
                arguments =>
                {
                    var matches = 0;
                    foreach (ExprValue value in arguments)
                    {
                        var cell = value.ToStateObject();
                        if (!GridState.IsEmpty(cell) && GridState.Symbol(cell) is var shown &&
                            (shown == symbol || shown == wild))
                        {
                            matches++;
                        }
                    }

                    return ExprValue.Number(matches);
                })
            {
                Most = rows
            });
            NativeFunction pays = Native(map.Id, "ways", ordinal, () =>
            {
                // The evaluator looks a symbol's payouts up in the first entry that names it.
                PaytableEntry first = paytable.Entries.First(other => other.SymbolId == symbol);
                var alone = new WaysEvaluator(paytable with
                {
                    Entries = [first]
                }, wild);
                BigInteger? most = Largest([first]) is { } pay ? pay * BigInteger.Pow(rows, cols) : null;
                return new NativeFunction($"ways:{map.Id}", arguments =>
                {
                    var counted = new object?[rows * cols];
                    for (var col = 0; col < cols; col++)
                    {
                        if (!arguments[col].IsInlineInteger || arguments[col].InlineInteger < 0 ||
                            arguments[col].InlineInteger > rows)
                        {
                            return V.Err;
                        }

                        for (var row = 0; row < arguments[col].InlineInteger && match is not null; row++)
                        {
                            counted[GridState.Index(row, col, cols)] = match;
                        }
                    }

                    return Amount(alone.Evaluate(new Dictionary<string, object?>(3, StringComparer.Ordinal)
                    {
                        [GridState.CellsKey] = counted,
                        [GridState.RowsKey] = rows,
                        [GridState.ColsKey] = cols
                    }));
                })
                {
                    Most = most
                };
            });

            var counts = new Term[cols];
            for (var col = 0; col < cols; col++)
            {
                var column = new Term[rows];
                for (var row = 0; row < rows; row++)
                {
                    column[row] = cells[GridState.Index(row, col, cols)];
                }

                counts[col] = _f.Native(count, column);
            }

            _f.Budget.Tick(cells.Length);
            if (_f.Fault == StructuredFault.EvaluatorPartDropped && parts.Count == 0)
            {
                parts.Add((V.Zero, _f.ZeroT));
                continue;
            }

            parts.Add((V.One, Part(_f.Native(pays, counts), map)));
        }

        return _f.Lin(V.Zero, parts);
    }
}
