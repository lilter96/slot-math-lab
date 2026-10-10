using System.Globalization;
using System.Numerics;

namespace SlotMath.Core.Math;

/// <summary>Exact base-ten ledger for shortest round-trip decimal monetary inputs.
/// No currency rounding is applied. A common decimal lattice expands only when a
/// payout requires a finer unit; ordinary integer/zero payouts avoid string parsing.
/// Sampling still uses the authored payout semantics. This contract applies only
/// to external bankroll/wager accounting and is identified in saved evidence.</summary>
internal sealed class SessionMoney
{
    public const string Contract = "decimal-roundtrip-v1";
    private int _exponent;
    private BigInteger _bankroll, _wager, _profit, _payout, _peakProfit, _drawdown;
    private long _rounds;

    public SessionMoney(double bankroll, double wager)
    {
        var bank = Parse(bankroll); var stake = Parse(wager);
        _exponent = System.Math.Min(bank.Exponent, stake.Exponent);
        _bankroll = Units(bank, _exponent); _wager = Units(stake, _exponent);
    }
    public bool InitiallyUnderfunded => _bankroll < _wager;
    public bool CanFundNextWager => _bankroll + _profit >= _wager;
    public bool Profitable => _profit.Sign > 0;
    public double TotalPayout => AsDouble(_payout);
    public double Turnover => AsDouble(_wager * _rounds);
    public double Profit => AsDouble(_profit);
    public double EndingBankroll => AsDouble(_bankroll + _profit);
    public double Drawdown => AsDouble(_drawdown);
    public double Return => _rounds == 0 ? 0 : new Rational(_payout, _wager * _rounds).ToDouble();

    public void Add(double payout)
    {
        var amount = Parse(payout);
        if (amount.Exponent < _exponent)
        {
            var factor = BigInteger.Pow(10, _exponent - amount.Exponent);
            _bankroll *= factor; _wager *= factor; _profit *= factor;
            _payout *= factor; _peakProfit *= factor; _drawdown *= factor;
            _exponent = amount.Exponent;
        }
        var units = Units(amount, _exponent);
        _rounds++; _payout += units; _profit += units - _wager;
        _peakProfit = BigInteger.Max(_peakProfit, _profit);
        _drawdown = BigInteger.Max(_drawdown, _peakProfit - _profit);
    }
    private double AsDouble(BigInteger amount) => _exponent < 0
        ? new Rational(amount, BigInteger.Pow(10, -_exponent)).ToDouble()
        : new Rational(amount * BigInteger.Pow(10, _exponent), BigInteger.One).ToDouble();
    private static BigInteger Units((BigInteger Coefficient, int Exponent) value, int exponent)
        => value.Coefficient.IsZero ? BigInteger.Zero : value.Coefficient * BigInteger.Pow(10, value.Exponent - exponent);

    private static (BigInteger Coefficient, int Exponent) Parse(double value)
    {
        if (!double.IsFinite(value)) throw new ArithmeticException("Session monetary amounts must be finite.");
        if (value == 0) return (BigInteger.Zero, 0);
        if (value >= long.MinValue && value < 9_223_372_036_854_775_808d && value == System.Math.Truncate(value))
            return (new BigInteger((long)value), 0);
        // R is the public JSON-compatible decimal identity of a binary64 value.
        var text = value.ToString("R", CultureInfo.InvariantCulture).AsSpan();
        var e = text.IndexOfAny('E', 'e'); var exponent = 0;
        if (e >= 0) { exponent = int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture); text = text[..e]; }
        var dot = text.IndexOf('.');
        if (dot < 0) return (BigInteger.Parse(text, CultureInfo.InvariantCulture), exponent);
        Span<char> digits = stackalloc char[text.Length - 1];
        text[..dot].CopyTo(digits); text[(dot + 1)..].CopyTo(digits[dot..]);
        return (BigInteger.Parse(digits, CultureInfo.InvariantCulture), exponent - (text.Length - dot - 1));
    }
}
