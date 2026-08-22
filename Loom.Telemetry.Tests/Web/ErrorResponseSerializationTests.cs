using System.Text.Json;
using Loom.Web.Contracts;
using Loom.Web.Contracts.Dtos;
using Xunit;

namespace Loom.Telemetry.Tests.Web;

/// <summary>
/// Guards against the anonymous-type serialization bug: Results.BadRequest(new { ... })
/// serializes fine under the Debug reflection resolver but throws NotSupportedException
/// under PublishAot=true. ErrorResponse must round-trip through the source-generated
/// LoomJsonSerializerContext with no reflection involved.
/// </summary>
public sealed class ErrorResponseSerializationTests
{
    [Fact]
    public void ErrorResponse_RoundTrips_ThroughSourceGeneratedContext()
    {
        var original = new ErrorResponse { Error = "Unknown metric type: Bogus. Must be Counter, Gauge, or Histogram." };

        var json = JsonSerializer.Serialize(original, LoomJsonSerializerContext.Default.ErrorResponse);
        var roundTripped = JsonSerializer.Deserialize(json, LoomJsonSerializerContext.Default.ErrorResponse);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Error, roundTripped!.Error);
    }
}
