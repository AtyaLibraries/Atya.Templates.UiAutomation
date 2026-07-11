using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using UiAutomation.Framework.Cleanup;
using UiAutomation.Framework.Configuration;
using UiAutomation.Framework.Secrets;
using UiAutomation.Framework.Users;

namespace UiAutomation.Framework.Composition;

/// <summary>
/// Builds the root dependency-injection container for the test run (Section 9.1). Immutable,
/// thread-safe shared services are singletons; per-test values flow through scoped accessors.
/// Application Page/Component/Flow/Scenario types are discovered by namespace convention so the
/// framework never names application vocabulary (Section 14).
/// </summary>
public static class Bootstrapper
{
    private static readonly string[] AppNamespaceMarkers =
        [".Pages", ".Components", ".Flows", ".Scenarios", ".Models"];

    public static ValueTask<IServiceProvider> BuildAsync(string? contentRoot = null)
    {
        var config = ConfigurationLoader.Load(contentRoot);
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(config);
        services.AddSingleton(config.Environment);
        services.AddSingleton(config.Profile);

        // Secrets + credentials (Section 16). Environment provider is the Phase 1 baseline.
        services.AddSingleton<ISecretProvider, EnvironmentSecretProvider>();
        services.AddSingleton<ICredentialResolver, CredentialResolver>();

        // Users + leasing (Section 15): in-memory, per-process for Phase 1.
        var userFile = ConfigurationLoader.ResolvePath(config.ContentRoot, config.Environment.UserFile);
        var users = UserDirectory.Load(userFile);
        services.AddSingleton<IUserLeaseManager>(new InMemoryUserLeaseManager(users));

        // Per-test scoped services.
        services.AddScoped<PageHandle>();
        services.AddScoped<TestIdentityAccessor>();
        services.AddScoped<ITestCleanupCoordinator, TestCleanupCoordinator>();
        services.AddScoped<TestActorManager>();

        RegisterApplicationGraph(services);

        IServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        return ValueTask.FromResult(provider);
    }

    /// <summary>
    /// Registers every concrete Page/Component/Flow/Scenario/Model type (by namespace marker)
    /// from the discovered application assemblies as a scoped service. Page Objects take
    /// <see cref="PageHandle"/>; Flows take Page Objects and the framework credential resolver;
    /// Scenarios take Flows/Pages — the whole graph is built by DI.
    /// </summary>
    private static void RegisterApplicationGraph(IServiceCollection services)
    {
        foreach (var assembly in DiscoverApplicationAssemblies())
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                var ns = type.Namespace ?? string.Empty;
                if (AppNamespaceMarkers.Any(marker => ns.Contains(marker, StringComparison.Ordinal)))
                {
                    services.AddScoped(type);
                }
            }
        }
    }

    private static List<Assembly> DiscoverApplicationAssemblies()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Assembly>();
        var queue = new Queue<Assembly>();

        var entry = Assembly.GetEntryAssembly();
        if (entry is not null)
        {
            queue.Enqueue(entry);
        }

        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            var name = assembly.GetName().Name ?? string.Empty;
            if (!seen.Add(name) || IsFrameworkOrRuntime(name))
            {
                continue;
            }

            result.Add(assembly);

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (seen.Contains(reference.Name ?? string.Empty) || IsFrameworkOrRuntime(reference.Name ?? string.Empty))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (Exception)
                {
                    // A reference that cannot be loaded is not an application assembly we care about.
                }
            }
        }

        return result;
    }

    private static bool IsFrameworkOrRuntime(string assemblyName) =>
        assemblyName.StartsWith("System", StringComparison.Ordinal)
        || assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
        || assemblyName.StartsWith("xunit", StringComparison.Ordinal)
        || assemblyName.StartsWith("netstandard", StringComparison.Ordinal)
        || assemblyName.EndsWith(".Framework", StringComparison.Ordinal)
        || assemblyName.EndsWith(".Framework.Tests", StringComparison.Ordinal);
}
