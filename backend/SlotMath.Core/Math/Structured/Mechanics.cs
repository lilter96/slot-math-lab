using System.Numerics;
using SlotMath.Core.Expressions;
using SlotMath.Core.Mechanics;
using SlotMath.Core.Model;

namespace SlotMath.Core.Math.Structured;

/// <summary>Symbolic execution of the generic reel draw and unnamed map contracts.</summary>
internal sealed partial class SymbolicExecutor
{
    private void CheckMap(MapNode map)
    {
        // Opaque transforms have no authored expression semantics to factor. The caller retains the
        // classic interpreter; symbolic execution never infers a transform's behavior from its identity.
        if (map.TransformId is not null)
        {
            throw Declined($"node '{map.Id}' runs an opaque transform; author its logic with atoms and expressions");
        }

        if (map.Inputs.Values.Any(port => port.Type == PortType.Board) &&
            map.Outputs.Values.Any(port => port.Type == PortType.Wins) &&
            Reach(map.Id).Any(reached => _nodes[reached] is PutStateNode))
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

        // An unnamed map has the compiler's generic pass-through/empty-wins contract.
        return map.Inputs.Values.Any(port => port.Type == PortType.Board) &&
               map.Outputs.Values.Any(port => port.Type == PortType.Wins)
            ? _f.ZeroT
            : input;
    }
}
