using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Gameplay.Combat;
using Meitou.Modding;
using Meitou.Simulation;

namespace Execution;

/// <summary>
/// The mod's entry: registers the execution system, the Executing job, the weapon swings and the interface (the right-click menu row, the limb
/// menu and the AI tab's Execute Neutrals option), all through the game's mod hooks; the mod patches nothing.
/// </summary>
public sealed class ExecutionMod : IMod
{
    /// <summary>
    /// The execution swing each weapon kind plays (the game's COMBAT_TECHNIQUE records): heavy weapons, hackers, blunt weapons, polearms and
    /// sabres chop down, katanas cut down, bare hands kick; other kinds keep the prison executioners' chop.
    /// </summary>
    static readonly (WeaponCategory Kind, string Technique)[] SwingByKind =
    [
        (WeaponCategory.Heavy, "heavy downcut v2"), (WeaponCategory.Hackers, "heavy downcut v2"), (WeaponCategory.Blunt, "heavy downcut v2"),
        (WeaponCategory.Polearms, "heavy downcut v2"), (WeaponCategory.Sabres, "heavy downcut v2"), (WeaponCategory.Katanas, "Downward combo"),
        (WeaponCategory.Unarmed, "ma kicklow"),
    ];

    /// <summary>
    /// The mod's context, kept for the interface to queue its orders.
    /// </summary>
    internal static ModContext? Context { get; private set; }

    /// <summary>
    /// The execution swing of each weapon kind (<see cref="WeaponCategory"/> as a number), read from the game data.
    /// </summary>
    internal static Dictionary<int, CombatTechnique> Swings { get; } = [];

    /// <summary>
    /// Registers the execution system (after every standard system, so its slow step sees the tick's combat), the Executing job (named in the
    /// jobs panel and saved by the game), the swing the animation plays while chopping, and the interface.
    /// </summary>
    public void Initialize(ModContext context)
    {
        Context = context;
        context.AddSystem("executions", ExecutionSystem.Create, SystemAnchor.End);
        context.AddJobType(ExecutionSystem.Row, ExecutionSystem.JobName);
        LoadSwings(context);
        context.AddCombatMove(Swing);
        context.AddUi(new MaimMenu());
    }

    /// <summary>
    /// Reads each weapon kind's swing technique from the game data; a missing one is logged, and that kind keeps the plain chop.
    /// </summary>
    static void LoadSwings(ModContext context)
    {
        var records = context.Data
            .OfType(FcsRecordType.CombatTechnique)
            .GroupBy(r => r.Name)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var (kind, name) in SwingByKind)
        {
            if (records.TryGetValue(name, out var record)) Swings[(int)kind] = CombatTechnique.From(record);
            else
                context.LogError($"no combat technique '{name}': {kind} executions use the plain chop");
        }
    }

    /// <summary>
    /// The combat move the animation plays for <paramref name="id"/>: its execution swing while it chops, else none. Read-only and thread-safe,
    /// from the swings the system's serial step set (<see cref="ExecutionSystem.SwingOf"/>).
    /// </summary>
    static CombatMove? Swing(World world, CharacterId id, long tick) =>
        ExecutionSystem.Current?.SwingOf(id, tick) is { } swing ? new CombatMove(swing.Technique, swing.Progress) : null;
}
