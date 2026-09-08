using System.Collections.Concurrent;

namespace SysSuite.Watchdog.Ipc;

public sealed record LeaseInfo(string LeaseId, string Owner, DateTimeOffset HeartbeatUtc);

public sealed class LeaseRegistry
{
    private readonly ConcurrentDictionary<string, LeaseInfo> leases = new();

    public LeaseInfo StartLease(string owner)
    {
        var lease = new LeaseInfo(Guid.NewGuid().ToString("N"), owner, DateTimeOffset.UtcNow);
        leases[lease.LeaseId] = lease;
        return lease;
    }

    public bool Renew(string leaseId)
    {
        return leases.TryGetValue(leaseId, out var lease) &&
               leases.TryUpdate(leaseId, lease with { HeartbeatUtc = DateTimeOffset.UtcNow }, lease);
    }

    public bool Release(string leaseId) => leases.TryRemove(leaseId, out _);

    public IReadOnlyList<LeaseInfo> GetActive(TimeSpan maximumIdleTime)
    {
        var threshold = DateTimeOffset.UtcNow - maximumIdleTime;
        return leases.Values.Where(lease => lease.HeartbeatUtc >= threshold).ToArray();
    }
}
