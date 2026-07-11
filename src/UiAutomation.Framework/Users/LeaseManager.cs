namespace UiAutomation.Framework.Users;

/// <summary>
/// A lease-backed handle to a test user. Non-disposable from a test's perspective — the
/// framework (the scenario scope) is the sole lifecycle owner and releases it after
/// application cleanup (Section 9.4 / ADR-008). Release is idempotent and carries an outcome.
/// </summary>
public sealed class TestUserLease
{
    private readonly Func<ActorReleaseOutcome, ValueTask> _release;
    private int _released;

    internal TestUserLease(TestUser user, long fencingToken, Func<ActorReleaseOutcome, ValueTask> release)
    {
        User = user;
        FencingToken = fencingToken;
        _release = release;
    }

    public TestUser User { get; }

    /// <summary>Monotonic ownership token (Section 15.4). A stale owner cannot release a reassigned lease.</summary>
    public long FencingToken { get; }

    public async ValueTask ReleaseAsync(ActorReleaseOutcome outcome)
    {
        // Idempotent: repeated release is safe and does not double-return capacity (Section 15.6).
        if (Interlocked.Exchange(ref _released, 1) == 1)
        {
            return;
        }

        await _release(outcome).ConfigureAwait(false);
    }
}

/// <summary>User leasing surface (Section 15.2). Multi-user set acquisition is atomic (all or none).</summary>
public interface IUserLeaseManager
{
    ValueTask<TestUserLease> AcquireAsync(UserLeaseRequest request, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TestUserLease>> AcquireSetAsync(
        IReadOnlyList<UserLeaseRequest> requests,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Phase 1 in-memory lease manager (Section 15.4): per-process, single application-hitting
/// test module. Honors per-user capacity and concurrency mode; a non-Healthy release
/// quarantines the account (its capacity is not returned to the pool for the run).
/// Distributed leasing (fencing across processes, SQL/service store) is Phase 3.
/// </summary>
public sealed class InMemoryUserLeaseManager : IUserLeaseManager
{
    private readonly IReadOnlyList<TestUser> _users;
    private readonly Dictionary<string, SemaphoreSlim> _capacity;
    private readonly TimeProvider _timeProvider;
    private long _fencing;

    public InMemoryUserLeaseManager(IReadOnlyList<TestUser> users)
        : this(users, TimeProvider.System)
    {
    }

    public InMemoryUserLeaseManager(IReadOnlyList<TestUser> users, TimeProvider timeProvider)
    {
        _users = users;
        _timeProvider = timeProvider;
        _capacity = users.ToDictionary(u => u.Key, u => new SemaphoreSlim(u.EffectiveCapacity, u.EffectiveCapacity));
    }

    public async ValueTask<TestUserLease> AcquireAsync(UserLeaseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.HasCriteria)
        {
            throw new ArgumentException("A user lease request must specify at least one criterion.", nameof(request));
        }

        var eligible = _users.Where(u => Matches(u, request)).ToList();
        if (eligible.Count == 0)
        {
            throw new LeaseCapacityException($"No configured user matches the lease request ({Describe(request)}).");
        }

        using var timeoutCts = new CancellationTokenSource(request.AcquisitionTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Fast path: any eligible user with immediately-available capacity.
        foreach (var user in eligible)
        {
            if (_capacity[user.Key].Wait(0, CancellationToken.None))
            {
                return Lease(user);
            }
        }

        // Otherwise wait on the first eligible user until the acquisition timeout.
        var target = eligible[0];
        try
        {
            await _capacity[target.Key].WaitAsync(linked.Token).ConfigureAwait(false);
            return Lease(target);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new LeaseCapacityException(
                $"Timed out after {request.AcquisitionTimeout} waiting for a user matching ({Describe(request)}).");
        }
    }

    public async ValueTask<IReadOnlyList<TestUserLease>> AcquireSetAsync(
        IReadOnlyList<UserLeaseRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var acquired = new List<TestUserLease>(requests.Count);
        try
        {
            foreach (var request in requests)
            {
                acquired.Add(await AcquireAsync(request, cancellationToken).ConfigureAwait(false));
            }

            return acquired;
        }
        catch
        {
            // Atomic: acquire the complete set or none. Roll back anything already taken.
            foreach (var lease in acquired)
            {
                await lease.ReleaseAsync(ActorReleaseOutcome.Healthy).ConfigureAwait(false);
            }

            throw;
        }
    }

    private TestUserLease Lease(TestUser user)
    {
        var token = Interlocked.Increment(ref _fencing);
        return new TestUserLease(user, token, outcome =>
        {
            // Only a healthy release returns capacity to the pool; anything else quarantines
            // the account (Section 15.6) — capacity is intentionally not returned.
            if (outcome == ActorReleaseOutcome.Healthy)
            {
                _capacity[user.Key].Release();
            }

            return ValueTask.CompletedTask;
        });
    }

    private static bool Matches(TestUser user, UserLeaseRequest request)
    {
        if (!user.IsActive)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(request.PersonaKey) &&
            !string.Equals(user.Key, request.PersonaKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.Role is { } role && user.Role != role)
        {
            return false;
        }

        if (!request.RequiredPermissions.All(user.Permissions.Contains))
        {
            return false;
        }

        if (request.ForbiddenPermissions.Any(user.Permissions.Contains))
        {
            return false;
        }

        foreach (var (key, value) in request.RequiredMetadata)
        {
            if (!user.Metadata.TryGetValue(key, out var actual) ||
                !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(UserLeaseRequest request)
    {
        var parts = new List<string>();
        if (request.Role is { } role)
        {
            parts.Add($"role={role}");
        }

        if (!string.IsNullOrEmpty(request.PersonaKey))
        {
            parts.Add($"persona={request.PersonaKey}");
        }

        if (request.RequiredPermissions.Count > 0)
        {
            parts.Add($"requires=[{string.Join(",", request.RequiredPermissions)}]");
        }

        if (request.ForbiddenPermissions.Count > 0)
        {
            parts.Add($"forbids=[{string.Join(",", request.ForbiddenPermissions)}]");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "no criteria";
    }
}
