using Microsoft.Playwright;
using UiAutomation.Framework.Composition;
using UiAutomation.Framework.Configuration;

namespace UiAutomation.Framework.Reporting;

/// <summary>
/// Starts/stops Playwright tracing per the trace <see cref="CapturePolicy"/> (Section 6.4 /
/// Section 8). The trace is exported to the private raw directory and only promoted to the
/// publish directory when the policy retains it.
/// </summary>
public sealed class PlaywrightCaptureController(IBrowserContext context, ExecutionProfile profile, ArtifactPaths paths)
    : ICaptureController
{
    private bool _tracing;

    public async ValueTask StartAsync()
    {
        if (profile.Trace == CapturePolicy.Off)
        {
            return;
        }

        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        }).ConfigureAwait(false);

        _tracing = true;
    }

    public async ValueTask FinalizeAsync(bool testFailed, CancellationToken cancellationToken)
    {
        if (!_tracing)
        {
            return;
        }

        _tracing = false;

        var retain = profile.Trace == CapturePolicy.Always || (profile.Trace == CapturePolicy.RetainOnFailure && testFailed);
        if (retain)
        {
            var rawTrace = paths.RawFile("trace.zip");
            await context.Tracing.StopAsync(new TracingStopOptions { Path = rawTrace }).ConfigureAwait(false);
            paths.PromoteToPublish(rawTrace);
        }
        else
        {
            // Stop and discard on success under RetainOnFailure.
            await context.Tracing.StopAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Best-effort failure diagnostics (Section 9.2): a screenshot captured while the page and
/// lease are still live. Written to the private raw directory, then promoted to publish
/// (screenshot-on-failure is the Phase 1 policy-approved artifact).
/// </summary>
public sealed class PlaywrightFailureDiagnostics(IPage page, ArtifactPaths paths) : IFailureDiagnostics
{
    public async ValueTask CaptureFailureAsync(Exception bodyException, CancellationToken cancellationToken)
    {
        if (page.IsClosed)
        {
            return;
        }

        var rawShot = paths.RawFile("screenshot.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = rawShot, FullPage = true }).ConfigureAwait(false);
        paths.PromoteToPublish(rawShot);
    }
}
