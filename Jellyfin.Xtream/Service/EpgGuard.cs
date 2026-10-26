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
using System.Collections.Concurrent;
using System.Threading;

namespace Jellyfin.Xtream.Service;

/// <summary>
/// Protects the provider during a guide refresh, which asks for the programs of every channel one by one.
/// Channels without a guide identifier are not queried, and after several failures in a row the guide is
/// paused for a while: a provider that blocks the address must not receive thousands more requests.
/// </summary>
/// <param name="time">The time provider.</param>
public sealed class EpgGuard(TimeProvider time)
{
    /// <summary>
    /// The number of failures in a row that pauses the guide.
    /// </summary>
    public const int MaxConsecutiveFailures = 5;

    private readonly ConcurrentDictionary<int, bool> _hasGuide = new();
    private readonly object _lock = new();
    private int _failures;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    /// <summary>
    /// Gets how long the guide is paused after too many failures.
    /// </summary>
    public static TimeSpan Pause { get; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets a value indicating whether the guide is paused.
    /// </summary>
    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return time.GetUtcNow() < _pausedUntil;
            }
        }
    }

    /// <summary>
    /// Records which channels have a guide identifier.
    /// </summary>
    /// <param name="streamId">The Xtream stream identifier.</param>
    /// <param name="hasGuide">Whether the provider gives the channel a guide identifier.</param>
    public void SetHasGuide(int streamId, bool hasGuide) => _hasGuide[streamId] = hasGuide;

    /// <summary>
    /// Tells whether the provider should be asked for the programs of a channel.
    /// </summary>
    /// <param name="streamId">The Xtream stream identifier.</param>
    /// <returns><c>false</c> when the channel has no guide or the guide is paused.</returns>
    public bool ShouldQuery(int streamId)
    {
        if (IsPaused)
        {
            return false;
        }

        return !_hasGuide.TryGetValue(streamId, out bool hasGuide) || hasGuide;
    }

    /// <summary>
    /// Records a successful guide request.
    /// </summary>
    public void ReportSuccess() => Interlocked.Exchange(ref _failures, 0);

    /// <summary>
    /// Records a failed guide request.
    /// </summary>
    /// <returns><c>true</c> when this failure pauses the guide.</returns>
    public bool ReportFailure()
    {
        lock (_lock)
        {
            _failures++;
            if (_failures < MaxConsecutiveFailures)
            {
                return false;
            }

            _failures = 0;
            _pausedUntil = time.GetUtcNow() + Pause;
            return true;
        }
    }
}
