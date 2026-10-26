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
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Xtream.Client;

/// <summary>
/// Spaces requests of one kind: each caller waits until at least <paramref name="interval"/> has passed
/// since the previous one. Turns bursts (hundreds of movie details, a whole guide) into a steady trickle
/// that providers do not mistake for abuse.
/// </summary>
/// <param name="interval">The minimal time between two requests.</param>
/// <param name="time">The time provider.</param>
/// <param name="delay">Waits; replaced in tests.</param>
public sealed class RequestPacer(TimeSpan interval, TimeProvider time, Func<TimeSpan, CancellationToken, Task>? delay = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>
    /// Waits for the turn of the caller.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the caller may send its request.</returns>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan wait = _last + interval - time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _last = time.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
