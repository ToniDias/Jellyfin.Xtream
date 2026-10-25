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
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Xtream.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

#pragma warning disable CS1591
namespace Jellyfin.Xtream.Client;

/// <summary>
/// The Xtream API client implementation.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="XtreamClient"/> class.
/// </remarks>
/// <param name="client">The HTTP client used.</param>
/// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
public class XtreamClient(HttpClient client, ILogger<XtreamClient> logger) : IDisposable, IXtreamClient
{
    private readonly JsonSerializerSettings _serializerSettings = new()
    {
        Error = NullableEventHandler(logger),
    };

    private readonly ResilientFetcher _fetcher = new(new ApiResponseCache(CacheDirectory, TimeProvider.System), TimeProvider.System, logger);

    /// <inheritdoc />
    public int DegradedGeneration => _fetcher.DegradedGeneration;

    public void UpdateUserAgent()
    {
        client.DefaultRequestHeaders.UserAgent.Clear();
        if (string.IsNullOrWhiteSpace(Plugin.Instance.Configuration.UserAgent))
        {
            ProductHeaderValue header = new ProductHeaderValue("Jellyfin.Xtream", Assembly.GetExecutingAssembly().GetName().Version?.ToString());
            ProductInfoHeaderValue userAgent = new ProductInfoHeaderValue(header);
            client.DefaultRequestHeaders.UserAgent.Add(userAgent);
        }
        else
        {
            // Trust the correctness of the configuration.
            client.DefaultRequestHeaders.Add("User-Agent", Plugin.Instance.Configuration.UserAgent);
        }
    }

    /// <summary>
    /// Ignores error events if the target property is nullable.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    /// <returns>An event handler using the given logger.</returns>
    public static EventHandler<ErrorEventArgs> NullableEventHandler(ILogger<XtreamClient> logger)
    {
        return (object? sender, ErrorEventArgs args) =>
        {
            if (args.ErrorContext.OriginalObject?.GetType() is Type type && args.ErrorContext.Member is string jsonName)
            {
                PropertyInfo? property = type.GetProperties().FirstOrDefault((p) =>
                {
                    CustomAttributeData? attribute = p.CustomAttributes.FirstOrDefault(a => a.AttributeType == typeof(JsonPropertyAttribute));
                    if (attribute == null)
                    {
                        return false;
                    }

                    if (attribute.ConstructorArguments.Count > 0)
                    {
                        // Attribute contains a `propertyName`.
                        string? value = attribute.ConstructorArguments.First().Value as string;
                        return jsonName.Equals(value, StringComparison.Ordinal);
                    }
                    else
                    {
                        // Attribute does not contain a `propertyName`, compare with the name of the property itself.
                        return jsonName.Equals(p.Name, StringComparison.Ordinal);
                    }
                });

                if (property != null && Nullable.GetUnderlyingType(property.PropertyType) != null)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Property `{0}` (`{1}` in JSON) is nullable, ignoring parsing error!", property.Name, jsonName);
                        logger.LogDebug("Stack trace: {0}", new System.Diagnostics.StackTrace());
                    }

                    args.ErrorContext.Handled = true;
                }
            }
        };
    }

    private static string? CacheDirectory()
    {
        try
        {
            return System.IO.Path.Combine(Plugin.Instance.DataFolderPath, "api-cache");
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private Task<T> QueryApi<T>(
        ConnectionInfo connectionInfo,
        string query,
        Func<T, bool> isUsable,
        CancellationToken cancellationToken,
        bool useCache = true,
        int maxAttempts = ResilientFetcher.DefaultMaxAttempts)
    {
        Uri uri = new Uri($"{connectionInfo.BaseUrl}/player_api.php?username={connectionInfo.UserName}&password={connectionInfo.Password}{query}");

        // The key identifies the account and the query, without the password.
        string key = $"{connectionInfo.BaseUrl}|{connectionInfo.UserName}|{query}";
        return _fetcher.FetchAsync(
            key,
            ct => client.GetStringAsync(uri, ct),
            json => JsonConvert.DeserializeObject<T>(json, _serializerSettings)!,
            isUsable,
            useCache,
            maxAttempts,
            cancellationToken);
    }

    private static bool HasItems<TItem>(List<TItem>? items) => items is { Count: > 0 };

    public Task<PlayerApi> GetUserAndServerInfoAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
        QueryApi<PlayerApi>(connectionInfo, string.Empty, info => info is not null, cancellationToken, useCache: false, maxAttempts: 3);

    public Task<List<Series>> GetSeriesByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken) =>
        QueryApi<List<Series>>(connectionInfo, $"&action=get_series&category_id={categoryId}", HasItems, cancellationToken);

    public Task<SeriesStreamInfo> GetSeriesStreamsBySeriesAsync(ConnectionInfo connectionInfo, int seriesId, CancellationToken cancellationToken) =>
        QueryApi<SeriesStreamInfo>(connectionInfo, $"&action=get_series_info&series_id={seriesId}", series => series?.Episodes is { Count: > 0 }, cancellationToken);

    public Task<List<StreamInfo>> GetVodStreamsByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken) =>
        QueryApi<List<StreamInfo>>(connectionInfo, $"&action=get_vod_streams&category_id={categoryId}", HasItems, cancellationToken);

    public Task<VodStreamInfo> GetVodInfoAsync(ConnectionInfo connectionInfo, int streamId, CancellationToken cancellationToken) =>
        QueryApi<VodStreamInfo>(connectionInfo, $"&action=get_vod_info&vod_id={streamId}", vod => vod?.Info is not null || vod?.MovieData is not null, cancellationToken);

    public Task<List<StreamInfo>> GetLiveStreamsAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
        QueryApi<List<StreamInfo>>(connectionInfo, "&action=get_live_streams", HasItems, cancellationToken);

    public Task<List<StreamInfo>> GetLiveStreamsByCategoryAsync(ConnectionInfo connectionInfo, int categoryId, CancellationToken cancellationToken) =>
        QueryApi<List<StreamInfo>>(connectionInfo, $"&action=get_live_streams&category_id={categoryId}", HasItems, cancellationToken);

    public Task<List<Category>> GetSeriesCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
        QueryApi<List<Category>>(connectionInfo, "&action=get_series_categories", HasItems, cancellationToken);

    public Task<List<Category>> GetVodCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
        QueryApi<List<Category>>(connectionInfo, "&action=get_vod_categories", HasItems, cancellationToken);

    public Task<List<Category>> GetLiveCategoryAsync(ConnectionInfo connectionInfo, CancellationToken cancellationToken) =>
        QueryApi<List<Category>>(connectionInfo, "&action=get_live_categories", HasItems, cancellationToken);

    // The guide can legitimately be empty and is cached by the Live TV service: one attempt, no cache.
    public Task<EpgListings> GetEpgInfoAsync(ConnectionInfo connectionInfo, int streamId, CancellationToken cancellationToken) =>
        QueryApi<EpgListings>(connectionInfo, $"&action=get_simple_data_table&stream_id={streamId}", _ => true, cancellationToken, useCache: false, maxAttempts: 1);

    /// <summary>
    /// Dispose the HTTP client.
    /// </summary>
    /// <param name="b">Unused.</param>
    protected virtual void Dispose(bool b)
    {
        client?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
