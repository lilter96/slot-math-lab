using SlotMath.Core.Model;

namespace SlotMath.Core.Authoring;

/// <summary>One independent finite variable, with an additive integer contribution per ordered outcome.</summary>
public sealed record ConditionalProductInput
{
    public DrawWeight[] Outcomes { get; init; } = [];
    public int[] Contributions { get; init; } = [];
}

/// <summary>An authoring request, not a runtime graph operation.</summary>
public sealed record ConditionalProductDefinition
{
    public ConditionalProductInput[] Inputs { get; init; } = [];
    public long MinimumSum { get; init; }
    public long MaximumSum { get; init; }
    public string Namespace { get; init; } = "product";
    public string OutputKey { get; init; } = "product";
    public bool IncludeOutcomeData { get; init; }
}

/// <summary>Editable ordinary atoms and data. Attach the assets alongside the reusable body.</summary>
public sealed record ConditionalProductComposition
{
    public required CustomMechanic Body { get; init; }
    public required Dictionary<string, DistributionAsset> Distributions { get; init; }
    public required string SourceIdentity { get; init; }
    public required string RandomSchedule { get; init; }
    public required string OutputKey { get; init; }
    public required string[] InternalStateKeys { get; init; }
    public required string TotalMass { get; init; }
    public required int DpCells { get; init; }
    public required long DpTransitions { get; init; }
}

public sealed class ConditionalProductException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}
