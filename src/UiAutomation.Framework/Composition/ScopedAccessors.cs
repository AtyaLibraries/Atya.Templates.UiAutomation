using Microsoft.Playwright;
using UiAutomation.Framework.Reporting;
using UiAutomation.Framework.Users;
using Xunit;

namespace UiAutomation.Framework.Composition;

/// <summary>
/// Carries the current test's <see cref="IPage"/> to Page Objects through DI. Deliberately
/// NOT <see cref="IDisposable"/>: <see cref="IPage"/> is never a DI service, so scope disposal
/// can never close a page (Section 9.3 / ADR-008). Page Objects take this handle.
/// </summary>
public sealed class PageHandle
{
    public IPage Page { get; private set; } = null!;

    internal void Set(IPage page) => Page = page;
}

/// <summary>Scoped accessor publishing the current test identity to framework services.</summary>
public sealed class TestIdentityAccessor
{
    public TestIdentity Identity { get; private set; } = null!;

    internal void Set(TestIdentity identity) => Identity = identity;
}

/// <summary>Wraps the process concurrency semaphore as a permit released exactly once (Section 6.4).</summary>
internal sealed class SemaphorePermit(SemaphoreSlim semaphore) : IConcurrencyPermit
{
    private int _released;

    public void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            semaphore.Release();
        }
    }
}

/// <summary>The framework-created actor contexts/pages, closed before actor release (Section 8).</summary>
internal sealed class ActorContextSet(IReadOnlyList<(IBrowserContext Context, IPage Page)> contexts) : IActorContextSet
{
    public async ValueTask CloseAllAsync()
    {
        foreach (var (context, page) in contexts)
        {
            if (!page.IsClosed)
            {
                await page.CloseAsync().ConfigureAwait(false);
            }

            await context.CloseAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>Publishes teardown warnings through the xUnit v3 <see cref="TestContext"/> (Section 22.3).</summary>
internal sealed class TestContextReporter : ITeardownReporter
{
    public void ReportWarning(string message) => TestContext.Current?.AddWarning(message);
}
