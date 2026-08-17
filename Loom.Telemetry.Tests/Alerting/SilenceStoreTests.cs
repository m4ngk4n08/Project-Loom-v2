using System;
using System.Threading;
using System.Threading.Tasks;
using Loom.Telemetry.Alerting;
using Xunit;

namespace Loom.Telemetry.Tests.Alerting;

public class SilenceStoreTests
{
    [Fact]
    public void InMemorySilenceStore_Constructor_Succeeds()
    {
        // Act
        var store = new InMemorySilenceStore();

        // Assert
        Assert.NotNull(store);
    }

    [Fact]
    public void Silence_NewAlert_IsSilenced()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMinutes(5);

        // Act
        store.Silence("TestAlert", until);

        // Assert
        Assert.True(store.IsSilenced("TestAlert"));
    }

    [Fact]
    public void IsSilenced_NonExistentAlert_ReturnsFalse()
    {
        // Arrange
        var store = new InMemorySilenceStore();

        // Act
        var isSilenced = store.IsSilenced("NonExistent");

        // Assert
        Assert.False(isSilenced);
    }

    [Fact]
    public void IsSilenced_ExpiredSilence_ReturnsFalse()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMilliseconds(50);
        store.Silence("TestAlert", until);

        // Act - wait for expiration
        Thread.Sleep(100);
        var isSilenced = store.IsSilenced("TestAlert");

        // Assert
        Assert.False(isSilenced);
    }

    [Fact]
    public void IsSilenced_ActiveSilence_ReturnsTrue()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMinutes(5);
        store.Silence("TestAlert", until);

        // Act
        var isSilenced = store.IsSilenced("TestAlert");

        // Assert
        Assert.True(isSilenced);
    }

    [Fact]
    public void GetSilencedUntil_ExistingAlert_ReturnsDateTime()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMinutes(5);
        store.Silence("TestAlert", until);

        // Act
        var result = store.GetSilencedUntil("TestAlert");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(until, result.Value);
    }

    [Fact]
    public void GetSilencedUntil_NonExistentAlert_ReturnsNull()
    {
        // Arrange
        var store = new InMemorySilenceStore();

        // Act
        var result = store.GetSilencedUntil("NonExistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Silence_UpdateExisting_UpdatesTimestamp()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var firstUntil = DateTime.UtcNow.AddMinutes(5);
        var secondUntil = DateTime.UtcNow.AddMinutes(10);
        store.Silence("TestAlert", firstUntil);

        // Act
        store.Silence("TestAlert", secondUntil);

        // Assert
        var result = store.GetSilencedUntil("TestAlert");
        Assert.NotNull(result);
        Assert.Equal(secondUntil, result.Value);
    }

    [Fact]
    public void Silence_MultipleAlerts_EachTrackedIndependently()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until1 = DateTime.UtcNow.AddMinutes(5);
        var until2 = DateTime.UtcNow.AddMinutes(10);

        // Act
        store.Silence("Alert1", until1);
        store.Silence("Alert2", until2);

        // Assert
        Assert.True(store.IsSilenced("Alert1"));
        Assert.True(store.IsSilenced("Alert2"));
        Assert.Equal(until1, store.GetSilencedUntil("Alert1"));
        Assert.Equal(until2, store.GetSilencedUntil("Alert2"));
    }

    [Fact]
    public void IsSilenced_RemovesExpiredEntry()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMilliseconds(50);
        store.Silence("TestAlert", until);

        // Act - first check before expiration
        Assert.True(store.IsSilenced("TestAlert"));

        // Wait for expiration and check again
        Thread.Sleep(100);
        var isSilenced = store.IsSilenced("TestAlert");

        // Assert - should be removed now
        Assert.False(isSilenced);
        Assert.Null(store.GetSilencedUntil("TestAlert"));
    }

    [Fact]
    public void Silence_PastTimestamp_IsNotSilenced()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMinutes(-5); // Past time

        // Act
        store.Silence("TestAlert", until);

        // Assert
        Assert.False(store.IsSilenced("TestAlert"));
    }

    [Fact]
    public async Task Silence_ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var store = new InMemorySilenceStore();
        var until = DateTime.UtcNow.AddMinutes(5);
        var tasks = new System.Collections.Generic.List<System.Threading.Tasks.Task>();

        // Act - concurrent writes
        for (int i = 0; i < 10; i++)
        {
            var alertName = $"Alert{i}";
            tasks.Add(System.Threading.Tasks.Task.Run(() => store.Silence(alertName, until)));
        }

        await System.Threading.Tasks.Task.WhenAll(tasks.ToArray());

        // Assert - all alerts should be silenced
        for (int i = 0; i < 10; i++)
        {
            Assert.True(store.IsSilenced($"Alert{i}"));
        }
    }
}
