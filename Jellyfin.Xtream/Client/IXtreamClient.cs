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
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client.Models;

#pragma warning disable CS1591
namespace Jellyfin.Xtream.Client;

public interface IXtreamClient
{
    /// <summary>
    /// Gets a counter increased each time a query ends without a usable response from the provider.
    /// </summary>
    int DegradedGeneration { get; }

    /// <summary>
    /// Gets a value indicating whether queries are paused because the provider refused a recent one.
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// Asks the provider again for every query made until the returned scope is disposed,
    /// even when a fresh answer is cached. Used by the catalog prefetch task.
    /// </summary>
    /// <returns>The refresh scope.</returns>
    IDisposable StartCatalogRefresh();

    /// <summary>
    /// Reads the details of a series from the cache only, without asking the provider.
    /// </summary>
    /// <param name="connectionInfo">The provider account.</param>
    /// <param name="seriesId">The Xtream id of the series.</param>
    /// <returns>The cached details, or <c>null</c> when the series was never opened.</returns>
    SeriesStreamInfo? GetCachedSeriesStreams(ConnectionInfo connectionInfo, int seriesId);

    Task<EpgListings> GetEpgInfoAsync(ConnectionInfo connectionInfo, int streamId, CancellationToken cancellationToken);

    Task<List<Category>> GetLiveCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken);

    Task<List<StreamInfo>> GetLiveStreamsAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken);

    Task<List<StreamInfo>> GetLiveStreamsByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken);

    Task<List<Series>> GetSeriesByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken);

    Task<List<Category>> GetSeriesCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken);

    Task<SeriesStreamInfo> GetSeriesStreamsBySeriesAsync(ConnectionInfo connectionInfo, int seriesId, CancellationToken cancellationToken);

    Task<PlayerApi> GetUserAndServerInfoAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken);

    Task<List<Category>> GetVodCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken);

    Task<VodStreamInfo> GetVodInfoAsync(ConnectionInfo connectionInfo, int streamId, CancellationToken cancellationToken);

    Task<List<StreamInfo>> GetVodStreamsByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken);
}
