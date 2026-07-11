using UiAutomation.Framework.Secrets;
using UiAutomation.Framework.Users;

namespace UiAutomation.Framework.Composition;

/// <summary>
/// Declares the actors a scenario needs. Actors are declared <em>before</em> the body and
/// acquired before any scenario graph is composed (Section 9.2 / ADR-004). Phase 1 supports a
/// single slot; multi-slot is additive (Section 20.3).
/// </summary>
public sealed class ActorPlan
{
    public const string DefaultSlot = "default";

    public ActorPlan(UserLeaseRequest actor)
        : this(new Dictionary<string, UserLeaseRequest> { [DefaultSlot] = actor })
    {
    }

    public ActorPlan(IReadOnlyDictionary<string, UserLeaseRequest> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count == 0)
        {
            throw new ArgumentException("An actor plan must declare at least one slot.", nameof(slots));
        }

        Slots = slots;
    }

    public IReadOnlyDictionary<string, UserLeaseRequest> Slots { get; }

    public bool IsSingle => Slots.Count == 1;
}

/// <summary>
/// A lease-backed actor handle (Section 9.4). Not independently disposable; the scenario scope
/// owns its lifecycle. No credential access on the public surface — authentication flows accept
/// the handle and resolve credentials internally.
/// </summary>
public sealed class TestActor
{
    internal TestActor(TestUserLease lease)
    {
        Lease = lease;
    }

    public TestUser User => Lease.User;

    internal TestUserLease Lease { get; }

    /// <summary>Framework-internal credential resolution used by application authentication flows.</summary>
    internal ValueTask<LoginCredentials> ResolveCredentialsAsync(ICredentialResolver resolver, CancellationToken cancellationToken)
        => resolver.ResolveAsync(User, cancellationToken);
}

/// <summary>
/// Internal machinery that acquires the declared slot set before scenario composition and
/// registers the resulting <see cref="TestActor"/> handles (Section 9.4). Tests never call it;
/// it is also the <see cref="IActorReleaser"/> the scenario scope releases in teardown.
/// </summary>
public sealed class TestActorManager(IUserLeaseManager leaseManager) : IActorReleaser
{
    private readonly Dictionary<string, TestActor> _actors = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, TestActor> Actors => _actors;

    /// <summary>Acquires the complete declared slot set atomically, then registers the handles.</summary>
    public async ValueTask AcquireAsync(ActorPlan plan, CancellationToken cancellationToken)
    {
        var slotNames = plan.Slots.Keys.ToList();
        var requests = slotNames.Select(name => plan.Slots[name]).ToList();

        var leases = await leaseManager.AcquireSetAsync(requests, cancellationToken).ConfigureAwait(false);

        for (var i = 0; i < slotNames.Count; i++)
        {
            _actors[slotNames[i]] = new TestActor(leases[i]);
        }
    }

    public TestActor SoleActor => _actors.Count == 1
        ? _actors.Values.First()
        : throw new InvalidOperationException($"Expected exactly one actor but the plan declared {_actors.Count}.");

    public TestActor Slot(string name) => _actors.TryGetValue(name, out var actor)
        ? actor
        : throw new KeyNotFoundException($"No actor slot named '{name}' was declared in the actor plan.");

    public async ValueTask ReleaseAllAsync()
    {
        foreach (var actor in _actors.Values)
        {
            // Phase 1: healthy release. Plausibility-based quarantine on cleanup/context-close
            // failure (Section 15.6) is a Phase 2 refinement.
            await actor.Lease.ReleaseAsync(ActorReleaseOutcome.Healthy).ConfigureAwait(false);
        }
    }
}
