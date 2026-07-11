using UiAutomation.Framework.Composition;
using Xunit;

// The assembly fixture builds the DI root, validated configuration, and the process
// concurrency gate once for the whole UI test assembly (docs/architecture.md Section 9.1).
[assembly: AssemblyFixture(typeof(HostFixture))]
