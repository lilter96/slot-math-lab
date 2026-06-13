using SlotMath.Core.Compiler;
using SlotMath.Core.Plugins;

namespace SlotMath.Core.Tests.Plugins;

// ═══════════════════════════════════════════════════════════════════════════
//  G12 / D2 / D18 — plugin ABI versioning
//
//  A plugin built against a mismatched ABI version is rejected at registration
//  with a coded error and is NOT registered.
// ═══════════════════════════════════════════════════════════════════════════

public class PluginAbiTests
{
    [Fact]
    public void MatchingAbi_RegistersAndRecordsVersion()
    {
        var host = new PluginHost();

        var reg = host.TryRegisterEvaluator(
            "abi-ok", new ConformantTestEvaluator(),
            PluginHost.CurrentAbiVersion, new ConformanceResult { Passed = true });

        Assert.True(reg.Accepted);
        Assert.Null(reg.Code);
        var entry = host.TryGetEntry("abi-ok");
        Assert.NotNull(entry);
        Assert.Equal(PluginHost.CurrentAbiVersion, entry!.AbiVersion);
    }

    [Fact]
    public void MismatchedAbi_RejectedWithCode_AndNotRegistered()
    {
        var host = new PluginHost();

        var reg = host.TryRegisterEvaluator(
            "abi-bad", new ConformantTestEvaluator(),
            PluginHost.CurrentAbiVersion + 1, new ConformanceResult { Passed = true });

        Assert.False(reg.Accepted);
        Assert.Equal(ErrorCodes.PluginAbiMismatch, reg.Code);
        Assert.NotNull(reg.Reason);
        Assert.Null(host.TryGetEntry("abi-bad")); // rejected ⇒ never registered
    }
}
