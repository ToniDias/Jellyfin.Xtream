using System.Net.Http;
using Jellyfin.Xtream.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;

namespace Jellyfin.Xtream.Tests;

public class ResilientFetcherTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero));
    private readonly List<TimeSpan> _delays = [];
    private readonly ApiResponseCache _cache;
    private readonly ResilientFetcher _fetcher;

    public ResilientFetcherTests()
    {
        _cache = new ApiResponseCache(() => null, _time);
        _fetcher = new ResilientFetcher(_cache, _time, NullLogger.Instance, (delay, _) =>
        {
            _delays.Add(delay);
            return Task.CompletedTask;
        });
    }

    private static Func<CancellationToken, Task<string>> Answers(params string[] answers)
    {
        int index = 0;
        return _ => Task.FromResult(answers[Math.Min(index++, answers.Length - 1)]);
    }

    private Task<List<int>> Fetch(Func<CancellationToken, Task<string>> fetch, bool useCache = true, int maxAttempts = 10, CancellationToken cancellationToken = default) =>
        _fetcher.FetchAsync(
            "key",
            fetch,
            json => JsonConvert.DeserializeObject<List<int>>(json)!,
            list => list.Count > 0,
            useCache,
            maxAttempts,
            cancellationToken);

    [Fact]
    public async Task ReturnsTheFirstNonEmptyAnswerAfterEmptyOnes()
    {
        int calls = 0;
        Func<CancellationToken, Task<string>> answers = Answers("[]", "[]", "[1,2]");
        List<int> result = await Fetch(ct => { calls++; return answers(ct); });

        Assert.Equal([1, 2], result);
        Assert.Equal(3, calls);
        Assert.Equal(2, _delays.Count);
        Assert.Equal(0, _fetcher.DegradedGeneration);
    }

    [Fact]
    public async Task RetriesHttpErrors()
    {
        int calls = 0;
        List<int> result = await Fetch(_ =>
        {
            calls++;
            return calls < 3 ? throw new HttpRequestException("444") : Task.FromResult("[5]");
        });

        Assert.Equal([5], result);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ServesTheLastGoodAnswerWhenEveryAttemptIsEmpty()
    {
        await Fetch(Answers("[7]"));
        _time.Advance(TimeSpan.FromHours(1));

        List<int> result = await Fetch(Answers("[]"));

        Assert.Equal([7], result);
        Assert.Equal(1, _fetcher.DegradedGeneration);
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenNothingUsableWasEverReceived()
    {
        int calls = 0;
        List<int> result = await Fetch(_ => { calls++; return Task.FromResult("[]"); }, maxAttempts: 4);

        Assert.Empty(result);
        Assert.Equal(4, calls);
        Assert.Equal(1, _fetcher.DegradedGeneration);
    }

    [Fact]
    public async Task ThrowsWhenTheProviderNeverAnswers()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Fetch(_ => throw new HttpRequestException("down"), maxAttempts: 3));
        Assert.Equal(1, _fetcher.DegradedGeneration);
    }

    [Fact]
    public async Task AFreshCachedAnswerSkipsTheProvider()
    {
        await Fetch(Answers("[3]"));
        _time.Advance(TimeSpan.FromMinutes(5));

        int calls = 0;
        List<int> result = await Fetch(_ => { calls++; return Task.FromResult("[4]"); });

        Assert.Equal([3], result);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task AnExpiredCachedAnswerIsRefreshed()
    {
        await Fetch(Answers("[3]"));
        _time.Advance(TimeSpan.FromMinutes(20));

        List<int> result = await Fetch(Answers("[4]"));

        Assert.Equal([4], result);
    }

    [Fact]
    public async Task WithoutCacheNothingIsStoredOrServed()
    {
        await Fetch(Answers("[3]"), useCache: false);

        List<int> result = await Fetch(Answers("[]"), useCache: false, maxAttempts: 2);

        Assert.Empty(result);
        Assert.False(_cache.TryGet("key", out _));
    }

    [Fact]
    public async Task CancellationByTheCallerStopsTheRetries()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Fetch(Answers("[]"), cancellationToken: source.Token));
        Assert.Empty(_delays);
    }
}
