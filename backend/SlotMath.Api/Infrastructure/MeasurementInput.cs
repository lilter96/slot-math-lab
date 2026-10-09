using System.Text.Json;
using SlotMath.Core.Measurements;
using SlotMath.Core.Model;
namespace SlotMath.Api.Infrastructure;

/// <summary>AST JSON is an explicit transport boundary, as it is for graph configs.
/// It avoids exporting an infinitely recursive polymorphic CLR expression schema.</summary>
public sealed record MeasurementInput
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? NodeId { get; init; }
    public JsonElement? Value { get; init; }
    public JsonElement? Filter { get; init; }
    public JsonElement? Options { get; init; }
    public string Unit { get; init; } = "";
    private static Expression? Parse(JsonElement? value) => value is null || value.Value.ValueKind == JsonValueKind.Null
        ? null : JsonSerializer.Deserialize<Expression>(value.Value.GetRawText(), SlotMath.Core.JsonOptions.Default);
    public MeasurementDefinition ToCore() => new() { Id = Id, Name = Name, NodeId = NodeId, Value = Parse(Value), Filter = Parse(Filter), Unit = Unit,
        Options = Options is null || Options.Value.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize<MeasurementOptions>(Options.Value.GetRawText(), SlotMath.Core.JsonOptions.Default) };
    public static MeasurementInput FromCore(MeasurementDefinition value) => new() { Id = value.Id, Name = value.Name, NodeId = value.NodeId, Unit = value.Unit,
        Value = value.Value is null ? null : JsonSerializer.SerializeToElement(value.Value, SlotMath.Core.JsonOptions.Default),
        Filter = value.Filter is null ? null : JsonSerializer.SerializeToElement(value.Filter, SlotMath.Core.JsonOptions.Default),
        Options = value.Options is null ? null : JsonSerializer.SerializeToElement(value.Options, SlotMath.Core.JsonOptions.Default) };
}
