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
using System.Linq;
using Jellyfin.Xtream.Client.Models;

namespace Jellyfin.Xtream.Service;

/// <summary>
/// Decides when the guide of a channel must be asked again: only when the saved programs are about to run out.
/// A provider that gives several days of guide is then asked once every few days per channel, not daily.
/// </summary>
public static class EpgCoverage
{
    /// <summary>
    /// Gets how much guide must remain ahead; below that, the channel is asked again.
    /// </summary>
    public static TimeSpan Margin { get; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Tells whether the guide of a channel must be asked again.
    /// </summary>
    /// <param name="saved">The saved guide of the channel, if any.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns><c>true</c> when nothing is saved or the saved programs end within <see cref="Margin"/>.</returns>
    public static bool NeedsRefresh(EpgListings? saved, DateTime nowUtc)
    {
        if (saved?.Listings is null || saved.Listings.Count == 0)
        {
            return true;
        }

        DateTime lastEnd = saved.Listings.Max(listing => listing.End.ToUniversalTime());
        return lastEnd < nowUtc + Margin;
    }
}
