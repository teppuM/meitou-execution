using Meitou.Simulation;
using Meitou.Simulation.Ai;

namespace Execution;

/// <summary>
/// The execution system on games with the code-mod API 3 (the chooser registry): registers the Executing job's contributor with the chooser.
/// Compiled against such a game into Execution.Chooser.dll, which the mod loads only there.
/// </summary>
public sealed class ExecutionSystemChooser : ExecutionSystem, IChooserOwner
{
    ExecutionSystemChooser(SystemBuildContext context) : base(context) { }

    /// <summary>
    /// The factory the mod registers on these games.
    /// </summary>
    public static ITickSystem Create(SystemBuildContext context) => Remember(new ExecutionSystemChooser(context));

    /// <summary>
    /// Registers this system as the Executing job's contributor.
    /// </summary>
    public void Register(ChooserRegistry chooser) => chooser.Jobs(Row, this);
}
