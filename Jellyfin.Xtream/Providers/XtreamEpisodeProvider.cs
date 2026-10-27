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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client;
using Jellyfin.Xtream.Service;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using EpisodeItem = MediaBrowser.Controller.Entities.TV.Episode;
using SeriesStreamInfo = Jellyfin.Xtream.Client.Models.SeriesStreamInfo;

namespace Jellyfin.Xtream.Providers;

/// <summary>
/// Takes the title, overview and air date of Xtream episodes from TMDB, without asking the Xtream provider:
/// the TMDB id of the series is read from its cached details, which were loaded when its season was opened.
/// Jellyfin cannot do it alone, because channel episodes have no series item to carry the TMDB id.
/// </summary>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
/// <param name="providerManager">Instance of the <see cref="IProviderManager"/> interface.</param>
/// <param name="xtreamClient">Instance of the <see cref="IXtreamClient"/> interface.</param>
public class XtreamEpisodeProvider(ILogger<XtreamEpisodeProvider> logger, IProviderManager providerManager, IXtreamClient xtreamClient) : ICustomMetadataProvider<EpisodeItem>
{
    private const string TmdbProviderName = "TheMovieDb";

    /// <inheritdoc/>
    public string Name => "XtreamEpisodeProvider";

    /// <inheritdoc/>
    public async Task<ItemUpdateType> FetchAsync(EpisodeItem item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        if (item.IndexNumber is not int episodeNumber
            || item.GetParent() is not BaseItem season
            || !StreamService.TryParseSeason(season.ExternalId, out int seriesId, out _))
        {
            return ItemUpdateType.None;
        }

        SeriesStreamInfo? series = xtreamClient.GetCachedSeriesStreams(Plugin.Instance.Creds, seriesId);
        if (StreamService.ParseTmdbId(series?.Info.Tmdb) is not string seriesTmdbId)
        {
            return ItemUpdateType.None;
        }

        IRemoteMetadataProvider<EpisodeItem, EpisodeInfo>? tmdb = providerManager
            .GetMetadataProviders<EpisodeItem>(item, new LibraryOptions(), true)
            .OfType<IRemoteMetadataProvider<EpisodeItem, EpisodeInfo>>()
            .FirstOrDefault(provider => provider.Name == TmdbProviderName);
        if (tmdb is null)
        {
            return ItemUpdateType.None;
        }

        EpisodeInfo info = new()
        {
            IndexNumber = episodeNumber,
            ParentIndexNumber = item.ParentIndexNumber,
            MetadataLanguage = item.GetPreferredMetadataLanguage(),
            MetadataCountryCode = item.GetPreferredMetadataCountryCode(),
            SeriesProviderIds = new Dictionary<string, string> { [nameof(MetadataProvider.Tmdb)] = seriesTmdbId },
        };
        MetadataResult<EpisodeItem> result = await tmdb.GetMetadata(info, cancellationToken).ConfigureAwait(false);
        if (!result.HasMetadata || result.Item is not EpisodeItem found)
        {
            logger.LogDebug("No TMDB episode for series {Series} S{Season}E{Episode}", seriesTmdbId, item.ParentIndexNumber, episodeNumber);
            return ItemUpdateType.None;
        }

        if (!string.IsNullOrWhiteSpace(found.Name))
        {
            item.Name = found.Name;
        }

        if (!string.IsNullOrWhiteSpace(found.Overview))
        {
            item.Overview = found.Overview;
        }

        item.PremiereDate ??= found.PremiereDate;
        item.ProductionYear ??= found.ProductionYear;
        item.CommunityRating ??= found.CommunityRating;
        if (found.GetProviderId(MetadataProvider.Tmdb) is string episodeTmdbId)
        {
            item.SetProviderId(MetadataProvider.Tmdb, episodeTmdbId);
        }

        return ItemUpdateType.MetadataEdit;
    }
}
