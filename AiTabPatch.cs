using HarmonyLib;
using Meitou.Data.Ui;
using Meitou.Game.Ui;
using Meitou.Modding;

namespace Execution;

/// <summary>
/// The "Execute Neutrals" tick button on the squad overview's AI tab (beside Heal Allies, Rescue Allies and the rest): ticked, the Executing job
/// also finishes off downed neutrals; clear, hostiles only. A postfix on the tab's content (<c>Meitou.Game.Ui.SquadAiTab.AddAiContent</c>) adds
/// the button under the game's options, shrinking their rows so the Follow Formation line below stays where the game put it; a prefix on the tab's
/// event handler (<c>AiAction</c>) takes the button's presses.
/// </summary>
static class AiTabPatch
{
    /// <summary>
    /// The button's control name and its press action.
    /// </summary>
    const string Name = "ai-option-execution", Action = "execution:strangers";

    /// <summary>
    /// The game's option buttons' name prefix.
    /// </summary>
    const string OptionPrefix = "ai-option-";

    /// <summary>
    /// The button's caption and tooltip.
    /// </summary>
    const string Caption = "Execute Neutrals",
        Tip = "Characters with the Executing job will also finish off downed neutrals, not only hostiles. Their factions will turn against you, "
            + "and guards who see it will call it murder.";

    /// <summary>
    /// Patches the tab; a missing member is logged and leaves the tab as it is.
    /// </summary>
    public static void Apply(ModContext context)
    {
        var tab = AccessTools.TypeByName("Meitou.Game.Ui.SquadAiTab");
        var content = tab is null ? null : AccessTools.Method(tab, "AddAiContent");
        var action = tab is null ? null : AccessTools.Method(tab, "AiAction");
        if (content is null || action is null)
        {
            context.LogError("the squad AI tab was not found: no Execute Neutrals option (the game changed; rebuild the mod)");
            return;
        }

        context.Harmony.Patch(content, postfix: new HarmonyMethod(typeof(AiTabPatch), nameof(AddButton)));
        context.Harmony.Patch(action, prefix: new HarmonyMethod(typeof(AiTabPatch), nameof(Press)));
    }

    /// <summary>
    /// After the game laid out its option buttons: they move up into rows of n / (n + 1) of their height, and the button takes the freed row
    /// above Follow Formation. Nothing when the tab is not open (the game added no option buttons).
    /// </summary>
    static void AddButton(List<UiControl> controls)
    {
        var options = controls.Select((c, i) => (Control: c, Index: i))
            .Where(x => x.Control.Name.StartsWith(OptionPrefix, StringComparison.Ordinal) && x.Control.Name != Name)
            .OrderBy(x => x.Control.Bounds.Y)
            .ToList();
        if (options.Count == 0 || controls.FirstOrDefault(c => c.Name == "AIOptions") is not { } panel) return;

        int n = options.Count;
        float old = n > 1 ? options[1].Control.Bounds.Y - options[0].Control.Bounds.Y : options[0].Control.Bounds.Height + 4;
        float line = old * n / (n + 1);
        for (int i = 0; i < n; i++)
        {
            var (control, index) = options[i];
            controls[index] = control with { Bounds = Row(panel.Bounds, control.Bounds, i, line) };
        }

        controls.Add(Button(options[0].Control, panel.Bounds, n, line));
    }

    /// <summary>
    /// Row <paramref name="i"/> of the panel at <paramref name="line"/> per row, as wide as the game's buttons.
    /// </summary>
    static UiRect Row(UiRect panel, UiRect like, int i, float line) => new(like.X, panel.Y + i * line, like.Width, line - 4);

    /// <summary>
    /// The button: a copy of the game's first option button with this one's name, caption, action, tooltip and tick.
    /// </summary>
    static UiControl Button(UiControl like, UiRect panel, int row, float line)
    {
        bool ticked = ExecutionSystem.Current?.Strangers == true;
        var properties = new Dictionary<string, string>(like.Properties)
        {
            ["tooltip_text"] = Tip,
            ["Checked"] = ticked ? "true" : "false",
            ["StateSelected"] = ticked ? "true" : "false",
        };
        return like with { Name = Name, Caption = Caption, Action = Action, Bounds = Row(panel, like.Bounds, row, line), Properties = properties };
    }

    /// <summary>
    /// A press of the button flips the option (through the simulation, so it is saved with the game); every other event goes on to the game.
    /// </summary>
    static bool Press(MyGuiEvent e)
    {
        if (e.Action != Action) return true;
        if (ExecutionMod.Context is { } context)
            context.Enqueue(new SetExecuteStrangers(!(ExecutionSystem.Current?.Strangers ?? false)) { Tick = context.Tick });
        return false;
    }
}
