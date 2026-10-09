using System.Security.Cryptography;
using System.Text;
using SlotMath.Api.Infrastructure;
namespace SlotMath.Api.Tests;

public class DiagnosticRetentionTests
{
    [Fact]
    public void CalculationsDeduplicateAndSurviveEncryptedArchiveRestartWithoutInflatingProgress()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"slotmath-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var snapshots = new EncryptedSnapshots(directory, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var runs = new InMemoryRunStore(snapshots); var run = runs.Create("pinned", 42, 1, 100, new string('a', 64));
            runs.Update(run.Id, "completed", "{}"); var sequence = runs.Get(run.Id)!.Sequence;
            var first = runs.RetainDiagnostic(run.Id, "independent-finite-law", new { probabilities = "1/2,1/2" }, new { mean = "49/50" });
            var repeated = runs.RetainDiagnostic(run.Id, "independent-finite-law", new { probabilities = "1/2,1/2" }, new { mean = "49/50" });
            Assert.True(first.Retained); Assert.Equal(first.ArtifactId, repeated.ArtifactId); Assert.Equal(sequence, runs.Get(run.Id)!.Sequence);
            var restored = new InMemoryRunStore(snapshots).Get(run.Id)!; var artifact = Assert.Single(restored.Diagnostics);
            Assert.Equal(run.ConfigHash, artifact.ConfigHash); Assert.Equal("49/50", artifact.Output.GetProperty("mean").GetString());
            Assert.NotNull(artifact.RuntimeProvenance.CoreBinarySha256); Assert.Equal(64, artifact.OutputSha256.Length);
            Assert.DoesNotContain("49/50", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "runs.bin"))));
            Assert.Equal(runs.Get(run.Id)!.Progress, restored.Progress);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void ArtifactBudgetPreservesExistingEvidenceAndReturnsAnExplicitExportFallback()
    {
        var runs = new InMemoryRunStore(); var run = runs.Create("pinned", 42, 1, 1);
        for (var i = 0; i < 16; i++) Assert.True(runs.RetainDiagnostic(run.Id, "diagnostic", new { i }, new { value = i }).Retained);
        var rejected = runs.RetainDiagnostic(run.Id, "diagnostic", new { i = 16 }, new { value = 16 });
        Assert.False(rejected.Retained); Assert.Contains("export", rejected.Note); Assert.Equal(16, runs.Get(run.Id)!.Diagnostics.Length);
        Assert.True(runs.RetainDiagnostic(run.Id, "diagnostic", new { i = 0 }, new { value = 0 }).Retained);
        Assert.False(runs.RetainDiagnostic("missing", "diagnostic", new { }, new { }).Retained);
    }
}
