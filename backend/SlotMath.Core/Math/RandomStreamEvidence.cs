using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SlotMath.Core.Math;

public sealed record RandomStreamIdentity(long StreamIndex, string ExpansionSeed, long Words, string Prefix, string Sha256);
public sealed record RandomStreamDuplicate(long FirstStream, long OtherStream, string Kind);
public sealed record RandomStreamReport(long Streams, bool Complete, long NonemptyStreams, long ComparablePrefixes,
    RandomStreamDuplicate[] Duplicates, double IdealIndependentPrefixCollisionUpperBound, string Calibration, string Detail)
{ public long ExpectedStreams { get; init; } public double ScheduledInitialWordCollisionProbability { get; init; } public long CollisionCandidatePairs { get; init; } }

/// <summary>Optional instrumentation observes consumed raw words, never draws extra words.
/// Evidence is bounded before admission, rather than silently omitting streams.</summary>
internal sealed class RandomStreamCapture : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly ulong[] _prefix = new ulong[4];
    private long _words;
    public void Add(ulong word)
    {
        if (_words < _prefix.Length) _prefix[_words] = word;
        _words++;
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, word); _hash.AppendData(bytes);
    }
    public RandomStreamIdentity Snapshot(long stream, ulong seed) => new(stream, seed.ToString("x16"), _words,
        string.Concat(_prefix.Take((int)System.Math.Min(4, _words)).Select(x => x.ToString("x16"))), Convert.ToHexString(_hash.GetCurrentHash()).ToLowerInvariant());
    public void Dispose() => _hash.Dispose();
}

internal static class RandomStreamEvidence
{
    public static RandomStreamReport Analyze(IEnumerable<RandomStreamIdentity> identities, long expectedStreams, bool completed)
    {
        var streams = identities.OrderBy(x => x.StreamIndex).ToArray();
        var duplicates = new List<RandomStreamDuplicate>();
        long candidates = 0;
        void Group(Func<RandomStreamIdentity, string> key, IEnumerable<RandomStreamIdentity> selected, string kind)
        {
            foreach (var group in selected.GroupBy(key))
            {
                var count = group.LongCount(); candidates += count * (count - 1) / 2;
                foreach (var other in group.Skip(1)) if (duplicates.Count < 256) duplicates.Add(new(group.First().StreamIndex, other.StreamIndex, kind));
            }
        }
        Group(x => x.ExpansionSeed, streams, "identical-initial-state");
        Group(x => x.Prefix, streams.Where(x => x.Words >= 4), "identical-first-four-raw-words");
        Group(x => $"{x.Words}:{x.Sha256}", streams.Where(x => x.Words > 0), "identical-length-and-sha256-transcript");
        long comparable = streams.LongCount(x => x.Words >= 4);
        return new(streams.Length, completed && streams.LongLength == expectedStreams, streams.LongCount(x => x.Words > 0), comparable, duplicates.ToArray(),
            comparable * (double)(comparable - 1) / 2 * System.Math.Pow(2, -256),
            "Union bound for four independent uniform 64-bit words, a reference null only; xoshiro streams seeded from 64 bits do not meet this null by construction.",
            "Distinct SplitMix stream indexes map bijectively to distinct expansion seeds. Seed duplication is deterministic. A matched transcript digest is a collision candidate, not a proof of identical full transcripts or statistical independence. Empty streams are excluded. Audits cover consumed words including rejection sampling. For this schedule, the first raw xoshiro256** word is a bijection of the 64-bit expansion seed: SplitMix64, multiplication by odd integers, and rotation are invertible. Distinct expansion seeds therefore cannot collide on the first raw word; a complete duplicate raw stream must share that word.") { ExpectedStreams = expectedStreams, ScheduledInitialWordCollisionProbability = streams.Select(x => x.ExpansionSeed).Distinct().Count() == streams.Length ? 0 : 1, CollisionCandidatePairs = candidates };
    }
}
