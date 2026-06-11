namespace SlotMath.Core.Tests.Benchmarks;

/// <summary>
/// Serialises all Dog House benchmark tests so they don't race on the shared
/// static EvaluatorRegistry / TransformRegistry.
/// </summary>
[CollectionDefinition("DogHouse")]
public sealed class DogHouseTestCollection { }
