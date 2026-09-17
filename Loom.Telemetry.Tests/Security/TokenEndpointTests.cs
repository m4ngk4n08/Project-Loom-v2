using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Loom.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Security;

/// <summary>Bare-WebApplication tests for TokenEndpoints, same pattern as
/// ExplainEndpointTests: going through MapLoomDashboard/AddLoomSecurity would read real
/// env vars CI does not have. These register UserStore/JwtIssuer/JwtValidator/LoginThrottle
/// by hand with scratch key material and call MapLoomTokenEndpoints() directly.</summary>
public sealed class TokenEndpointTests
{
    private sealed class TokenApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required string UsersFilePath { get; init; }
        public required JwtIssuer Issuer { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
            File.Delete(UsersFilePath);
        }
    }

    private static async Task<TokenApi> StartAsync(params string[] usersFileLines)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var usersPath = Path.GetTempFileName();
        File.WriteAllLines(usersPath, usersFileLines);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var users = UserStore.Load(usersPath);
        var issuer = new JwtIssuer(key, TimeProvider.System);
        var validator = new JwtValidator(key, TimeProvider.System);
        var throttle = new LoginThrottle(TimeProvider.System);

        builder.Services.AddSingleton(users);
        builder.Services.AddSingleton(issuer);
        builder.Services.AddSingleton(validator);
        builder.Services.AddSingleton(throttle);

        var app = builder.Build();
        app.MapLoomTokenEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new TokenApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            UsersFilePath = usersPath,
            Issuer = issuer
        };
    }

    private static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    [Fact]
    public async Task NullUsername_Returns400()
    {
        await using var api = await StartAsync($"op:{PasswordHasher.Hash("pw")}");

        var response = await api.Client.PostAsync("/api/token", RawJson("""{"username":null,"password":"x"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NullPassword_Returns400()
    {
        await using var api = await StartAsync($"alice:{PasswordHasher.Hash("pw")}");

        var response = await api.Client.PostAsync("/api/token", RawJson("""{"username":"alice","password":null}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentWrongPasswordLogins_ExactlyFiveAreFourOhOne()
    {
        await using var api = await StartAsync($"op:{PasswordHasher.Hash("correct-horse")}");

        // One serial wrong-password login first, then 20 concurrent ones. Across all 21,
        // exactly LoginThrottle.MaxFailures (5) must be 401 and the rest 429.
        var first = await api.Client.PostAsync("/api/token", RawJson("""{"username":"op","password":"wrong"}"""));

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => api.Client.PostAsync("/api/token", RawJson("""{"username":"op","password":"wrong"}""")))
            .ToArray();
        var rest = await Task.WhenAll(tasks);

        var all = rest.Append(first).ToArray();
        var unauthorized = all.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var tooManyRequests = all.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);

        Assert.Equal(5, unauthorized);
        Assert.Equal(21 - 5, tooManyRequests);
    }

    [Fact]
    public async Task Refresh_RemovedUser_Returns401()
    {
        await using var api = await StartAsync($"alice:{PasswordHasher.Hash("pw")}");

        // "bob" was never in this host's users file (a removed user).
        var bobToken = api.Issuer.Issue("bob", TokenEndpoints.AccessTokenLifetime);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/token/refresh");
        request.Headers.Add("Authorization", $"Bearer {bobToken}");
        var response = await api.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ExistingUser_Returns200()
    {
        await using var api = await StartAsync($"alice:{PasswordHasher.Hash("pw")}");

        var aliceToken = api.Issuer.Issue("alice", TokenEndpoints.AccessTokenLifetime);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/token/refresh");
        request.Headers.Add("Authorization", $"Bearer {aliceToken}");
        var response = await api.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
