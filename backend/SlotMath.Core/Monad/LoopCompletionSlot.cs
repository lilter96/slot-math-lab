namespace SlotMath.Core.Monad;

internal interface ILoopCompletionNode
{
    string NodeId { get; }
    string IterationKey { get; }
    int MaximumIterations { get; }
}

// Located after loop completion, before the exit chain. Reading the existing
// counter never re-evaluates the stopping expression or consumes random draws.
// Exact interpreters transparently unwrap it; samplers record only settled rounds.
internal sealed class LoopCompletionSlot<S, T>(string nodeId, string iterationKey, int maximumIterations, Slot<S, T> inner)
    : Slot<S, T>, IAnnotationNode, ILoopCompletionNode
{
    public string NodeId => nodeId;
    public string IterationKey => iterationKey;
    public int MaximumIterations => maximumIterations;
    public object InnerUntyped => inner;
}
