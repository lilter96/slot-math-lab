using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SlotMath.Api.Infrastructure;
using SlotMath.Core;
using SlotMath.Core.Compiler;
using SlotMath.Core.Math;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public class CompiledGraphCacheTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void IdenticalGraphsShareOnePlan_EditingInputsInvalidatesIt()
    {
        var cache = factory.Services.GetRequiredService<CompiledGraphCache>();
        var config = JsonSerializer.Deserialize<GraphConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DogHouse", "dog-house-ui.json")), JsonOptions.Default)!;
        var plans = new CompileResult[16];
        Parallel.For(0, plans.Length, i => plans[i] = cache.Compile(config));
        foreach (var plan in plans) { Assert.True(plan.IsValid); Assert.Same(plans[0], plan); }
        var defaults = new Dictionary<string, JsonElement>(config.InitialState!) { ["traceEnabled"] = JsonSerializer.SerializeToElement(true) };
        var edited = cache.Compile(config with { InitialState = defaults });
        Assert.NotSame(plans[0], edited);
        var original = SampledInterpreter.RunSingle(plans[0].Program!, new Dictionary<string, object?>(), 42, 230);
        var traced = SampledInterpreter.RunSingle(edited.Program!, new Dictionary<string, object?>(), 42, 230);
        Assert.Equal(original.Value, traced.Value);
        Assert.False((bool)original.State["traceEnabled"]!); Assert.True((bool)traced.State["traceEnabled"]!);
        Assert.Same(plans[0], cache.Compile(config));
    }
}
