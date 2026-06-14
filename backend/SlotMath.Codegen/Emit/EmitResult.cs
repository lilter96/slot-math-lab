namespace SlotMath.Codegen.Emit;

/// <summary>
/// Outcome of emitting C# for a graph.  When <see cref="Supported"/> is false
/// the graph uses a construct the G7 emitter does not yet handle; the
/// <see cref="Diagnostics"/> say which.  The emitter NEVER emits code it cannot
/// prove equivalent — it reports a diagnostic instead (no silent miscompile).
/// </summary>
public sealed record EmitResult
{
    public required bool Supported { get; init; }

    /// <summary>The generated C# source (null when unsupported).</summary>
    public string? Source { get; init; }

    /// <summary>Fully-qualified type name of the generated class.</summary>
    public string FullTypeName { get; init; } = "SlotMath.Generated.GeneratedGame";

    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public static EmitResult Unsupported(params string[] diagnostics) =>
        new() { Supported = false, Diagnostics = diagnostics };
}

/// <summary>Raised internally when an expression/node is outside the emitter's subset.</summary>
public sealed class CodegenUnsupportedException(string message) : Exception(message);
