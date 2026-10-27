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
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client;
using Jellyfin.Xtream.Configuration;
using Jellyfin.Xtream.Service;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Jellyfin.Xtream.Tasks;

/// <summary>
/// Opens, during the night, the movie and series categories one at a time, with a random pause between two,
/// exactly as a user browsing them would. Jellyfin then knows every movie and series of the selection, and its
/// search finds them. Each category costs at most one request to the provider, none when its list is cached;
/// series details, episodes and guide are never asked.
/// </summary>
/// <param name="channelManager">Instance of the <see cref="IChannelManager"/> interface.</param>
/// <param name="xtreamClient">Instance of the <see cref="IXtreamClient"/> interface.</param>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
public class IndexCatalogTask(IChannelManager channelManager, IXtreamClient xtreamClient, ILogger<IndexCatalogTask> logger) : IScheduledTask
{
    /// <inheritdoc />
    public string Name => "Index Xtream catalog";

    /// <inheritdoc />
    public string Key => "XtreamIndexCatalog";

    /// <inheritdoc />
    public string Description => "Opens the selected Xtream categories one at a time during the night, so that the search finds their movies and series.";

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
                TimeOfDayTicks = TimeSpan.FromHours(IndexSchedule.WindowStartHour).Ticks,
                MaxRuntimeTicks = IndexSchedule.ManualRunLength.Ticks,
            },
        ];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        Plugin plugin = Plugin.Instance;
        PluginConfiguration config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            return;
        }

        DateTimeOffset deadline = IndexSchedule.Deadline(DateTimeOffset.Now);
        Dictionary<string, (Guid Channel, BaseItem Folder)> categories = await ListCategories(config, cancellationToken).ConfigureAwait(false);
        Dictionary<string, DateTime> state = LoadState();
        IReadOnlyList<string> order = IndexSchedule.Order(categories.Keys, state);

        int done = 0;
        Random random = new();
        foreach (string key in order)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (done > 0)
            {
                TimeSpan pause = IndexSchedule.NextPause(random, config.IndexMinPauseSeconds, config.IndexMaxPauseSeconds);
                if (DateTimeOffset.Now + pause >= deadline)
                {
                    break;
                }

                await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
            }

            if (xtreamClient.IsPaused)
            {
                logger.LogWarning("The provider refuses our requests, indexing stopped");
                break;
            }

            (Guid channel, BaseItem folder) = categories[key];
            try
            {
                await Open(channel, folder.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Indexing of category {Category} failed", folder.Name);
                if (xtreamClient.IsPaused)
                {
                    logger.LogWarning("The provider refuses our requests, indexing stopped");
                    break;
                }

                continue;
            }

            state[key] = DateTime.UtcNow;
            SaveState(state);
            done++;
            progress.Report(100.0 * done / order.Count);
        }

        logger.LogInformation("Xtream catalog indexed: {Done} of {Count} categories", done, order.Count);
    }

    private static string? StatePath()
    {
        try
        {
            return Path.Combine(Plugin.Instance.DataFolderPath, "index-state.json");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Dictionary<string, DateTime> LoadState()
    {
        string? path = StatePath();
        try
        {
            if (path is not null && File.Exists(path))
            {
                return JsonConvert.DeserializeObject<Dictionary<string, DateTime>>(File.ReadAllText(path)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A lost state only means indexing starts again from the first category.
        }

        return [];
    }

    private static void SaveState(Dictionary<string, DateTime> state)
    {
        string? path = StatePath();
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same: the next run simply indexes some categories again.
        }
    }

    private static bool IsGroup(BaseItem item) =>
        Guid.TryParse(item.ExternalId, out Guid id)
        && StreamService.FromGuidPrefix(id) is StreamService.VodGroupPrefix or StreamService.SeriesGroupPrefix;

    private async Task<IReadOnlyList<BaseItem>> Open(Guid channel, Guid parent, CancellationToken cancellationToken)
    {
        InternalItemsQuery query = new()
        {
            ChannelIds = [channel],
            ParentId = parent,
        };
        return (await channelManager.GetChannelItemsInternal(query, new Progress<double>(), cancellationToken).ConfigureAwait(false)).Items;
    }

    private async Task<Dictionary<string, (Guid Channel, BaseItem Folder)>> ListCategories(PluginConfiguration config, CancellationToken cancellationToken)
    {
        HashSet<string> names = [];
        if (config.IsVodVisible && config.Vod.Count > 0)
        {
            names.Add(Plugin.ChannelName(c => c.VodChannelName, VodChannel.DefaultName));
        }

        if (config.IsSeriesVisible && config.Series.Count > 0)
        {
            names.Add(Plugin.ChannelName(c => c.SeriesChannelName, SeriesChannel.DefaultName));
        }

        Dictionary<string, (Guid Channel, BaseItem Folder)> categories = [];
        foreach (Channel channel in (await channelManager.GetChannelsInternalAsync(new ChannelQuery()).ConfigureAwait(false)).Items)
        {
            if (!names.Contains(channel.Name))
            {
                continue;
            }

            foreach (BaseItem top in await Open(channel.Id, Guid.Empty, cancellationToken).ConfigureAwait(false))
            {
                IEnumerable<BaseItem> folders = IsGroup(top)
                    ? await Open(channel.Id, top.Id, cancellationToken).ConfigureAwait(false)
                    : [top];
                foreach (BaseItem folder in folders)
                {
                    categories[$"{channel.Name}|{folder.ExternalId}"] = (channel.Id, folder);
                }
            }
        }

        return categories;
    }
}
