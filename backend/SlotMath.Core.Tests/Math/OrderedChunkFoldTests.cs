using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

public class OrderedChunkFoldTests
{
    [Fact]
    public void Results_AreFoldedInChunkOrder_WhateverOrderTheyArriveIn()
    {
        var folded = new List<(int Chunk, int? Value)>();
        var fold = new OrderedChunkFold<int>(8, (c, v) => folded.Add((c, v)));

        foreach (var c in new[] { 2, 1, 4 })
        {
            Assert.True(fold.Admit(c, () => false));
            fold.Complete(c, c * 10);
        }

        Assert.Empty(folded);
        Assert.Equal(3, fold.Waiting);

        fold.Complete(0, 0);
        Assert.Equal([(0, 0), (1, 10), (2, 20)], folded);
        Assert.Equal(1, fold.Waiting);

        // A chunk that did not run is folded as null and does not hold the rest back.
        fold.Complete(3, null);
        Assert.Equal([(0, 0), (1, 10), (2, 20), (3, null), (4, 40)], folded);
        Assert.Equal(0, fold.Waiting);
    }

    [Fact]
    public async Task SlowChunk_HoldsLaterWorkersBack_SoFinishedResultsDoNotPileUp()
    {
        const int window = 4;
        var folded = new List<int>();
        var fold = new OrderedChunkFold<int>(window, (c, _) => folded.Add(c));

        // Chunk 0 is still running. Chunks 1..3 are inside the window and finish.
        Assert.True(fold.Admit(0, () => false));
        for (var c = 1; c < window; c++)
        {
            Assert.True(fold.Admit(c, () => false));
            fold.Complete(c, c);
        }

        Assert.Equal(window - 1, fold.Waiting);

        // Chunk 4 is outside the window: its worker waits instead of adding a result.
        Task<bool> blocked = Task.Run(() => fold.Admit(window, () => false));
        Assert.NotSame(blocked, await Task.WhenAny(blocked, Task.Delay(300)));
        Assert.Equal(window - 1, fold.Waiting);

        fold.Complete(0, 0);
        Assert.True(await blocked.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal([0, 1, 2, 3], folded);
        Assert.Equal(0, fold.Waiting);
    }

    [Fact]
    public async Task WaitingWorker_IsReleased_WhenTheRunStops()
    {
        var stopped = false;
        var folded = new List<int>();
        var fold = new OrderedChunkFold<int>(2, (c, _) => folded.Add(c));
        fold.Complete(1, 1);

        Task<bool> blocked = Task.Run(() => fold.Admit(5, () => Volatile.Read(ref stopped)));
        Assert.NotSame(blocked, await Task.WhenAny(blocked, Task.Delay(200)));

        Volatile.Write(ref stopped, true);
        Assert.False(await blocked.WaitAsync(TimeSpan.FromSeconds(10)));

        // Chunk 0 never completed: what waited is folded at the end, in order.
        fold.Complete(3, 3);
        fold.FoldRemaining();
        Assert.Equal([1, 3], folded);
        Assert.Equal(0, fold.Waiting);
    }
}
