using Jellyfin.Xtream.Client;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Xtream.Tests;

public sealed class ApiResponseCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "xtream-cache-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AStoredAnswerSurvivesARestart()
    {
        new ApiResponseCache(() => _directory, _time).Store("series|42", "[1,2,3]");

        ApiResponseCache afterRestart = new(() => _directory, _time);

        Assert.True(afterRestart.TryGet("series|42", out CachedResponse response));
        Assert.Equal("[1,2,3]", response.Json);
        Assert.Equal(_time.GetUtcNow(), response.StoredAt);
    }

    [Fact]
    public void TheCacheFileNameDoesNotContainTheKey()
    {
        new ApiResponseCache(() => _directory, _time).Store("http://host|secret-user|&action=x", "[1]");

        string file = Assert.Single(Directory.GetFiles(_directory));
        Assert.DoesNotContain("secret", Path.GetFileName(file), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutDirectoryTheCacheStaysInMemory()
    {
        ApiResponseCache cache = new(() => null, _time);
        cache.Store("key", "[1]");

        Assert.True(cache.TryGet("key", out _));
        Assert.False(new ApiResponseCache(() => null, _time).TryGet("key", out _));
    }

    [Fact]
    public void AnUnknownKeyIsNotFound()
    {
        Assert.False(new ApiResponseCache(() => _directory, _time).TryGet("unknown", out _));
    }
}
