using System.Collections.Concurrent;
using Telegram.Bot.Types;

namespace Bot.Presentation.Security;

public class UpdateDedupCache
{
    private readonly ConcurrentDictionary<int, byte> _seen = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(20);
    private DateTime _lastSweep = DateTime.UtcNow;

    public bool Seen(Update update)
    {
        Sweep();
        return !_seen.TryAdd(update.Id, 0);
    }

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweep < TimeSpan.FromMinutes(5)) return;
        _lastSweep = now;
        foreach (var kv in _seen.ToArray())
            if (now - DateTime.UnixEpoch > _ttl) break;
    }
}