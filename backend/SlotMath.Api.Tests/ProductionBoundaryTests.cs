using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Model;

namespace SlotMath.Api.Tests;

[Collection("SerialTests")]
public class ProductionBoundaryTests
{
    [Fact]
    public void EncryptedConfigSnapshot_SurvivesRestartWithoutPlaintext()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-" + Guid.NewGuid());
        try
        {
            var snapshots = new EncryptedSnapshots(directory, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            var first = new InMemoryConfigStore(snapshots);
            var id = first.Create(new GraphConfig { SchemaVersion = "1.0.0", Name = "private-test-math" });
            var second = new InMemoryConfigStore(snapshots);
            Assert.Equal("private-test-math", second.GetLatest(id)!.Config.Name);
            Assert.DoesNotContain("private-test-math", Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "configs.bin"))));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Production_RejectsAnonymousAndForgedDevLogin()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-" + Guid.NewGuid());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("JWT:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Auth:User", "operator");
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2("random-test-password", salt, 210000, HashAlgorithmName.SHA256, 32);
            builder.UseSetting("Auth:PasswordHash", $"pbkdf2:210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}");
            builder.UseSetting("Storage:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseSetting("Storage:Directory", directory);
        });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/configs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/token", new { userId = "operator" })).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/token", new { userId = "operator", password = "random-test-password" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var json = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/configs")).StatusCode);
    }

    [Theory]
    [InlineData("JWT:Secret")]
    [InlineData("Auth:User")]
    [InlineData("Auth:PasswordHash")]
    [InlineData("Storage:Key")]
    public void Production_StartupFails_NamingEachMissingSecret(string missingKey)
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-" + Guid.NewGuid());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            if (missingKey != "JWT:Secret")
                builder.UseSetting("JWT:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            if (missingKey != "Auth:User")
                builder.UseSetting("Auth:User", "operator");
            if (missingKey != "Auth:PasswordHash")
                builder.UseSetting("Auth:PasswordHash", "pbkdf2:210000:c2FsdA==:aGFzaA==");
            if (missingKey != "Storage:Key")
                builder.UseSetting("Storage:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseSetting("Storage:Directory", directory);
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(missingKey, FlattenMessages(exception));
    }

    [Fact]
    public void Production_EnabledPluginsWithoutStorageKey_StillFail_NamingTheKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slotmath-" + Guid.NewGuid());
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Features:Plugins", "true");
            builder.UseSetting("JWT:Secret", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
            builder.UseSetting("Auth:User", "operator");
            builder.UseSetting("Auth:PasswordHash", "pbkdf2:210000:c2FsdA==:aGFzaA==");
            builder.UseSetting("Storage:Directory", directory);
            // Storage:Key deliberately missing: feature flags never bypass the secret checks.
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Storage:Key", FlattenMessages(exception));
    }

    private static string FlattenMessages(Exception exception)
    {
        var messages = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException!)
            messages.AppendLine(current.Message);
        if (exception is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                messages.AppendLine(inner.Message);
        return messages.ToString();
    }
}
