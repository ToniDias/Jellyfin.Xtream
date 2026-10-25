using Jellyfin.Xtream.Service;

namespace Jellyfin.Xtream.Tests;

public class MediaSourceIdTests
{
    // Jellyfin 12 drops media sources whose identifier is a GUID unknown to the library, and requires a GUID
    // when streaming. An empty identifier lets Jellyfin use the identifier of the item, which satisfies both.
    [Theory]
    [InlineData(StreamType.Series, 123)]
    [InlineData(StreamType.Vod, 456)]
    [InlineData(StreamType.CatchUp, 1)]
    public void ChannelMediaLeaveTheIdentifierToJellyfin(StreamType type, int id)
    {
        Assert.Equal(string.Empty, StreamService.MediaSourceId(type, id));
    }

    [Fact]
    public void LiveTvKeepsAStableGuid()
    {
        string id = StreamService.MediaSourceId(StreamType.Live, 789);

        Assert.True(Guid.TryParse(id, out _));
        Assert.Equal(id, StreamService.MediaSourceId(StreamType.Live, 789));
        Assert.NotEqual(id, StreamService.MediaSourceId(StreamType.Live, 790));
    }

    [Fact]
    public void TheMediaSourceFormatDateIsUtc()
    {
        Assert.Equal(DateTimeKind.Utc, StreamService.MediaSourceFormatDate.Kind);
    }
}
