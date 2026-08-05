using Spindle.Abstractions.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spindle.Runtime;

/// <summary>
/// This is a small concurrency helper that can help with global locking of a flow to prevent i.e. database deadlocks.
/// </summary>
internal class ConcurrencyHelper
{

    private static readonly Dictionary<FlowInstanceId, SemaphoreSlim> _semaphores = new();
    private static readonly Dictionary<FlowInstanceId, DateTime> _lastRelease = new();

    /// <summary>
    /// Asynchronously obtains the lock
    /// </summary>
    /// <param name="flowInstanceId">The instance to aquire the lock for</param>
    /// <returns></returns>
    public static async Task AquireLock(FlowInstanceId flowInstanceId)
    {
        SemaphoreSlim? semaphore;
        lock (_semaphores)
        {
            if (!_semaphores.TryGetValue(flowInstanceId, out semaphore))
            {
                semaphore = new SemaphoreSlim(1, 1);
                _semaphores.Add(flowInstanceId, semaphore);
            }
        }

        await semaphore.WaitAsync();
    }

    public static void ReleaseLock(FlowInstanceId flowInstanceId)
    {
        lock(_semaphores)
        {
            if (!_semaphores.TryGetValue(flowInstanceId, out SemaphoreSlim? semaphore)) return;
            semaphore.Release();
        }
        lock (_lastRelease)
        {
            _lastRelease[flowInstanceId] = DateTime.Now;
        }
    }

    internal static void CleanupLocks()
    {
        var olderThan = DateTime.Now.AddSeconds(-10);
        List<FlowInstanceId> toRemove;
        lock (_lastRelease)
        {
            toRemove = _lastRelease.Where(x => x.Value < olderThan).Select(x => x.Key).ToList();
        }
        if (toRemove.Count == 0) return;
        
        lock (_semaphores) 
        {
            foreach (var item in toRemove)
            {
                var sem = _semaphores[item];
                if (sem.CurrentCount == 0) continue; // It's currently aquired

                _semaphores.Remove(item);
                _lastRelease.Remove(item);
            }
        }
    }

}
