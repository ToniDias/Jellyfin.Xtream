using Jellyfin.Xtream.Service;

namespace Jellyfin.Xtream.Tests;

public class ParseNameTests
{
    // Some providers send live channels without a name: one of them used to abort the whole channel list.
    [Fact]
    public void AMissingNameGivesAnEmptyTitle()
    {
        ParsedName parsed = StreamService.ParseName(null);

        Assert.Equal(string.Empty, parsed.Title);
        Assert.Empty(parsed.Tags);
    }

    [Theory]
    [InlineData("FR - Blue Box (2024) (JP)", "Blue Box (2024) (JP)", "FR")]
    [InlineData("EN - The Game (1997) SEAN PENN", "The Game (1997) SEAN PENN", "EN")]
    public void ALanguagePrefixBecomesATag(string name, string title, string tag)
    {
        ParsedName parsed = StreamService.ParseName(name);

        Assert.Equal(title, parsed.Title);
        Assert.Equal([tag], parsed.Tags);
    }

    [Theory]
    [InlineData("WWE - Raw")]
    [InlineData("A - Z")]
    public void OtherDashesAreKept(string name)
    {
        Assert.Equal(name, StreamService.ParseName(name).Title);
    }

    [Fact]
    public void TagsAreSeparatedFromTheTitle()
    {
        ParsedName parsed = StreamService.ParseName("|FR| ACTION");

        Assert.Equal("ACTION", parsed.Title);
        Assert.Equal(["FR"], parsed.Tags);
    }
}
