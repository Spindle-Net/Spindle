using Spindle.Abstractions.Core;
using Spindle.Persistence.Leases;

namespace Spindle.Persistence.InMemory.Stores;

public sealed class InMemoryLeaseStore : ILeaseStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(FlowInstanceId FlowInstanceId, StepId StepId), StepLeaseRecord> _leases = [];

    public ValueTask<bool> TryAcquireStepLeaseAsync(
        StepLeaseRecord lease,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (lease.FlowInstanceId, lease.StepId);
            if (_leases.TryGetValue(key, out var existing) &&
                existing.ExpiresAt > lease.AcquiredAt &&
                !string.Equals(existing.Owner, lease.Owner, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            _leases[key] = lease;
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask ReleaseStepLeaseAsync(
        FlowInstanceId flowInstanceId,
        StepId stepId,
        string owner,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var key = (flowInstanceId, stepId);
            if (_leases.TryGetValue(key, out var existing) &&
                string.Equals(existing.Owner, owner, StringComparison.Ordinal))
            {
                _leases.Remove(key);
            }
        }

        return ValueTask.CompletedTask;
    }
}
