namespace UiAutomation.Framework.Configuration;

/// <summary>How the framework resolves secrets for an environment (Section 16).</summary>
public sealed record SecretProviderSettings
{
    /// <summary>Provider discriminator, e.g. <c>Environment</c> (baseline) or <c>KeyVault</c> (Phase 2).</summary>
    public string Provider { get; init; } = "Environment";

    /// <summary>Optional provider-specific endpoint (e.g. Key Vault URI). Unused by the environment provider.</summary>
    public string? Endpoint { get; init; }
}

/// <summary>
/// Environment configuration (Section 6.3 / ADR-003). This is deliberately separate
/// from <see cref="ExecutionProfile"/>: browser is an execution-matrix dimension, not
/// an intrinsic property of an environment. Base URL is environment-owned and never a
/// profile value.
/// </summary>
public sealed record EnvironmentSettings
{
    public required EnvironmentName Name { get; init; }

    public required Uri BaseUrl { get; init; }

    public Uri? ApiBaseUrl { get; init; }

    public required string UserFile { get; init; }

    public required string TestDataRoot { get; init; }

    public SecretProviderSettings Secrets { get; init; } = new();

    public IReadOnlyDictionary<string, bool> Features { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> Capabilities { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
