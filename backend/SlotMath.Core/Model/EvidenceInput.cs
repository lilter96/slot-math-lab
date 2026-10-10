namespace SlotMath.Core.Model;

/// <summary>External input identity bound by the graph hash. Hashes are authored
/// assertions; a URI is metadata and is never fetched by execution.</summary>
public sealed record EvidenceInput(string Kind, string Id, string Version, string Sha256, string? Uri = null, string? ContentBase64 = null)
{
    public static void Validate(IReadOnlyList<EvidenceInput> inputs)
    {
        if (inputs.Count > 64 || inputs.Any(x => x is null || x.Kind is not ("rule" or "specification" or "asset")
            || string.IsNullOrWhiteSpace(x.Id) || x.Id.Length > 128 || string.IsNullOrWhiteSpace(x.Version) || x.Version.Length > 128
            || x.Sha256 is null || x.Sha256.Length != 64 || x.Sha256.Any(c => !System.Uri.IsHexDigit(c)) || x.Uri?.Length > 2048)
            || inputs.Select(x => (x.Kind, x.Id)).Distinct().Count() != inputs.Count)
            throw new ArgumentException("External evidence requires up to 64 unique kind/ID identities, versions and SHA-256 digests.");
        if (inputs.Sum(x => (long)(x.ContentBase64?.Length ?? 0)) > 174764) throw new ArgumentException("Embedded evidence exceeds 128 KiB.");
        foreach (var input in inputs.Where(x => x.ContentBase64 is not null))
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(input.ContentBase64!); } catch (FormatException) { throw new ArgumentException("Evidence content is not valid base64."); }
            if (bytes.Length > 65536 || !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(input.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Embedded evidence exceeds 64 KiB or does not match its SHA-256 identity.");
        }
    }
}
