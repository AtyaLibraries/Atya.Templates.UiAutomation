namespace UiAutomation.Framework.Users;

/// <summary>
/// Framework-neutral role value (Section 14). The framework never hardcodes the target
/// application's role names; the Application project maps its vocabulary onto these.
/// </summary>
public readonly record struct RoleKey(string Value)
{
    public static implicit operator RoleKey(string value) => new(value);

    public override string ToString() => Value;
}

/// <summary>How safely an account may be shared across concurrent tests (Section 14).</summary>
public enum ConcurrencyMode
{
    Exclusive,
    ReadOnlyShared,
    IsolatedTenantShared,
    ApplicationVerifiedShared,
}

/// <summary>A reference to a credential; the password itself is resolved via <see cref="Secrets.ISecretProvider"/>.</summary>
public sealed record CredentialReference
{
    public required string Username { get; init; }

    public required string PasswordSecretName { get; init; }
}

/// <summary>A leasable test user (Section 14).</summary>
public sealed record TestUser
{
    public required string Key { get; init; }

    public required CredentialReference Credentials { get; init; }

    public required RoleKey Role { get; init; }

    public IReadOnlySet<string> Permissions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public int SessionCapacity { get; init; } = 1;

    public ConcurrencyMode ConcurrencyMode { get; init; } = ConcurrencyMode.Exclusive;

    public bool IsActive { get; init; } = true;

    /// <summary>Effective capacity: a value &gt; 1 is honored only for a non-exclusive mode (Section 14).</summary>
    public int EffectiveCapacity =>
        ConcurrencyMode == ConcurrencyMode.Exclusive ? 1 : Math.Max(1, SessionCapacity);
}
