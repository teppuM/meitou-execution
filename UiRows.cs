using Meitou.Game.Ui;
using Meitou.Simulation;

namespace Execution;

/// <summary>
/// The mod's rows in the game's own windows, through the game's interface hooks: "Violence..." in the right-click menu of a downed body
/// (<see cref="UiRegistration.AddCharacterMenuVerbs"/>) and the squad AI tab's Execute Neutrals option (<see cref="UiRegistration.AddSquadAiOptions"/>).
/// Both run on the interface's thread and only read snapshots and queue commands.
/// </summary>
static class UiRows
{
    /// <summary>
    /// The right-click menu row.
    /// </summary>
    public const string ViolenceCaption = "Violence...";

    /// <summary>
    /// The AI tab option's caption.
    /// </summary>
    const string NeutralsCaption = "Execute Neutrals";

    /// <summary>
    /// The AI tab option's tooltip.
    /// </summary>
    const string NeutralsTip = "Characters with the Executing job will also finish off downed neutrals, not only hostiles. Their factions will turn "
        + "against you, and guards who see it will call it murder.";

    /// <summary>
    /// Adds the menu row and the AI tab option.
    /// </summary>
    public static void Register(UiRegistration registration)
    {
        registration.AddCharacterMenuVerbs(Verbs);
        registration.AddSquadAiOptions(Options);
    }

    /// <summary>
    /// The row for a downed, living, uncarried character who is not the player's and not in a cage, while some character is selected. Chosen, it
    /// opens the limb menu (<see cref="MaimMenu"/>) where the right-click menu was; chosen with Shift held, it gives the selection the Executing job.
    /// </summary>
    static IEnumerable<UiMenuVerb> Verbs(UiMenuContext menu)
    {
        var target = menu.Target;
        if (!target.Down || target.IsPlayer || target.Prison is not null || !target.CarriedBy.IsNone) return [];
        if (target.Body is { Dead: true } || menu.Selected.Count == 0) return [];

        IReadOnlyList<CharacterId> selection = [.. menu.Selected];
        var at = menu.Cursor;
        return [new UiMenuVerb(ViolenceCaption, () => Choose(menu, selection, target, at))];
    }

    /// <summary>
    /// The row chosen: the Executing job with Shift held, else the limb menu.
    /// </summary>
    static void Choose(UiMenuContext menu, IReadOnlyList<CharacterId> selection, CharacterSnapshot target, System.Numerics.Vector2 at)
    {
        if (menu.Shift) menu.Enqueue(new ExecuteOrder(selection, target.Id) { Tick = menu.Tick, AsJob = true });
        else MaimMenu.Show(selection, target, at);
    }

    /// <summary>
    /// The Execute Neutrals tick button, ticked while the system's option is on; a press queues the change, which the system applies in its serial
    /// step and saves.
    /// </summary>
    static IEnumerable<UiSquadAiOption> Options(UiSquadAiContext tab)
    {
        bool on = ExecutionSystem.Current?.Strangers == true;
        return [new UiSquadAiOption(NeutralsCaption, NeutralsTip, on, () => Toggle(tab))];
    }

    /// <summary>
    /// Queues the option's change from its value now.
    /// </summary>
    static void Toggle(UiSquadAiContext tab) =>
        tab.Enqueue(new SetExecuteStrangers(!(ExecutionSystem.Current?.Strangers ?? false)) { Tick = tab.Tick });
}
