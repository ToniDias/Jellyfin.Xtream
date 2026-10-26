using Jellyfin.Xtream.Service;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Xtream.Tests;

public class EpgGuardTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 1, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AChannelWithoutGuideIsNotQueried()
    {
        EpgGuard guard = new(_time);
        guard.SetHasGuide(1, hasGuide: false);
        guard.SetHasGuide(2, hasGuide: true);

        Assert.False(guard.ShouldQuery(1));
        Assert.True(guard.ShouldQuery(2));
        Assert.True(guard.ShouldQuery(3));
    }

    [Fact]
    public void FiveFailuresInARowPauseTheGuide()
    {
        EpgGuard guard = new(_time);
        for (int i = 1; i < EpgGuard.MaxConsecutiveFailures; i++)
        {
            Assert.False(guard.ReportFailure());
        }

        Assert.True(guard.ReportFailure());
        Assert.True(guard.IsPaused);
        Assert.False(guard.ShouldQuery(2));
    }

    [Fact]
    public void ASuccessResetsTheFailureCount()
    {
        EpgGuard guard = new(_time);
        for (int i = 1; i < EpgGuard.MaxConsecutiveFailures; i++)
        {
            guard.ReportFailure();
        }

        guard.ReportSuccess();

        Assert.False(guard.ReportFailure());
        Assert.False(guard.IsPaused);
    }

    [Fact]
    public void ThePauseEnds()
    {
        EpgGuard guard = new(_time);
        for (int i = 0; i < EpgGuard.MaxConsecutiveFailures; i++)
        {
            guard.ReportFailure();
        }

        _time.Advance(EpgGuard.Pause + TimeSpan.FromSeconds(1));

        Assert.False(guard.IsPaused);
        Assert.True(guard.ShouldQuery(2));
    }
}
