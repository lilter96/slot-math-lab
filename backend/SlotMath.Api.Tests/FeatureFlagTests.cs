using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SlotMath.Api.Tests;

/// <summary>
/// Feature flag contract: disabled flags are never registered (404), GET /api/features
/// reports the flags, and unset flags default to enabled locally / to the 1.0 set in production.
/// </summary>
[Collection("SerialTests")]
public class FeatureFlagTests
{
    [Fact]
    public async Task FeaturesEndpoint_ReportsAllFlagsEnabledByDefaultLocally()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var json = await ReadFeaturesAsync(client);

        Assert.True(json.GetProperty("ai").GetBoolean());
        Assert.True(json.GetProperty("autoTune").GetBoolean());
        Assert.True(json.GetProperty("plugins").GetBoolean());
        Assert.True(json.GetProperty("play").GetBoolean());
    }

    [Fact]
    public async Task DisabledFlags_AreNotRegisteredAndAnswer404()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Features:Ai", "false");
            builder.UseSetting("Features:AutoTune", "false");
            builder.UseSetting("Features:Plugins", "false");
            builder.UseSetting("Features:Play", "false");
        });
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/api/ai/generate-graph", "/api/ai/auto-tune", "/api/ai/lint", "/api/ai/explain" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/plugins")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/plugins", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/play/round", new { })).StatusCode);

        var json = await ReadFeaturesAsync(client);
        Assert.False(json.GetProperty("ai").GetBoolean());
        Assert.False(json.GetProperty("autoTune").GetBoolean());
        Assert.False(json.GetProperty("plugins").GetBoolean());
        Assert.False(json.GetProperty("play").GetBoolean());
    }

    [Fact]
    public async Task FlagSetThroughEnvironmentVariable_ParsesStringFalse()
    {
        Environment.SetEnvironmentVariable("Features__Ai", "false");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();

            var json = await ReadFeaturesAsync(client);
            Assert.False(json.GetProperty("ai").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/ai/lint", new { })).StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Features__Ai", null);
        }
    }

    [Fact]
    public async Task ProductionDefaults_AreExposedWithoutLogin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-" + Guid.NewGuid());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("JWT:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Auth:User", "operator");
            builder.UseSetting("Auth:PasswordHash", "pbkdf2:210000:c2FsdA==:aGFzaA==");
            builder.UseSetting("Storage:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseSetting("Storage:Directory", directory);
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/features");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(json.GetProperty("ai").GetBoolean());
        Assert.False(json.GetProperty("autoTune").GetBoolean());
        Assert.False(json.GetProperty("plugins").GetBoolean());
        Assert.True(json.GetProperty("play").GetBoolean());
    }

    private static async Task<JsonElement> ReadFeaturesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/features");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }
}
