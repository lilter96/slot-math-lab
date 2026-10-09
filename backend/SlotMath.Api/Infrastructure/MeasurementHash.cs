using System.Security.Cryptography;
using System.Text.Json;
using SlotMath.Core.Measurements;

namespace SlotMath.Api.Infrastructure;
public static class MeasurementHash
{
    public static string Compute(IReadOnlyList<MeasurementDefinition> definitions) => Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(definitions, SlotMath.Core.JsonOptions.Default)));
}
