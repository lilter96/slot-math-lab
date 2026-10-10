namespace SlotMath.Core.Math;

/// <summary>
///     Hands chunk results to <paramref name="fold" /> in strict ascending chunk order (D3),
///     whatever order the workers finish in. A worker is held back while its chunk is
///     <paramref name="window" /> or more ahead of the first chunk not yet folded, so at
///     most <c>window − 1</c> finished results wait, however slow one chunk is.
/// </summary>
/// <param name="window">How far past the first unfolded chunk a worker may start.</param>
/// <param name="fold">Receives each chunk in order; <c>null</c> for a chunk that did not run.</param>
internal sealed class OrderedChunkFold<TResult>(int window, Action<int, TResult?> fold)
    where TResult : struct
{
    private readonly object _gate = new();
    private readonly Dictionary<int, TResult?> _waiting = [];
    private int _frontier;

    /// <summary>Finished chunks that wait for an earlier one.</summary>
    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count;
            }
        }
    }

    /// <summary>
    ///     Blocks until chunk <paramref name="chunk" /> is inside the window. Returns
    ///     <c>false</c> when <paramref name="stopped" /> turns true first; the chunk must
    ///     then not be run.
    /// </summary>
    public bool Admit(int chunk, Func<bool> stopped)
    {
        lock (_gate)
        {
            while (chunk - _frontier >= window)
            {
                if (stopped())
                {
                    return false;
                }

                // Woken when the frontier moves; the timeout picks up a stop.
                Monitor.Wait(_gate, 50);
            }
        }

        return true;
    }

    /// <summary>Records the outcome of an admitted chunk and folds every chunk that is now in order.</summary>
    public void Complete(int chunk, TResult? result)
    {
        lock (_gate)
        {
            _waiting.Add(chunk, result);
            var moved = false;
            while (_waiting.Remove(_frontier, out TResult? next))
            {
                fold(_frontier, next);
                _frontier++;
                moved = true;
            }

            if (moved)
            {
                Monitor.PulseAll(_gate);
            }
        }
    }

    /// <summary>
    ///     Folds what still waits behind a chunk that was never completed, in ascending
    ///     order. Call once, after every worker has stopped.
    /// </summary>
    public void FoldRemaining()
    {
        lock (_gate)
        {
            foreach (var chunk in _waiting.Keys.Order())
            {
                fold(chunk, _waiting[chunk]);
            }

            _waiting.Clear();
        }
    }
}
