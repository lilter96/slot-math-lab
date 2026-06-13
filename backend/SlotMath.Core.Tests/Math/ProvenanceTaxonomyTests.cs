using SlotMath.Core.Math;

namespace SlotMath.Core.Tests.Math;

// ═══════════════════════════════════════════════════════════════════════════
//  D5 — provenance taxonomy + aggregation rule
// ═══════════════════════════════════════════════════════════════════════════

public class ProvenanceTaxonomyTests
{
    [Fact]
    public void Aggregate_EmptyOrAllExact_IsExact()
    {
        Assert.Equal(Provenance.Exact, ProvenanceTag.Aggregate(Array.Empty<Provenance>()));
        Assert.Equal(Provenance.Exact, ProvenanceTag.Aggregate(Provenance.Exact, Provenance.Exact));
    }

    [Fact]
    public void Aggregate_AnyIntervalButNoLossOrSampled_IsInterval()
    {
        Assert.Equal(
            Provenance.ExactInterval,
            ProvenanceTag.Aggregate(Provenance.Exact, Provenance.ExactInterval, Provenance.Exact));
    }

    [Fact]
    public void Aggregate_MassLossBeatsInterval()
    {
        Assert.Equal(
            Provenance.ExactWithMassLoss,
            ProvenanceTag.Aggregate(Provenance.ExactInterval, Provenance.ExactWithMassLoss));
    }

    [Fact]
    public void Aggregate_SampledDominatesEverything()
    {
        Assert.Equal(
            Provenance.Sampled,
            ProvenanceTag.Aggregate(
                Provenance.Exact, Provenance.ExactInterval, Provenance.ExactWithMassLoss, Provenance.Sampled));
    }

    [Fact]
    public void IsRegulatory_OnlyExactAndInterval()
    {
        Assert.True(ProvenanceTag.Exact.IsRegulatory);
        Assert.True(ProvenanceTag.ExactInterval.IsRegulatory);
        Assert.False(ProvenanceTag.ExactWithMassLoss.IsRegulatory);
        Assert.False(ProvenanceTag.Sampled.IsRegulatory);
    }

    [Fact]
    public void Interval_Factory_CarriesBoundsAndSource()
    {
        var tag = ProvenanceTag.Interval(lo: 0.7, hi: 0.75, prunedMass: 1e-9, BoundSource.DeclaredWinCap);
        Assert.Equal(Provenance.ExactInterval, tag.Provenance);
        Assert.Equal(0.7, tag.Lo);
        Assert.Equal(0.75, tag.Hi);
        Assert.Equal(1e-9, tag.PrunedMass);
        Assert.Equal(BoundSource.DeclaredWinCap, tag.BoundSource);
    }

    [Fact]
    public void MassLoss_Factory_IsNonRegulatoryAndCarriesMass()
    {
        var tag = ProvenanceTag.MassLoss(prunedMass: 0.01);
        Assert.Equal(Provenance.ExactWithMassLoss, tag.Provenance);
        Assert.Equal(0.01, tag.PrunedMass);
        Assert.False(tag.IsRegulatory);
    }

    [Fact]
    public void SampledWith_Factory_CarriesCapAndLoopCapHits()
    {
        var tag = ProvenanceTag.SampledWith(
            n: 20_000, mean: 0.88, stdErr: 0.002, ci95: 0.004, capHits: 3, loopCapHits: 7);
        Assert.Equal(Provenance.Sampled, tag.Provenance);
        Assert.Equal(20_000, tag.N);
        Assert.Equal(3, tag.CapHits);
        Assert.Equal(7, tag.LoopCapHits);
    }

    [Fact]
    public void Labels_AreStable()
    {
        Assert.Equal("Exact", ProvenanceTag.Exact.Label);
        Assert.Equal("Exact (±ε)", ProvenanceTag.ExactInterval.Label);
        Assert.Equal("Sampled", ProvenanceTag.Sampled.Label);
    }
}
