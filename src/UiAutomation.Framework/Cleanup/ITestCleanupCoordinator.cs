namespace UiAutomation.Framework.Cleanup;

/// <summary>
/// Registers cleanup <em>operations</em> (not bare resources) so each knows how to undo
/// itself, and executes them under the teardown token (Section 21.1). Registered
/// immediately after each resource is created; default execution order is LIFO.
/// </summary>
public interface ITestCleanupCoordinator
{
    void Register(string name, Func<CancellationToken, ValueTask> cleanup, CleanupOptions options);

    /// <summary>
    /// Executes all registered steps LIFO. Every step is attempted even if an earlier one
    /// fails; all failures are collected and thrown together. Completed steps are never
    /// re-run on a subsequent call (resume-after-partial semantics).
    /// </summary>
    ValueTask ExecuteAsync(CancellationToken teardownToken);
}
