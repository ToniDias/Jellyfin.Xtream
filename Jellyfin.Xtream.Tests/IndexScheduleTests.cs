using Jellyfin.Xtream.Service;

namespace Jellyfin.Xtream.Tests;

public class IndexScheduleTests
{
    private static readonly TimeSpan Zurich = TimeSpan.FromHours(1);

    [Fact]
    public void ANightRunStopsAtTheEndOfTheWindow()
    {
        DateTimeOffset start = new(2026, 10, 28, 1, 0, 0, Zurich);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 6, 0, 0, Zurich), IndexSchedule.Deadline(start));
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 6, 0, 0, Zurich), IndexSchedule.Deadline(start.AddHours(4.5)));
    }

    [Fact]
    public void ARunStartedByHandLastsFiveHours()
    {
        DateTimeOffset start = new(2026, 10, 27, 14, 30, 0, Zurich);
        Assert.Equal(start.AddHours(5), IndexSchedule.Deadline(start));
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 5, 30, 0, Zurich), IndexSchedule.Deadline(start.AddHours(10)));
    }

    [Fact]
    public void PausesStayWithinTheBounds()
    {
        Random random = new(42);
        for (int i = 0; i < 1000; i++)
        {
            TimeSpan pause = IndexSchedule.NextPause(random, 180, 600);
            Assert.InRange(pause.TotalSeconds, 180, 600);
        }
    }

    [Fact]
    public void PausesAreNotAllTheSame()
    {
        Random random = new(7);
        HashSet<int> seconds = [];
        for (int i = 0; i < 50; i++)
        {
            seconds.Add((int)IndexSchedule.NextPause(random, 180, 600).TotalSeconds);
        }

        Assert.True(seconds.Count > 20);
    }

    [Theory]
    [InlineData(600, 180)]
    [InlineData(-5, 10)]
    [InlineData(30, 30)]
    public void SwappedOrOddBoundsAreTolerated(int min, int max)
    {
        TimeSpan pause = IndexSchedule.NextPause(new Random(1), min, max);
        Assert.InRange(pause.TotalSeconds, Math.Max(0, Math.Min(min, max)), Math.Max(min, max));
    }

    [Fact]
    public void NeverIndexedCategoriesComeFirstThenTheOldest()
    {
        Dictionary<string, DateTime> last = new()
        {
            ["a"] = new DateTime(2026, 10, 27, 2, 0, 0, DateTimeKind.Utc),
            ["b"] = new DateTime(2026, 10, 26, 2, 0, 0, DateTimeKind.Utc),
            ["d"] = new DateTime(2026, 10, 27, 3, 0, 0, DateTimeKind.Utc),
        };
        Assert.Equal(["c", "e", "b", "a", "d"], IndexSchedule.Order(["a", "b", "c", "d", "e", "c"], last));
    }
}
