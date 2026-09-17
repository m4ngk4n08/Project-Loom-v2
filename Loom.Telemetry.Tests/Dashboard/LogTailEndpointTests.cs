using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Loom.Dashboard.Extensions;
using Loom.Storage;
using Loom.Telemetry;
using Loom.Web.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Loom.Telemetry.Tests.Dashboard;

/// <summary>
/// Covers BACKLOG.md 6.31: /api/logs/tail must not echo a stale (ahead-of-buffer) or
/// negative cursor back to the caller. Same bare-WebApplication pattern as
/// ExplainEndpointTests: only the log group and an ILogStore are registered, no security.
/// </summary>
public sealed class LogTailEndpointTests
{
    private sealed class LogTailApi : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required HttpClient Client { get; init; }
        public required InMemoryLogStore Store { get; init; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static async Task<LogTailApi> StartAsync()
    {
        var store = new InMemoryLogStore();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<ILogStore>(store);

        var app = builder.Build();
        app.MapGroup("/api").MapLogEndpoints();
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new LogTailApi
        {
            App = app,
            Client = new HttpClient { BaseAddress = new Uri(address) },
            Store = store
        };
    }

    private static void WriteThreeRecords(InMemoryLogStore store)
    {
        for (var i = 0; i < 3; i++)
        {
            store.Write(new LogRecord(
                $"message {i}",
                "TestCategory",
                LoomLogLevel.Information,
                DateTime.UtcNow.Ticks));
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo)
        where T : class =>
        JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), typeInfo);

    [Fact]
    public async Task AheadCursor_DoesNotEchoStaleCursor()
    {
        await using var api = await StartAsync();
        WriteThreeRecords(api.Store);

        var response = await api.Client.GetAsync("/api/logs/tail?after=103");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync(response, LoomJsonSerializerContext.Default.LogTailResponse);
        Assert.NotNull(body);
        Assert.Empty(body!.Entries);
        Assert.Equal(3, body.NextSequence);
    }

    [Fact]
    public async Task NegativeCursor_ClampedToZero_ReturnsAllRecords()
    {
        await using var api = await StartAsync();
        WriteThreeRecords(api.Store);

        var response = await api.Client.GetAsync("/api/logs/tail?after=-5");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync(response, LoomJsonSerializerContext.Default.LogTailResponse);
        Assert.NotNull(body);
        Assert.Equal(3, body!.Entries.Length);
        Assert.Equal(3, body.NextSequence);
    }

    [Fact]
    public async Task MidCursor_ReturnsRemainingRecords_UnchangedBehaviour()
    {
        await using var api = await StartAsync();
        WriteThreeRecords(api.Store);

        var response = await api.Client.GetAsync("/api/logs/tail?after=1");

        response.EnsureSuccessStatusCode();
        var body = await ReadAsync(response, LoomJsonSerializerContext.Default.LogTailResponse);
        Assert.NotNull(body);
        Assert.Equal(2, body!.Entries.Length);
        Assert.Equal(3, body.NextSequence);
    }
}
