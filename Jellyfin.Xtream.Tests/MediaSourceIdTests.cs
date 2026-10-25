using Jellyfin.Xtream.Service;

namespace Jellyfin.Xtream.Tests;

public class MediaSourceIdTests
{
    // Jellyfin 12 drops media sources whose identifier is a GUID unknown to the library.
    [Theory]
    [InlineData(StreamType.Series, 123)]
    [InlineData(StreamType.Vod, 456)]
    [InlineData(StreamType.Live, 789)]
    [InlineData(StreamType.CatchUp, 1)]
    public void IsNotAGuid(StreamType type, int id)
    {
        Assert.False(Guid.TryParse(StreamService.MediaSourceId(type, id), out _));
    }

    [Fact]
    public void IsStableAndDistinctPerTypeAndStream()
    {
        Assert.Equal(StreamService.MediaSourceId(StreamType.Series, 5), StreamService.MediaSourceId(StreamType.Series, 5));
        Assert.NotEqual(StreamService.MediaSourceId(StreamType.Series, 5), StreamService.MediaSourceId(StreamType.Vod, 5));
        Assert.NotEqual(StreamService.MediaSourceId(StreamType.Series, 5), StreamService.MediaSourceId(StreamType.Series, 6));
    }
}
