using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Execution;

/// <summary>
/// The execution system on games before the code-mod API 3 (0.22.1 and older): registers the Executing job's contributor with the decision
/// system. Never loaded on newer games, where that registration is internal (<c>ExecutionSystemChooser</c> in Execution.Chooser.dll instead).
/// </summary>
public sealed class ExecutionSystemLegacy : ExecutionSystem, IDecisionOwner
{
    ExecutionSystemLegacy(SystemBuildContext context) : base(context) { }

    /// <summary>
    /// The factory the mod registers on these games.
    /// </summary>
    public static ITickSystem Create(SystemBuildContext context) => Remember(new ExecutionSystemLegacy(context));

    /// <summary>
    /// Registers this system as the Executing job's contributor.
    /// </summary>
    public void RegisterSources(DecisionSystem decisions) => decisions.RegisterJob(Row, this);
}
