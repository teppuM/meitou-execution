using Meitou.Modding;
using Meitou.Simulation;

namespace Execution;

/// <summary>
/// The mod's entry: registers the execution system and patches the right-click menu.
/// </summary>
public sealed class ExecutionMod : IMod
{
    /// <summary>
    /// The mod's context, kept for the menu patch to queue its orders.
    /// </summary>
    internal static ModContext? Context { get; private set; }

    /// <summary>
    /// The player's settings (read by the simulation in a serial step).
    /// </summary>
    internal static ExecutionSettings Settings { get; } = new();

    /// <summary>
    /// Registers the settings, the execution system (after every standard system, so its slow step sees the tick's combat) and the patches: the
    /// menu row, the job's name in the jobs panel, and the job's save.
    /// </summary>
    public void Initialize(ModContext context)
    {
        Context = context;
        context.AddSettings(Settings);
        context.AddSystem("executions", ExecutionSystem.Create, SystemAnchor.End);
        MenuPatch.Apply(context);
        JobPatches.Apply(context);
    }
}
