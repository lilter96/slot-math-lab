using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

/// <summary>Samples the runtime's allocation ticks (one per ~100 KB allocated)
/// and reports allocated bytes by type.</summary>
internal sealed class AllocationProfile : EventListener
{
    private readonly ConcurrentDictionary<string, long> _bytes = new();

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName is null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload is null || e.PayloadNames is null) return;
        var type = (string)e.Payload[e.PayloadNames.IndexOf("TypeName")]!;
        var amount = Convert.ToInt64(e.Payload[e.PayloadNames.IndexOf("AllocationAmount64")]);
        _bytes.AddOrUpdate(type, amount, (_, total) => total + amount);
    }

    // Compiler-generated closures are reported by a bare name; say whose they are.
    private static string Owner(string type)
    {
        if (!type.StartsWith('<')) return "";
        var owners = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("SlotMath", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes()).Where(t => t.Name == type && t.DeclaringType is not null)
            .Select(t => $"{t.DeclaringType!.Name} [{string.Join(", ", t.GetFields().Select(f => f.Name))}]");
        return "  in " + string.Join(" | ", owners);
    }

    public void Report(long rounds)
    {
        var total = _bytes.Values.Sum();
        Console.WriteLine($"allocation ticks: {total / (double)rounds:N0} B/round sampled");
        foreach (var (type, bytes) in _bytes.OrderByDescending(p => p.Value).Take(15))
            Console.WriteLine($"  {bytes / (double)rounds,8:N1} B/round  {100.0 * bytes / total,5:N1}%  {type}{Owner(type)}");
    }
}
