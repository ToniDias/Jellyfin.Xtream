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
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Xtream.Client;

/// <summary>
/// Keeps the last usable response of each API query, in memory and, when a directory is available, on disk.
/// The disk copy survives server restarts, so a flaky provider never leaves the library empty.
/// </summary>
/// <param name="directory">Returns the cache directory, or <c>null</c> to keep responses in memory only.</param>
/// <param name="time">The time provider.</param>
public sealed class ApiResponseCache(Func<string?> directory, TimeProvider time)
{
    private readonly ConcurrentDictionary<string, CachedResponse> _memory = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the cached response for the key, from memory or disk.
    /// </summary>
    /// <param name="key">The query key.</param>
    /// <param name="response">The cached response, if any.</param>
    /// <returns><c>true</c> when a response is cached.</returns>
    public bool TryGet(string key, out CachedResponse response)
    {
        if (_memory.TryGetValue(key, out response))
        {
            return true;
        }

        string? path = PathFor(key);
        if (path is null)
        {
            return false;
        }

        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string json = File.ReadAllText(path);
            response = new CachedResponse(json, new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
            _memory[key] = response;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stores a usable response for the key.
    /// </summary>
    /// <param name="key">The query key.</param>
    /// <param name="json">The raw JSON response.</param>
    public void Store(string key, string json)
    {
        DateTimeOffset now = time.GetUtcNow();
        _memory[key] = new CachedResponse(json, now);

        string? path = PathFor(key);
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            File.SetLastWriteTimeUtc(temporary, now.UtcDateTime);
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
            // The memory copy is enough to serve this session.
        }
        catch (UnauthorizedAccessException)
        {
            // The memory copy is enough to serve this session.
        }
    }

    private string? PathFor(string key)
    {
        string? root = directory();
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(root, name + ".json");
    }
}
