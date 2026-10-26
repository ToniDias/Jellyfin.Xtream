using Jellyfin.Xtream.Client.Models;
using Jellyfin.Xtream.Service;

namespace Jellyfin.Xtream.Tests;

public class EpgCoverageTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 22, 0, 0, DateTimeKind.Utc);

    private static EpgListings Until(DateTime end) => new()
    {
        Listings = [new EpgInfo { Start = end.AddHours(-1), End = end }],
    };

    [Fact]
    public void NothingSavedMustBeAsked()
    {
        Assert.True(EpgCoverage.NeedsRefresh(null, Now));
        Assert.True(EpgCoverage.NeedsRefresh(new EpgListings(), Now));
    }

    [Fact]
    public void AGuideThatLastsSeveralDaysIsKept()
    {
        Assert.False(EpgCoverage.NeedsRefresh(Until(Now.AddDays(3)), Now));
    }

    [Fact]
    public void AGuideEndingWithinADayIsAskedAgain()
    {
        Assert.True(EpgCoverage.NeedsRefresh(Until(Now.AddHours(20)), Now));
    }
}
