using Jellyfin.Xtream.Client;
using Microsoft.Extensions.Time.Testing;

namespace Jellyfin.Xtream.Tests;

public class RequestPacerTests
{
    [Fact]
    public async Task RequestsAreSpacedByTheInterval()
    {
        FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 26, 22, 0, 0, TimeSpan.Zero));
        List<TimeSpan> waits = [];
        using RequestPacer pacer = new(TimeSpan.FromSeconds(1), time, (wait, _) =>
        {
            waits.Add(wait);
            time.Advance(wait);
            return Task.CompletedTask;
        });

        await pacer.WaitAsync(default);
        await pacer.WaitAsync(default);
        time.Advance(TimeSpan.FromMilliseconds(300));
        await pacer.WaitAsync(default);
        time.Advance(TimeSpan.FromSeconds(5));
        await pacer.WaitAsync(default);

        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(700)], waits);
    }
}
