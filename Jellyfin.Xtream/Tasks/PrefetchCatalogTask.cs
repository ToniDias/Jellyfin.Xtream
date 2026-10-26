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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client;
using Jellyfin.Xtream.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Xtream.Tasks;

/// <summary>
/// Loads the provider catalog of the selected categories into the plugin cache, every night and after
/// each configuration change, so that opening a folder does not wait for a slow or flaky provider.
/// Only lists are loaded (categories, movies, series); the details of a series are loaded when it is opened.
/// </summary>
/// <param name="xtreamClient">Instance of the <see cref="IXtreamClient"/> interface.</param>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
public class PrefetchCatalogTask(IXtreamClient xtreamClient, ILogger<PrefetchCatalogTask> logger) : IScheduledTask
{
    /// <inheritdoc />
    public string Name => "Prefetch Xtream catalog";

    /// <inheritdoc />
    public string Key => "XtreamPrefetchCatalog";

    /// <inheritdoc />
    public string Description => "Loads the selected Xtream categories into the cache so that browsing them is fast.";

    /// <inheritdoc />
    public string Category => "Live TV";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(4).Ticks,
            },
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.StartupTrigger,
            },
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        Plugin plugin = Plugin.Instance;
        PluginConfiguration config = plugin.Configuration;
        ConnectionInfo creds = plugin.Creds;
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            return;
        }

        List<Func<Task>> steps = [];
        if (config.LiveTv.Count > 0)
        {
            steps.Add(() => xtreamClient.GetLiveStreamsAsync(creds, cancellationToken));
        }

        if (config.IsVodVisible && config.Vod.Count > 0)
        {
            steps.Add(() => xtreamClient.GetVodCategoryAsync(creds, cancellationToken));
            steps.AddRange(config.Vod.Keys.Select(id => (Func<Task>)(() => xtreamClient.GetVodStreamsByCategoryAsync(creds, id, cancellationToken))));
        }

        if (config.IsSeriesVisible && config.Series.Count > 0)
        {
            steps.Add(() => xtreamClient.GetSeriesCategoryAsync(creds, cancellationToken));
            steps.AddRange(config.Series.Keys.Select(id => (Func<Task>)(() => xtreamClient.GetSeriesByCategoryAsync(creds, id, cancellationToken))));
        }

        int failures = 0;
        using (xtreamClient.StartCatalogRefresh())
        {
            for (int i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await steps[i]().ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    failures++;
                    logger.LogWarning(ex, "Prefetch step {Step} of {Count} failed", i + 1, steps.Count);
                    if (ResilientFetcher.IsRefusal(ex) || (ex.InnerException is HttpRequestException inner && ResilientFetcher.IsRefusal(inner)))
                    {
                        logger.LogWarning("The provider refuses our requests, prefetch stopped");
                        break;
                    }
                }

                progress.Report(100.0 * (i + 1) / steps.Count);
            }
        }

        logger.LogInformation("Xtream catalog prefetched: {Count} lists, {Failures} failed", steps.Count, failures);
    }
}
