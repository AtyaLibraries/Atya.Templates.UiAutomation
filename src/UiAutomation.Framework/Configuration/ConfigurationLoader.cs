using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace UiAutomation.Framework.Configuration;

/// <summary>
/// Loads and validates configuration before any test runs (Section 6.5 / ADR-003).
/// Precedence is fixed: base JSON -&gt; environment overlay -&gt; execution profile ->
/// environment-variable overrides. Paths resolve against a stable content root, never the
/// current working directory. Invalid infrastructure configuration fails fast.
/// </summary>
public static class ConfigurationLoader
{
    public const string EnvVariable = "TEST_ENV";
    public const string ProfileVariable = "TEST_PROFILE";

    public static TestConfiguration Load(string? contentRoot = null, string? environmentName = null, string? profileName = null)
    {
        contentRoot ??= FindContentRoot();

        var env = (environmentName ?? Environment.GetEnvironmentVariable(EnvVariable) ?? "local").Trim();
        var profile = (profileName ?? Environment.GetEnvironmentVariable(ProfileVariable) ?? "chromium-headless").Trim();

        if (!Enum.TryParse<EnvironmentName>(env, ignoreCase: true, out var envEnum))
        {
            throw new ConfigurationException(
                $"{EnvVariable}='{env}' is not a supported environment (expected: local, dev, qa, uat).");
        }

        var settings = LoadEnvironment(contentRoot, env, envEnum);
        var executionProfile = LoadProfile(contentRoot, profile);
        Validate(contentRoot, settings, executionProfile);

        return new TestConfiguration
        {
            Environment = settings,
            Profile = executionProfile,
            ContentRoot = contentRoot,
        };
    }

    private static EnvironmentSettings LoadEnvironment(string contentRoot, string env, EnvironmentName envEnum)
    {
        var configDir = Path.Combine(contentRoot, "config");
        var basePath = Path.Combine(configDir, "appsettings.json");
        if (!File.Exists(basePath))
        {
            throw new ConfigurationException($"Base configuration not found: {basePath}");
        }

        var overlayPath = Path.Combine(configDir, $"appsettings.{env}.json");
        if (!File.Exists(overlayPath))
        {
            throw new ConfigurationException($"Environment overlay not found for '{env}': {overlayPath}");
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(configDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{env}.json", optional: false)
            .AddEnvironmentVariables("UIA_")
            .Build();

        var dto = configuration.GetSection("Environment").Get<EnvironmentDto>()
            ?? throw new ConfigurationException("The 'Environment' configuration section is missing or empty.");

        if (string.IsNullOrWhiteSpace(dto.BaseUrl) || !Uri.TryCreate(dto.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            throw new ConfigurationException($"Environment '{env}' has a missing or non-absolute BaseUrl ('{dto.BaseUrl}').");
        }

        Uri? apiBaseUrl = null;
        if (!string.IsNullOrWhiteSpace(dto.ApiBaseUrl) && !Uri.TryCreate(dto.ApiBaseUrl, UriKind.Absolute, out apiBaseUrl))
        {
            throw new ConfigurationException($"Environment '{env}' has a non-absolute ApiBaseUrl ('{dto.ApiBaseUrl}').");
        }

        return new EnvironmentSettings
        {
            Name = envEnum,
            BaseUrl = baseUrl,
            ApiBaseUrl = apiBaseUrl,
            UserFile = dto.UserFile ?? "test-assets/users/users.json",
            TestDataRoot = dto.TestDataRoot ?? "test-assets/data",
            Secrets = new SecretProviderSettings { Provider = dto.Secrets?.Provider ?? "Environment", Endpoint = dto.Secrets?.Endpoint },
            Features = new Dictionary<string, bool>(dto.Features, StringComparer.OrdinalIgnoreCase),
            Capabilities = new HashSet<string>(dto.Capabilities, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static ExecutionProfile LoadProfile(string contentRoot, string profileName)
    {
        var path = Path.Combine(contentRoot, "config", "execution-profiles.json");
        if (!File.Exists(path))
        {
            throw new ConfigurationException($"Execution profiles file not found: {path}");
        }

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<ProfilesFileDto>(stream, WebJson)
            ?? throw new ConfigurationException("execution-profiles.json deserialized to null.");

        if (!document.Profiles.TryGetValue(profileName, out var dto))
        {
            var available = string.Join(", ", document.Profiles.Keys);
            throw new ConfigurationException($"{ProfileVariable}='{profileName}' is not defined. Available: [{available}].");
        }

        var profile = new ExecutionProfile
        {
            Name = dto.Name ?? profileName,
            Browser = Enum.Parse<BrowserKind>(dto.Browser ?? "Chromium", ignoreCase: true),
            Headless = dto.Headless ?? true,
            MaxParallelTests = dto.MaxParallelTests ?? 4,
            Trace = Enum.Parse<CapturePolicy>(dto.Trace ?? "RetainOnFailure", ignoreCase: true),
            Video = Enum.Parse<CapturePolicy>(dto.Video ?? "Off", ignoreCase: true),
        };

        return profile;
    }

    private static void Validate(string contentRoot, EnvironmentSettings settings, ExecutionProfile profile)
    {
        var userFile = ResolvePath(contentRoot, settings.UserFile);
        if (!File.Exists(userFile))
        {
            throw new ConfigurationException($"User file does not exist: {userFile}");
        }

        var testDataRoot = ResolvePath(contentRoot, settings.TestDataRoot);
        if (!Directory.Exists(testDataRoot))
        {
            throw new ConfigurationException($"Test-data root does not exist: {testDataRoot}");
        }

        if (profile.MaxParallelTests < 1)
        {
            throw new ConfigurationException($"Profile '{profile.Name}' has MaxParallelTests < 1.");
        }

        var ceiling = ReadMaxParallelThreads(contentRoot);
        if (ceiling is { } max && profile.MaxParallelTests > max)
        {
            throw new ConfigurationException(
                $"Profile '{profile.Name}' MaxParallelTests ({profile.MaxParallelTests}) exceeds the " +
                $"testconfig.json maxParallelThreads ceiling ({max}).");
        }
    }

    /// <summary>Resolves a possibly-relative path against the content root (Section 6.5).</summary>
    public static string ResolvePath(string contentRoot, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(contentRoot, path));

    private static int? ReadMaxParallelThreads(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "testconfig.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("xUnit", out var xunit) &&
            xunit.TryGetProperty("maxParallelThreads", out var threads) &&
            threads.TryGetInt32(out var value))
        {
            return value;
        }

        return null;
    }

    private static string FindContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config", "appsettings.json")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new ConfigurationException(
            "Could not locate the content root (an ancestor directory containing config/appsettings.json).");
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private sealed class EnvironmentDto
    {
        public string? Name { get; set; }

        public string? BaseUrl { get; set; }

        public string? ApiBaseUrl { get; set; }

        public string? UserFile { get; set; }

        public string? TestDataRoot { get; set; }

        public SecretDto? Secrets { get; set; }

        public Dictionary<string, bool> Features { get; set; } = [];

        public List<string> Capabilities { get; set; } = [];
    }

    private sealed class SecretDto
    {
        public string? Provider { get; set; }

        public string? Endpoint { get; set; }
    }

    private sealed class ProfilesFileDto
    {
        public Dictionary<string, ProfileDto> Profiles { get; set; } = [];
    }

    private sealed class ProfileDto
    {
        public string? Name { get; set; }

        public string? Browser { get; set; }

        public bool? Headless { get; set; }

        public int? MaxParallelTests { get; set; }

        public string? Trace { get; set; }

        public string? Video { get; set; }
    }
}
