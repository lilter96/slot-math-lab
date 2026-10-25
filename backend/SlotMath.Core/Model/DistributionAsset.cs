namespace SlotMath.Core.Model;

/// <summary>
///     Ordered categorical data shared by ordinary draws. Outcome order, zero-weight
///     positions and values are observable through seeded replay and are retained.
/// </summary>
public sealed record DistributionAsset
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public required DrawWeight[] Outcomes { get; init; }
}
