namespace UiAutomation.Framework.Users;

/// <summary>
/// A request for a user lease by role, permissions, and metadata (Section 15.1 / ADR-005).
/// At least one criterion is required; empty requests are rejected at validation time.
/// </summary>
public sealed record UserLeaseRequest
{
    public RoleKey? Role { get; init; }

    public string? PersonaKey { get; init; }

    public IReadOnlySet<string> RequiredPermissions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Negative tests need this: a "user without approval rights" test is invalidated if the
    /// matcher hands it a user who also holds approval rights (Section 15.1).
    /// </summary>
    public IReadOnlySet<string> ForbiddenPermissions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> RequiredMetadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> RequiredAccountStates { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public TimeSpan AcquisitionTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>True when the request specifies at least one matching criterion.</summary>
    public bool HasCriteria =>
        Role is not null
        || !string.IsNullOrEmpty(PersonaKey)
        || RequiredPermissions.Count > 0
        || ForbiddenPermissions.Count > 0
        || RequiredMetadata.Count > 0
        || RequiredAccountStates.Count > 0;
}

/// <summary>Outcome of releasing an actor's lease (Section 15.6). Only Healthy returns the account to the pool.</summary>
public enum ActorReleaseOutcome
{
    Healthy,
    SuspectedContaminated,
    AuthenticationFailed,
    CleanupFailed,
    Quarantined,
}

/// <summary>Raised when no eligible user can be leased within the acquisition timeout (Section 15.3) — an infrastructure-capacity failure, classified separately from a product defect.</summary>
public sealed class LeaseCapacityException : Exception
{
    public LeaseCapacityException(string message)
        : base(message)
    {
    }
}
