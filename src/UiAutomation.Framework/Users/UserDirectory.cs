using System.Text.Json;

namespace UiAutomation.Framework.Users;

/// <summary>Loads the configured <see cref="TestUser"/> set from the environment's JSON user file (Section 14).</summary>
public static class UserDirectory
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<TestUser> Load(string userFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userFilePath);
        if (!File.Exists(userFilePath))
        {
            throw new FileNotFoundException($"User file not found: {userFilePath}", userFilePath);
        }

        using var stream = File.OpenRead(userFilePath);
        var file = JsonSerializer.Deserialize<UsersFileDto>(stream, Options)
            ?? throw new InvalidOperationException($"User file '{userFilePath}' deserialized to null.");

        return file.Users.Select(Map).ToList();
    }

    private static TestUser Map(UserDto dto) => new()
    {
        Key = dto.Key,
        Credentials = new CredentialReference
        {
            Username = dto.Credentials.Username,
            PasswordSecretName = dto.Credentials.PasswordSecretName,
        },
        Role = new RoleKey(dto.Role),
        Permissions = new HashSet<string>(dto.Permissions, StringComparer.OrdinalIgnoreCase),
        Metadata = new Dictionary<string, string>(dto.Metadata, StringComparer.OrdinalIgnoreCase),
        SessionCapacity = dto.SessionCapacity,
        ConcurrencyMode = Enum.Parse<ConcurrencyMode>(dto.ConcurrencyMode, ignoreCase: true),
        IsActive = dto.IsActive,
    };

    private sealed class UsersFileDto
    {
        public List<UserDto> Users { get; set; } = [];
    }

    private sealed class UserDto
    {
        public string Key { get; set; } = string.Empty;

        public CredentialDto Credentials { get; set; } = new();

        public string Role { get; set; } = string.Empty;

        public List<string> Permissions { get; set; } = [];

        public Dictionary<string, string> Metadata { get; set; } = [];

        public int SessionCapacity { get; set; } = 1;

        public string ConcurrencyMode { get; set; } = "Exclusive";

        public bool IsActive { get; set; } = true;
    }

    private sealed class CredentialDto
    {
        public string Username { get; set; } = string.Empty;

        public string PasswordSecretName { get; set; } = string.Empty;
    }
}
