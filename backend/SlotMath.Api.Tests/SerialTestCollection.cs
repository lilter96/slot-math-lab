namespace SlotMath.Api.Tests;

/// <summary>
/// Collection fixture that serializes test execution across classes that
/// share the static EvaluatorRegistry.  Prevents races on register/clear.
/// </summary>
[CollectionDefinition("SerialTests", DisableParallelization = true)]
public class SerialTestCollection
{
}
