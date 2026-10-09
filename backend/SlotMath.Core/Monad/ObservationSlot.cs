namespace SlotMath.Core.Monad;

// Exact interpreters treat this as a transparent annotation. Only an explicitly
// instrumented sampled run observes it; observation cannot alter game state.
internal interface IObservationNode { string NodeId { get; } }
internal sealed class ObservationSlot<S, T>(string nodeId, Slot<S, T> inner) : Slot<S, T>, IAnnotationNode, IObservationNode
{
    public string NodeId => nodeId;
    public object InnerUntyped => inner;
}
