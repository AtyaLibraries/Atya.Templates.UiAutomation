using UiAutomation.Framework.Configuration;

// ─────────────────────────────────────────────────────────────────────────────
//  Environment health check (docs/architecture.md Section 24). A dedicated console tool
//  with a real entry point, run as `dotnet run --project tools/UiAutomation.Preflight`.
//  Health-check failures are INFRASTRUCTURE failures (non-zero exit), not product-test
//  failures — a failed preflight stops the affected test stage.
//
//  Phase 1 scope: base URL reachability + secret-provider configuration. Full checks (auth
//  provider, integrations, capabilities, user capacity) are Phase 2.
// ─────────────────────────────────────────────────────────────────────────────

TestConfiguration config;
try
{
    config = ConfigurationLoader.Load();
}
catch (ConfigurationException exception)
{
    await Console.Error.WriteLineAsync($"[preflight] configuration invalid: {exception.Message}");
    return 1;
}

Console.WriteLine(
    $"[preflight] env={config.Environment.Name} profile={config.Profile.Name} " +
    $"browser={config.Profile.Browser} baseUrl={config.Environment.BaseUrl}");

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
try
{
    using var response = await http.GetAsync(config.Environment.BaseUrl);
    Console.WriteLine($"[preflight] base URL reachable: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
}
catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
{
    await Console.Error.WriteLineAsync(
        $"[preflight] base URL UNREACHABLE ({config.Environment.BaseUrl}): {exception.Message}");
    return 2;
}

Console.WriteLine($"[preflight] secret provider: {config.Environment.Secrets.Provider}");
Console.WriteLine("[preflight] OK");
return 0;
