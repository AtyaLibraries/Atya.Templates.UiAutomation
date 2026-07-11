namespace UiAutomation.Framework.Composition;

/// <summary>
/// What the test body receives (Section 9.2): the composed scenario, the actor(s), and the
/// test cancellation token — nothing disposable. Tests never receive, dispose, or manage a
/// lifecycle object; the framework owns the whole lifecycle.
/// </summary>
public sealed class ScenarioExecution<TScenario>
    where TScenario : notnull
{
    private readonly TestActorManager _actors;

    internal ScenarioExecution(TScenario scenario, TestActorManager actors, CancellationToken cancellationToken)
    {
        Scenario = scenario;
        _actors = actors;
        CancellationToken = cancellationToken;
    }

    public TScenario Scenario { get; }

    /// <summary>The single actor (single-slot form).</summary>
    public TestActor Actor => _actors.SoleActor;

    /// <summary>A named actor slot (multi-actor form; additive per Section 20.3).</summary>
    public TestActor Session(string name) => _actors.Slot(name);

    public CancellationToken CancellationToken { get; }
}
