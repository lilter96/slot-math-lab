/// <summary>
/// Shared collection for all tests that touch the static EvaluatorRegistry
/// or TransformRegistry, preventing parallel execution collisions.
/// </summary>
[CollectionDefinition("Registry")]
public class RegistryCollection : ICollectionFixture<object>
{
    // No shared fixture needed — the collection just serializes execution.
}
