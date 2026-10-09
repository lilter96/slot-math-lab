using System.Numerics;
using System.Text;
using SlotMath.Core.Math;
using SlotMath.Core.Monad;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  G32 — VERIFICATION.md regenerator + standing proof gate
//
//  Computes every D9 reference fixture through the exact engine and asserts the
//  result matches the contract's closed form (rational equality, or the REF-B
//  enclosure), then regenerates VERIFICATION.md. If any fixture drifts from its
//  D9 closed form this test fails — the standing proof that the math is right.
// ═══════════════════════════════════════════════════════════════════════════

public class VerificationReportTests
{
    private readonly record struct Row(
        string Fixture, string Quantity, string Expected, string Computed, string Provenance, bool Match);

    [Fact]
    public void Verification_RegeneratesDoc_AndMatchesD9ClosedForms()
    {
        var rows = new List<Row>();

        void Exact(string fixture, string qty, Rational expected, Rational computed, ProvenanceTag prov) =>
            rows.Add(new Row(fixture, qty, expected.ToString(), computed.ToString(),
                prov.Provenance.ToString(), computed == expected));

        // ── REF-A "Coin" ──
        var a = ExactEmitInterpreter.Evaluate(
            ReferenceFixtureTests.RefA(), Unit.Value, _ => BigInteger.Zero, labels: ["base"], winCap: 10);
        Exact("REF-A Coin", "RTP", new Rational(3, 4), a.ExpectedWin, a.Provenance);
        Exact("REF-A Coin", "Variance", new Rational(15, 16), a.Variance, a.Provenance);
        Exact("REF-A Coin", "Hit frequency", new Rational(1, 2), a.HitFrequency, a.Provenance);
        Exact("REF-A Coin", "per-label base", new Rational(3, 4), a.PerLabelExpectation["base"], a.Provenance);

        // ── REF-C "MiniCascade" (vs independent enumerator) ──
        var c = ExactEmitInterpreter.Evaluate(
            ReferenceFixtureTests.RefC(), new ReferenceFixtureTests.CascadeState(0, true),
            s => (BigInteger)(s.CascadesUsed * 2 + (s.Continue ? 1 : 0)), labels: ["cascade_win"], winCap: 100);
        var (cn, cd) = RefCEnumerator.ExpectedWin(10);
        Exact("REF-C MiniCascade", "RTP (= brute-force enumerator E(10))", new Rational(cn, cd), c.ExpectedWin, c.Provenance);

        // ── REF-D "Volcano" ──
        var dUncapped = ExactEmitInterpreter.Evaluate(ReferenceFixtureTests.RefD(), Unit.Value, _ => BigInteger.Zero);
        Exact("REF-D Volcano", "RTP (uncapped)", new Rational(1, 2), dUncapped.ExpectedWin, dUncapped.Provenance);
        var dCapped = ExactEmitInterpreter.Evaluate(
            ReferenceFixtureTests.RefD(), Unit.Value, _ => BigInteger.Zero, winCap: 1000);
        var pCap = ExactEmitInterpreter.ProbabilityAtLeast(dCapped.TotalWin, 1000);
        Exact("REF-D Volcano", "P(cap reached @ 1000)", new Rational(1, 10000), pCap, dCapped.Provenance);

        // ── REF-B "Retrigger" (ExactInterval enclosure) ──
        const int budget = 70;
        var b = ExactEmitInterpreter.Evaluate(
            ReferenceFixtureTests.RefB(budget), new ReferenceFixtureTests.FsState(0, budget),
            s => (BigInteger)(s.Remaining * 1_000_003L + s.Budget), labels: ["base", "freespins"], winCap: 10000);
        var target = new Rational(15, 17);
        var enclosureOk =
            b.ExpectedWin <= target
            && target - b.ExpectedWin < new Rational(1, 1_000_000_000)
            && b.Provenance.Provenance == Provenance.ExactInterval;
        rows.Add(new Row("REF-B Retrigger", "RTP (enclosure of 15/17, 15/17 − r < 1e-9)",
            "≤ 15/17, deficit < 1e-9", b.ExpectedWin.ToString(), b.Provenance.Provenance.ToString(), enclosureOk));

        // ── Gate: every fixture matches its D9 closed form ──
        Assert.All(rows, r => Assert.True(
            r.Match, $"{r.Fixture} — {r.Quantity}: expected {r.Expected}, computed {r.Computed} [{r.Provenance}]"));

        // ── Regenerate VERIFICATION.md ──
        TryWriteRepoFile("VERIFICATION.md", BuildMarkdown(rows));
    }

    private static string BuildMarkdown(IReadOnlyList<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# VERIFICATION — D9 reference fixtures");
        sb.AppendLine();
        sb.AppendLine("**Auto-generated** by `VerificationReportTests` (G32). Every figure below is computed");
        sb.AppendLine("by the exact engine and checked against its D9 closed form on each CI run — the");
        sb.AppendLine("standing proof that the math is right. Amounts are exact rationals (`n/d`, D1).");
        sb.AppendLine();
        sb.AppendLine("| Fixture | Quantity | Expected (D9) | Computed | Provenance | Match |");
        sb.AppendLine("|---------|----------|---------------|----------|------------|-------|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Fixture} | {r.Quantity} | `{r.Expected}` | `{r.Computed}` | {r.Provenance} | {(r.Match ? "✅" : "❌")} |");
        sb.AppendLine();
        sb.AppendLine("All computations use exact `BigInteger` rationals (invariant 1); REF-C is checked");
        sb.AppendLine("against an independent brute-force enumerator that imports nothing from `SlotMath.Core`.");
        return sb.ToString();
    }

    private static void TryWriteRepoFile(string fileName, string content)
    {
        try
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
                dir = dir.Parent;
            if (dir is not null)
                File.WriteAllText(Path.Combine(dir.FullName, fileName), content);
        }
        catch
        {
            // Regeneration is best-effort; the assertions above are the gate.
        }
    }
}
