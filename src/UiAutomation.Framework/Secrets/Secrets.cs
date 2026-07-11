using UiAutomation.Framework.Users;

namespace UiAutomation.Framework.Secrets;

/// <summary>Resolves a named secret (Section 16). The environment provider is the Phase 1 baseline; Key Vault is Phase 2.</summary>
public interface ISecretProvider
{
    ValueTask<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads secrets from environment variables (Section 16 baseline). No run-wide plaintext
/// cache — secret lifetime stays scenario-scoped (Section 9.4 / 16).
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    public ValueTask<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        var value = Environment.GetEnvironmentVariable(secretName);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"Secret '{secretName}' is not set. Provide it via an environment variable for this environment.");
        }

        return ValueTask.FromResult(value);
    }
}

/// <summary>
/// Login credentials. <see cref="ToString"/> is overridden so accidental logging, string
/// interpolation, or exception messages cannot leak the password (Section 16).
/// </summary>
public sealed record LoginCredentials(string Username, string Password)
{
    public override string ToString() => $"{Username}:[REDACTED]";
}

/// <summary>Resolves a user's login credentials by dereferencing the password secret (Section 16).</summary>
public interface ICredentialResolver
{
    ValueTask<LoginCredentials> ResolveAsync(TestUser user, CancellationToken cancellationToken = default);
}

/// <summary>Default resolver: username from the reference, password from the secret provider.</summary>
public sealed class CredentialResolver(ISecretProvider secretProvider) : ICredentialResolver
{
    public async ValueTask<LoginCredentials> ResolveAsync(TestUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var password = await secretProvider
            .GetSecretAsync(user.Credentials.PasswordSecretName, cancellationToken)
            .ConfigureAwait(false);

        return new LoginCredentials(user.Credentials.Username, password);
    }
}
