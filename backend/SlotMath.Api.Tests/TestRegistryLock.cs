namespace SlotMath.Api.Tests;

/// <summary>
/// Shared lock for evaluator/transform registry access across test classes.
/// </summary>
public static class TestRegistryLock
{
    public static readonly object Lock = new();
}
