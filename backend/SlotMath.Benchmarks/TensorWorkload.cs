using System.Globalization;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Benchmarks;

/// <summary>The same product transition authored as local matrices and as ordinary Draw/Branch JSON atoms.</summary>
internal sealed class TensorWorkload
{
    private const int Dimension = 4;

    public TensorWorkload(int components, bool correlated = false, bool sparse = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(components, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(components, 4);
        if (correlated && components == 1)
        {
            throw new ArgumentException("A correlated initial-law benchmark requires at least two components.");
        }

        Components = components;
        Correlated = correlated;
        Sparse = sparse;
        States = Enumerable.Range(0, components).Aggregate(1, (size, _) => size * Dimension);
        Factors = new ExactSparseMatrix[components];
        var initial = new int[components][];
        var transitions = new int[components][][];
        var next = new Rational[components][];
        for (var axis = 0; axis < components; axis++)
        {
            initial[axis] = Enumerable.Range(0, Dimension).Select(index => 1 + (index + axis) % 5).ToArray();
            transitions[axis] = new int[Dimension][];
            var entries = new List<ExactSparseEntry>();
            next[axis] = new Rational[Dimension];
            Array.Fill(next[axis], Rational.Zero);
            for (var row = 0; row < Dimension; row++)
            {
                int axisId = axis, rowId = row;
                transitions[axis][row] = Enumerable.Range(0, Dimension)
                    .Select(column => sparse
                        ? column == rowId ? 1 : column == (rowId + 1) % Dimension ? 2 : 0
                        : 1 + (3 * axisId + 5 * rowId + 7 * column) % 9).ToArray();
                for (var column = 0; column < Dimension; column++)
                {
                    var probability = new Rational(transitions[axis][row][column], transitions[axis][row].Sum());
                    entries.Add(new ExactSparseEntry(row, column, probability));
                    next[axis][column] += new Rational(initial[axis][row], initial[axis].Sum()) * probability;
                }
            }

            Factors[axis] = ExactSparseMatrix.Create(Dimension, entries);
        }

        Input = new Rational[States];
        Oracle = new Rational[States];
        for (var state = 0; state < States; state++)
        {
            Rational input = Rational.One, output = Rational.One;
            var encoded = state;
            for (var axis = components - 1; axis >= 0; axis--)
            {
                var local = encoded % Dimension;
                encoded /= Dimension;
                input *= new Rational(initial[axis][local], initial[axis].Sum());
                output *= next[axis][local];
            }

            Input[state] = input;
            Oracle[state] = output;
        }

        var initialJoint =
            Enumerable.Range(0, States).Select(state => 1 + (state * state + 7 * state) % 17).ToArray();
        if (correlated)
        {
            Array.Fill(Oracle, Rational.Zero);
            for (var state = 0; state < States; state++)
            {
                Input[state] = new Rational(initialJoint[state], initialJoint.Sum());
            }

            // Streaming explicit entry definition is independent of the matrix-free tensor shuffle.
            for (var from = 0; from < States; from++)
            {
                for (var to = 0; to < States; to++)
                {
                    Oracle[to] += Input[from] * Entry(from, to);
                }
            }
        }

        InitialLawFactors = IsProduct(Input);
        if (correlated && components > 1 && InitialLawFactors)
        {
            throw new ArithmeticException("The correlated benchmark requires a genuinely dependent initial law.");
        }

        Graph = Author(initial, transitions, initialJoint);
    }

    public int Components { get; }
    public bool Correlated { get; }
    public bool Sparse { get; }
    public bool InitialLawFactors { get; }
    public int States { get; }
    public ExactSparseMatrix[] Factors { get; }
    public Rational[] Input { get; }
    public Rational[] Oracle { get; }
    public GraphConfig Graph { get; }

    public ExactSparseMatrix Expand()
    {
        var entries = new List<ExactSparseEntry>();
        for (var from = 0; from < States; from++)
        {
            for (var to = 0; to < States; to++)
            {
                Rational probability = Entry(from, to);
                if (!probability.IsZero)
                {
                    entries.Add(new ExactSparseEntry(from, to, probability));
                }
            }
        }

        return ExactSparseMatrix.Create(States, entries);
    }

    private Rational Entry(int from, int to)
    {
        Rational probability = Rational.One;
        for (var axis = Components - 1; axis >= 0; axis--)
        {
            probability *= Factors[axis][from % Dimension, to % Dimension];
            from /= Dimension;
            to /= Dimension;
        }

        return probability;
    }

    private bool IsProduct(Rational[] probability)
    {
        var marginals = new Rational[Components][];
        for (var axis = 0; axis < Components; axis++)
        {
            marginals[axis] = new Rational[Dimension];
            Array.Fill(marginals[axis], Rational.Zero);
        }

        for (var state = 0; state < States; state++)
        {
            var encoded = state;
            for (var axis = Components - 1; axis >= 0; axis--)
            {
                marginals[axis][encoded % Dimension] += probability[state];
                encoded /= Dimension;
            }
        }

        for (var state = 0; state < States; state++)
        {
            var encoded = state;
            Rational product = Rational.One;
            for (var axis = Components - 1; axis >= 0; axis--)
            {
                product *= marginals[axis][encoded % Dimension];
                encoded /= Dimension;
            }

            if (product != probability[state])
            {
                return false;
            }
        }

        return true;
    }

    private GraphConfig Author(int[][] initial, int[][][] transitions, int[] initialJoint)
    {
        var nodes = new List<Node>();
        var edges = new List<Edge>();
        var expressions = new Dictionary<string, Expression>();

        Dictionary<string, Port> Ports(params string[] names)
        {
            return names.ToDictionary(name => name,
                name => new Port
                {
                    Name = name,
                    Type = PortType.State
                });
        }

        void Link(string from, string port, string to)
        {
            edges.Add(new Edge
            {
                Id = from + "-" + port + "-" + to,
                SourceNodeId = from,
                SourcePort = port,
                TargetNodeId = to,
                TargetPort = "in"
            });
        }

        DrawNode Draw(string id, string key, int[] weights, bool root = false)
        {
            return new DrawNode
            {
                Id = id,
                StateWriteKey = key,
                Inputs = root ? [] : Ports("in"),
                Outputs = Ports("out"),
                DrawWeights = weights.Select((weight, index) => new DrawWeight
                {
                    OutcomeId = index.ToString(CultureInfo.InvariantCulture),
                    Weight = weight,
                    Value = index
                }).ToArray()
            };
        }

        Expression encoded = new ConstantExpr
        {
            Kind = ConstantKind.Integer,
            Value = "0"
        };
        var stride = States;
        var schema = new List<StateFieldSchema>();
        if (Correlated)
        {
            schema.Add(new StateFieldSchema
            {
                Name = "origin",
                Type = "string"
            });
            nodes.Add(Draw("origin", "origin", initialJoint, true));
            Link("origin", "out", "component0Branch0");
        }

        for (var axis = 0; axis < Components; axis++)
        {
            stride /= Dimension;
            var prefix = "component" + axis;
            var before = prefix + "From";
            var after = prefix + "To";
            if (!Correlated)
            {
                schema.Add(new StateFieldSchema
                {
                    Name = before,
                    Type = "string"
                });
            }

            schema.Add(new StateFieldSchema
            {
                Name = after,
                Type = "string"
            });
            if (!Correlated)
            {
                nodes.Add(Draw(prefix, before, initial[axis], axis == 0));
                Link(prefix, "out", prefix + "Branch0");
            }

            var successor = axis + 1 == Components
                ? "encode"
                : "component" + (axis + 1) + (Correlated ? "Branch0" : "");
            for (var row = 0; row < Dimension; row++)
            {
                var draw = prefix + "Row" + row;
                nodes.Add(Draw(draw, after, transitions[axis][row]));
                Link(draw, "out", successor);
                if (row == Dimension - 1)
                {
                    continue;
                }

                var branch = prefix + "Branch" + row;
                Expression currentState;
                if (Correlated)
                {
                    Expression quotient = new CallExpr
                    {
                        Function = "floor",
                        Args =
                        [
                            new BinaryExpr
                            {
                                Op = BinaryOp.Div,
                                Left = new CallExpr
                                {
                                    Function = "tonumber",
                                    Args =
                                    [
                                        new FieldAccessExpr
                                        {
                                            Target = "state",
                                            Path = ["origin"]
                                        }
                                    ]
                                },
                                Right = new ConstantExpr
                                {
                                    Kind = ConstantKind.Integer,
                                    Value = stride.ToString(CultureInfo.InvariantCulture)
                                }
                            }
                        ]
                    };
                    currentState = new BinaryExpr
                    {
                        Op = BinaryOp.Sub,
                        Left = quotient,
                        Right = new BinaryExpr
                        {
                            Op = BinaryOp.Mul,
                            Left = new ConstantExpr
                            {
                                Kind = ConstantKind.Integer,
                                Value = "4"
                            },
                            Right = new CallExpr
                            {
                                Function = "floor",
                                Args =
                                [
                                    new BinaryExpr
                                    {
                                        Op = BinaryOp.Div,
                                        Left = quotient,
                                        Right = new ConstantExpr
                                        {
                                            Kind = ConstantKind.Integer,
                                            Value = "4"
                                        }
                                    }
                                ]
                            }
                        }
                    };
                }
                else
                {
                    currentState = new FieldAccessExpr
                    {
                        Target = "state",
                        Path = [before]
                    };
                }

                expressions[branch] = new CompareExpr
                {
                    Op = CompareOp.Eq,
                    Left = currentState,
                    Right = new ConstantExpr
                    {
                        Kind = Correlated ? ConstantKind.Integer : ConstantKind.String,
                        Value = row.ToString(CultureInfo.InvariantCulture)
                    }
                };
                nodes.Add(new BranchNode
                {
                    Id = branch,
                    ConditionId = branch,
                    Inputs = Ports("in"),
                    Outputs = Ports("true", "false")
                });
                Link(branch, "true", draw);
                Link(branch, "false",
                    row + 2 == Dimension ? prefix + "Row" + (row + 1) : prefix + "Branch" + (row + 1));
            }

            encoded = new BinaryExpr
            {
                Op = BinaryOp.Add,
                Left = encoded,
                Right = new BinaryExpr
                {
                    Op = BinaryOp.Mul,
                    Left = new ConstantExpr
                    {
                        Kind = ConstantKind.Integer,
                        Value = stride.ToString(CultureInfo.InvariantCulture)
                    },
                    Right = new CallExpr
                    {
                        Function = "tonumber",
                        Args =
                        [
                            new FieldAccessExpr
                            {
                                Target = "state",
                                Path = [after]
                            }
                        ]
                    }
                }
            };
        }

        expressions["encode"] = encoded;
        schema.Add(new StateFieldSchema
        {
            Name = "win",
            Type = "number"
        });
        nodes.Add(new ModifyStateNode
        {
            Id = "encode",
            OutputKey = "win",
            ExpressionId = "encode",
            Inputs = Ports("in"),
            Outputs = Ports("out")
        });
        nodes.Add(new MetricsSinkNode
        {
            Id = "sink",
            WinStateKey = "win",
            WinCap = States - 1,
            Inputs = Ports("in")
        });
        Link("encode", "out", "sink");
        return new GraphConfig
        {
            SchemaVersion = "1.0.0",
            Name = Correlated
                ? "Product transitions from a correlated initial law"
                : "Independent finite component transitions",
            StateSchema = schema.ToArray(),
            Nodes = nodes.ToArray(),
            Edges = edges.ToArray(),
            Expressions = expressions
        };
    }
}
