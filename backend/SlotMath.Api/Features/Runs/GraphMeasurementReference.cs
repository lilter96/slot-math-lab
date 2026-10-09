using SlotMath.Api.Infrastructure;
using SlotMath.Core.Measurements;
namespace SlotMath.Api.Features.Runs;
public sealed record GraphMeasurementReference(string ConfigHash, string? MeasurementHash, RuntimeProvenance RuntimeProvenance, GraphEnumerationReport Report)
{ public DiagnosticRetention? Retention { get; init; } }
