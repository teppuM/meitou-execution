using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Combat;

namespace Execution;

/// <summary>
/// Weapon swings for executions: the combat technique each weapon kind swings at a body on the ground (from the game's own COMBAT_TECHNIQUE
/// records), and a postfix on <see cref="CombatSystem.Playing"/>, the animation layer's question of which technique a character plays, that answers
/// for a chopping executioner. <c>Playing</c> has no other caller, so the patch changes what is drawn and nothing in the simulation.
/// </summary>
static class SwingPatch
{
    /// <summary>
    /// The technique record each weapon kind uses, by name: a big overhead chop for heavy weapons, hackers, blunt weapons, polearms and sabres; the
    /// downward combo for katanas; a low kick for bare hands. Other kinds (crossbows, turrets, animals) keep the executioners' chop.
    /// </summary>
    static readonly (WeaponCategory Kind, string Technique)[] ByKind =
    [
        (WeaponCategory.Heavy, "heavy downcut v2"), (WeaponCategory.Hackers, "heavy downcut v2"), (WeaponCategory.Blunt, "heavy downcut v2"),
        (WeaponCategory.Polearms, "heavy downcut v2"), (WeaponCategory.Sabres, "heavy downcut v2"), (WeaponCategory.Katanas, "Downward combo"),
        (WeaponCategory.Unarmed, "ma kicklow"),
    ];

    /// <summary>
    /// Reads the swings from the merged data into <paramref name="swings"/> (a missing record is logged and that kind keeps the chop) and patches
    /// <see cref="CombatSystem.Playing"/>.
    /// </summary>
    public static void Apply(ModContext context, Dictionary<int, CombatTechnique> swings)
    {
        var records = context.Data.OfType(FcsRecordType.CombatTechnique).GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First());
        foreach (var (kind, name) in ByKind)
        {
            if (records.TryGetValue(name, out var record)) swings[(int)kind] = CombatTechnique.From(record);
            else
                context.LogError($"no combat technique '{name}': {kind} executions use the plain chop");
        }

        var playing = AccessTools.Method(typeof(CombatSystem), nameof(CombatSystem.Playing));
        if (playing is null)
        {
            context.LogError("CombatSystem.Playing was not found: executions use the plain chop");
            return;
        }
        context.Harmony.Patch(playing, postfix: new HarmonyMethod(typeof(SwingPatch), nameof(Postfix)));
    }

    /// <summary>
    /// A character the combat system has no technique for, chopping with a weapon swing: that swing and its progress.
    /// </summary>
    static void Postfix(CharacterId id, long tick, ref (CombatTechnique Technique, float Progress, bool IsAttack)? __result)
    {
        if (__result is null && ExecutionSystem.Current?.SwingOf(id, tick) is { } swing) __result = swing;
    }
}
