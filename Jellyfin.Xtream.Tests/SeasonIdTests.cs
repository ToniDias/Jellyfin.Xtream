using Jellyfin.Xtream.Service;

namespace Jellyfin.Xtream.Tests;

public class SeasonIdTests
{
    [Fact]
    public void ReadsSeriesAndSeasonFromASeasonFolder()
    {
        string id = StreamService.ToGuid(StreamService.SeasonPrefix, 885, 8389, 2).ToString();
        Assert.True(StreamService.TryParseSeason(id, out int seriesId, out int seasonId));
        Assert.Equal(8389, seriesId);
        Assert.Equal(2, seasonId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a guid")]
    public void RejectsWhatIsNotAnIdentifier(string? id)
    {
        Assert.False(StreamService.TryParseSeason(id, out _, out _));
    }

    [Fact]
    public void RejectsOtherFolders()
    {
        string series = StreamService.ToGuid(StreamService.SeriesPrefix, 885, 8389, 0).ToString();
        Assert.False(StreamService.TryParseSeason(series, out _, out _));
        Assert.Equal(StreamService.SeriesGroupPrefix, StreamService.FromGuidPrefix(StreamService.ToGuid(StreamService.SeriesGroupPrefix, 1, 0, 0)));
    }
}
