using System.Numerics;
namespace SlotMath.Core.Monad;

internal interface ISettlementNode
{
    object RawUntyped { get; }
    object Settle(object raw);
}
/// <summary>Exact interpreters unwrap the settled program. Sampled interpreters can observe
/// the raw award before settlement, with no closure shared between workers.</summary>
internal sealed class SettlementSlot<S>(Slot<S, BigInteger> raw, BigInteger cap) : Slot<S, BigInteger>, IAnnotationNode, ISettlementNode
{
    public object RawUntyped => raw;
    public object InnerUntyped => raw.Select(value => (BigInteger)Settle(value));
    public object Settle(object rawValue)
    {
        var value = (BigInteger)rawValue;
        return value.Sign < 0 ? throw new InvalidOperationException("Round payouts must be non-negative.") : BigInteger.Min(value, cap);
    }
}
internal sealed class SettlementContinuation(ISettlementNode settlement, Action<object>? observe) : IFlatMapNode
{
    public object SourceUntyped => settlement.RawUntyped;
    public object ApplyUntyped(object value) { observe?.Invoke(value); return new SettlementPure(settlement.Settle(value)); }
    private sealed class SettlementPure(object value) : IPureNode { public object ValueUntyped => value; }
}
