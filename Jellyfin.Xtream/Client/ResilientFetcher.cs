// Copyright (C) 2022  Kevin Jilissen

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Xtream.Client;

/// <summary>
/// Queries an unreliable provider: retries empty or failed responses, then falls back on the last usable one.
/// Some providers answer the same query with an empty list most of the time; one usable answer is enough.
/// </summary>
/// <param name="cache">The response cache.</param>
/// <param name="time">The time provider.</param>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
/// <param name="delay">Waits between attempts; replaced in tests.</param>
public sealed class ResilientFetcher(
    ApiResponseCache cache,
    TimeProvider time,
    ILogger logger,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>
    /// The default number of attempts for one query.
    /// </summary>
    public const int DefaultMaxAttempts = 10;

    private static readonly TimeSpan[] _delays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1.5),
        TimeSpan.FromSeconds(2),
    ];

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    private int _degradedGeneration;

    private long _pausedUntilTicks;

    /// <summary>
    /// Gets how long every query is paused after the provider refused one (blocked address, banned account).
    /// </summary>
    public static TimeSpan BlockedPause { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets a value indicating whether queries are paused because the provider refused a recent one.
    /// </summary>
    public bool IsPaused => time.GetUtcNow().UtcTicks < Interlocked.Read(ref _pausedUntilTicks);

    /// <summary>
    /// Gets or sets how long a cached response is served without asking the provider again.
    /// The prefetch task refreshes the catalog at startup and every 3 days, so browsing is served from the cache.
    /// </summary>
    public TimeSpan FreshFor { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Gets a counter increased each time a query ends without a usable response.
    /// Channels use it in their cache key, so Jellyfin does not keep a degraded answer for hours.
    /// </summary>
    public int DegradedGeneration => Volatile.Read(ref _degradedGeneration);

    /// <summary>
    /// Reads a cached response, whatever its age, without ever asking the provider.
    /// </summary>
    /// <typeparam name="T">The parsed response type.</typeparam>
    /// <param name="key">The cache key, without credentials.</param>
    /// <param name="parse">Parses the raw response.</param>
    /// <param name="value">The parsed response, when one is cached.</param>
    /// <returns><c>true</c> when a response is cached and parses.</returns>
    public bool TryGetCached<T>(string key, Func<string, T> parse, out T value)
    {
        ArgumentNullException.ThrowIfNull(parse);
        value = default!;
        if (!cache.TryGet(key, out CachedResponse cached))
        {
            return false;
        }

        try
        {
            value = parse(cached.Json);
            return value is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fetches and parses a provider response.
    /// </summary>
    /// <typeparam name="T">The parsed response type.</typeparam>
    /// <param name="key">The cache key, without credentials.</param>
    /// <param name="fetch">Downloads the raw response.</param>
    /// <param name="parse">Parses the raw response.</param>
    /// <param name="isUsable">Tells whether a parsed response can be trusted (for example, a non-empty list).</param>
    /// <param name="useCache">Whether responses are cached and served from the cache.</param>
    /// <param name="maxAttempts">The maximum number of attempts.</param>
    /// <param name="refresh">Whether to ask the provider even when a fresh answer is cached.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The first usable response, else the last cached one, else the last parsed one.</returns>
    public async Task<T> FetchAsync<T>(
        string key,
        Func<CancellationToken, Task<string>> fetch,
        Func<string, T> parse,
        Func<T, bool> isUsable,
        bool useCache,
        int maxAttempts,
        bool refresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(isUsable);

        if (useCache && !refresh && cache.TryGet(key, out CachedResponse fresh) && time.GetUtcNow() - fresh.StoredAt < FreshFor)
        {
            return parse(fresh.Json);
        }

        bool hasParsed = false;
        T lastParsed = default!;
        Exception? lastError = null;
        for (int attempt = 1; attempt <= maxAttempts && !IsPaused; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await fetch(cancellationToken).ConfigureAwait(false);
                T value = parse(json);
                if (isUsable(value))
                {
                    if (useCache)
                    {
                        cache.Store(key, json);
                    }

                    return value;
                }

                hasParsed = true;
                lastParsed = value;
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
                if (IsRefusal(ex))
                {
                    // Insisting would only extend the block: stop all queries for a while.
                    Interlocked.Exchange(ref _pausedUntilTicks, (time.GetUtcNow() + BlockedPause).UtcTicks);
                    logger.LogWarning("The provider refused a request ({Reason}), all queries paused for {Pause}", ex.StatusCode?.ToString() ?? ex.InnerException?.Message, BlockedPause);
                    break;
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                lastError = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // HTTP timeout, not a cancellation by the caller.
                lastError = ex;
            }

            if (attempt < maxAttempts && !IsPaused)
            {
                await _delay(_delays[Math.Min(attempt - 1, _delays.Length - 1)], cancellationToken).ConfigureAwait(false);
            }
        }

        Interlocked.Increment(ref _degradedGeneration);

        if (useCache && cache.TryGet(key, out CachedResponse stale))
        {
            logger.LogWarning("No usable answer from the provider after {Attempts} attempts, serving the cached answer of {StoredAt}", maxAttempts, stale.StoredAt);
            return parse(stale.Json);
        }

        if (hasParsed)
        {
            logger.LogWarning("No usable answer from the provider after {Attempts} attempts, returning its last answer", maxAttempts);
            return lastParsed;
        }

        throw new HttpRequestException(
            IsPaused ? "Provider queries are paused after a refusal." : $"The provider did not answer after {maxAttempts} attempts.",
            lastError);
    }

    /// <summary>
    /// Tells whether an error means that the provider refuses us, as opposed to a transient failure.
    /// </summary>
    /// <param name="ex">The request error.</param>
    /// <returns><c>true</c> for 401, 403, 429, 444 or a refused connection.</returns>
    public static bool IsRefusal(HttpRequestException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or (HttpStatusCode)444
            || ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
    }
}
