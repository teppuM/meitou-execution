using System.Reflection;
using HarmonyLib;
using Meitou.Modding;
using Meitou.Simulation;

namespace Execution;

/// <summary>
/// Adds "Execute" to the right-click menu of a downed, living character who is not the player's: a postfix on the menu's verb list
/// (<c>Meitou.Game.Ui.ContextMenuWindow.Verbs</c>, internal, so it is found by name). The selection is read from the window's player interface
/// (internal too). Unknown captions sort last, so the row sits at the bottom of the menu.
/// </summary>
static class MenuPatch
{
    /// <summary>
    /// The menu caption.
    /// </summary>
    public const string Caption = "Execute";

    /// <summary>
    /// The window's player interface field (a captured primary-constructor parameter, so found by type, not name).
    /// </summary>
    static FieldInfo? s_player;

    /// <summary>
    /// The player interface's selection read, <c>SelectedIds()</c>.
    /// </summary>
    static MethodInfo? s_selectedIds;

    /// <summary>
    /// The player interface's Shift state, <c>SlowModifier</c> (Shift turns an order into a job).
    /// </summary>
    static PropertyInfo? s_shift;

    /// <summary>
    /// Finds the menu's verb list and the selection read, and patches the list. A missing member is logged and leaves the menu alone.
    /// </summary>
    public static void Apply(ModContext context)
    {
        var window = AccessTools.TypeByName("Meitou.Game.Ui.ContextMenuWindow");
        var verbs = window is null ? null : AccessTools.Method(window, "Verbs", [typeof(CharacterSnapshot)]);
        s_player = window?.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType.Name == "PlayerInterface");
        s_selectedIds = s_player is null ? null : AccessTools.Method(s_player.FieldType, "SelectedIds");
        s_shift = s_player is null ? null : AccessTools.Property(s_player.FieldType, "SlowModifier");
        if (verbs is null || s_selectedIds is null)
        {
            context.LogError("the right-click menu's verb list was not found: no Execute row (the game changed; rebuild the mod)");
            return;
        }

        context.Harmony.Patch(verbs, postfix: new HarmonyMethod(typeof(MenuPatch), nameof(Postfix)));
    }

    /// <summary>
    /// Adds the row to a downed, living, uncarried character of another faction who is not in a cage, while some character is selected. Chosen with
    /// Shift held, it gives the selection the Executing job instead.
    /// </summary>
    static void Postfix(object __instance, CharacterSnapshot target, List<(string, Action)> __result)
    {
        if (!target.Down || target.IsPlayer || target.Prison is not null || !target.CarriedBy.IsNone) return;
        if (target.Body is { Dead: true }) return;
        if (Selection(__instance) is not { Count: > 0 } selection) return;

        var id = target.Id;
        var player = s_player?.GetValue(__instance);
        __result.Add((Caption, () => Give(selection, id, Shift(player))));
    }

    /// <summary>
    /// Whether Shift is held now (false when it cannot be read).
    /// </summary>
    static bool Shift(object? player) => player is not null && s_shift?.GetValue(player) is true;

    /// <summary>
    /// The window's current selection; null when it cannot be read.
    /// </summary>
    static IReadOnlyList<CharacterId>? Selection(object window)
    {
        var player = s_player?.GetValue(window);
        return player is null ? null : s_selectedIds?.Invoke(player, null) as IReadOnlyList<CharacterId>;
    }

    /// <summary>
    /// Queues the order, or the job with <paramref name="asJob"/>, for the next tick (or at once while paused, as every order is).
    /// </summary>
    static void Give(IReadOnlyList<CharacterId> selection, CharacterId target, bool asJob)
    {
        if (ExecutionMod.Context is not { } context) return;
        context.Enqueue(new ExecuteOrder(selection, target) { Tick = context.Tick, AsJob = asJob });
    }
}
