namespace SlotMath.Core.Math;

public enum ExactStorageKind
{
    TermObjects,
    RandomWeightSlots,
    CachedLawAtoms,
    LawBuilderCapacity,
    LawOrderCapacity,
    LoopKernelCoefficientSlots,
    LoopExitCoefficientSlots,
    TransferCoefficientSlots,
    MatrixCoefficientSlots,
    ConvolutionLatticeCapacity,
    ConvolutionResultSlots,
    TransformResidueCapacity
}

public sealed record ExactStorageCounter(ExactStorageKind Kind, long Current, long Peak);

/// <summary>Logical cache ownership and numerical buffer slots. Counts are not bytes or a complete GC heap profile.</summary>
public sealed record ExactStorageSnapshot(IReadOnlyList<ExactStorageCounter> Counters);

internal sealed class ExactStorageTracker
{
    private readonly List<(ExactStorageKind Kind, Array Array)> _arrayOrder = [];
    private readonly HashSet<(ExactStorageKind Kind, Array Array)> _arrays = [];
    private readonly long[] _current = new long[Enum.GetValues<ExactStorageKind>().Length];
    private readonly long[] _peak = new long[Enum.GetValues<ExactStorageKind>().Length];

    public void Add(ExactStorageKind kind, long count)
    {
        var index = (int)kind;
        var next = checked(_current[index] + count);
        if (next < 0)
        {
            throw new InvalidOperationException("Storage ownership was released twice.");
        }

        _current[index] = next;
        _peak[index] = System.Math.Max(_peak[index], next);
    }

    public void RetainArray(ExactStorageKind kind, Array array)
    {
        (ExactStorageKind kind, Array array) key = (kind, array);
        if (!_arrays.Add(key))
        {
            return;
        }

        _arrayOrder.Add(key);
        Add(kind, array.LongLength);
    }

    public Lease Hold(ExactStorageKind kind, long count = 0) => new(this, kind, count);
    public IDisposable Scope() => new ScopeOwner(this);
    public IDisposable ArrayScope(ExactStorageKind kind) => new ArrayOwner(this, kind);

    public ExactStorageSnapshot Snapshot() => new(Array.AsReadOnly(Enum.GetValues<ExactStorageKind>()
        .Select(kind => new ExactStorageCounter(kind, _current[(int)kind], _peak[(int)kind])).ToArray()));

    internal sealed class Lease : IDisposable
    {
        private readonly ExactStorageKind _kind;
        private readonly ExactStorageTracker _owner;
        private bool _kept, _disposed;

        public Lease(ExactStorageTracker owner, ExactStorageKind kind, long count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _owner = owner;
            _kind = kind;
            owner.Add(kind, count);
            Count = count;
        }

        public long Count { get; private set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!_kept)
            {
                _owner.Add(_kind, -Count);
            }
        }

        public void Resize(long count)
        {
            if (_disposed || count < 0)
            {
                throw new InvalidOperationException("Invalid storage lease resize.");
            }

            _owner.Add(_kind, count - Count);
            Count = count;
        }

        public void Keep() => _kept = true;
    }

    private sealed class ScopeOwner : IDisposable
    {
        private readonly int _arrays;
        private readonly long[] _baseline;
        private readonly ExactStorageTracker _owner;
        private bool _disposed;

        public ScopeOwner(ExactStorageTracker owner)
        {
            _owner = owner;
            _baseline = (long[])owner._current.Clone();
            _arrays = owner._arrayOrder.Count;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _baseline.CopyTo(_owner._current, 0);
            for (var at = _owner._arrayOrder.Count - 1; at >= _arrays; at--)
            {
                _owner._arrays.Remove(_owner._arrayOrder[at]);
            }

            _owner._arrayOrder.RemoveRange(_arrays, _owner._arrayOrder.Count - _arrays);
        }
    }

    private sealed class ArrayOwner(ExactStorageTracker owner, ExactStorageKind kind) : IDisposable
    {
        private readonly long _baseline = owner._current[(int)kind];
        private readonly int _start = owner._arrayOrder.Count;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner._current[(int)kind] = _baseline;
            for (var at = owner._arrayOrder.Count - 1; at >= _start; at--)
            {
                if (owner._arrayOrder[at].Kind != kind)
                {
                    continue;
                }

                owner._arrays.Remove(owner._arrayOrder[at]);
                owner._arrayOrder.RemoveAt(at);
            }
        }
    }
}
