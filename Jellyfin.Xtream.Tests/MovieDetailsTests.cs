using Jellyfin.Xtream.Providers;
using Jellyfin.Xtream.Service;
using MediaBrowser.Controller.Providers;
using Xunit;

namespace Jellyfin.Xtream.Tests;

public class MovieDetailsTests
{
    [Theory]
    [InlineData(MetadataRefreshMode.Default, false)]
    [InlineData(MetadataRefreshMode.ValidationOnly, false)]
    [InlineData(MetadataRefreshMode.None, false)]
    [InlineData(MetadataRefreshMode.FullRefresh, true)]
    public void OnlyRefreshByHandQueriesProvider(MetadataRefreshMode mode, bool expected)
    {
        Assert.Equal(expected, XtreamVodProvider.QueriesProvider(mode));
    }

    [Theory]
    [InlineData("1395140", "1395140")]
    [InlineData("  ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("0", null)]
    [InlineData("-5", null)]
    [InlineData("tt0212720", null)]
    public void ParsesTmdbIdFromList(string? raw, string? expected)
    {
        Assert.Equal(expected, StreamService.ParseTmdbId(raw));
    }
}
