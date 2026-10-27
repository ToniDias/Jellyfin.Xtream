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
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Xtream.Service;

/// <summary>
/// Pace of the nightly catalog indexing: one category at a time, a random pause between two,
/// inside a night window, the categories never indexed first and then the oldest.
/// </summary>
public static class IndexSchedule
{
    /// <summary>
    /// Hour (local time) at which the nightly run starts.
    /// </summary>
    public const int WindowStartHour = 1;

    /// <summary>
    /// Hour (local time) at which the nightly run stops.
    /// </summary>
    public const int WindowEndHour = 6;

    /// <summary>
    /// Gets how long a run started outside the night window (by hand) may last.
    /// </summary>
    public static TimeSpan ManualRunLength { get; } = TimeSpan.FromHours(WindowEndHour - WindowStartHour);

    /// <summary>
    /// Gives the moment a run started at <paramref name="localStart"/> must stop: the end of the night window,
    /// or <see cref="ManualRunLength"/> later for a run started by hand during the day.
    /// </summary>
    /// <param name="localStart">The local start time.</param>
    /// <returns>The local deadline.</returns>
    public static DateTimeOffset Deadline(DateTimeOffset localStart)
    {
        if (localStart.Hour >= WindowStartHour && localStart.Hour < WindowEndHour)
        {
            return new DateTimeOffset(localStart.Date.AddHours(WindowEndHour), localStart.Offset);
        }

        return localStart + ManualRunLength;
    }

    /// <summary>
    /// Draws the pause before the next category, uniformly between the two bounds.
    /// </summary>
    /// <param name="random">The random source.</param>
    /// <param name="minSeconds">The shortest pause, in seconds.</param>
    /// <param name="maxSeconds">The longest pause, in seconds.</param>
    /// <returns>The pause.</returns>
    public static TimeSpan NextPause(Random random, int minSeconds, int maxSeconds)
    {
        ArgumentNullException.ThrowIfNull(random);
        int min = Math.Max(0, Math.Min(minSeconds, maxSeconds));
        int max = Math.Max(min, Math.Max(minSeconds, maxSeconds));
        return TimeSpan.FromSeconds(min + ((max - min) * random.NextDouble()));
    }

    /// <summary>
    /// Orders the categories: never indexed first (in their listing order), then the least recently indexed.
    /// </summary>
    /// <param name="keys">The category keys, in listing order.</param>
    /// <param name="lastIndexed">When each category was last indexed.</param>
    /// <returns>The keys in indexing order.</returns>
    public static IReadOnlyList<string> Order(IEnumerable<string> keys, IReadOnlyDictionary<string, DateTime> lastIndexed)
    {
        ArgumentNullException.ThrowIfNull(lastIndexed);
        return keys
            .Distinct()
            .Select((key, index) => (key, index, last: lastIndexed.TryGetValue(key, out DateTime t) ? t : DateTime.MinValue))
            .OrderBy(x => x.last)
            .ThenBy(x => x.index)
            .Select(x => x.key)
            .ToList();
    }
}
