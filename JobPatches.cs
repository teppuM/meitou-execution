using System.Reflection;
using HarmonyLib;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Production;
using Meitou.Simulation.Saving;

namespace Execution;

/// <summary>
/// The Executing job's patches: its name in the jobs panel, and its save. The game's own job list would save the job as task type 229, which
/// MeitouClient drops on load and the original game was never meant to read; so the job is left out of that list while a character is written, and
/// its place in the list is kept under a key of the mod's own (<see cref="SaveKey"/>) on the same record, read back after the game's jobs.
/// </summary>
static class JobPatches
{
    /// <summary>
    /// The job's name in the jobs panel.
    /// </summary>
    public const string JobName = "Executing";

    /// <summary>
    /// The character AI record key holding the job's place in the list.
    /// </summary>
    public const string SaveKey = "execution pjob";

    /// <summary>
    /// The save's per-character record of its AI (<c>SaveCharacter.Ai</c>), found by name.
    /// </summary>
    static MemberInfo? s_ai;

    /// <summary>
    /// Applies the three patches; a missing member is logged and leaves that part out.
    /// </summary>
    public static void Apply(ModContext context)
    {
        var panel = AccessTools.TypeByName("Meitou.Game.Ui.OrdersPanel");
        var rowText = panel is null ? null : AccessTools.Method(panel, "JobRowText");
        if (rowText is null) context.LogError("the jobs panel was not found: the Executing job shows as StealthKill");
        else
            context.Harmony.Patch(rowText, postfix: new HarmonyMethod(typeof(JobPatches), nameof(RowText)));

        var write = AccessTools.Method(typeof(SaveCapture), "WriteGameState");
        var read = AccessTools.Method(typeof(PermaJobSave), nameof(PermaJobSave.Read));
        var saveCharacter = write?.GetParameters().FirstOrDefault(p => p.Name == "sc")?.ParameterType;
        s_ai = saveCharacter is null ? null : (MemberInfo?)AccessTools.Property(saveCharacter, "Ai") ?? AccessTools.Field(saveCharacter, "Ai");
        if (write is null || read is null || s_ai is null)
        {
            context.LogError("the save's job writer was not found: the Executing job is not saved");
            return;
        }

        context.Harmony.Patch(write, prefix: new HarmonyMethod(typeof(JobPatches), nameof(BeforeWrite)),
            postfix: new HarmonyMethod(typeof(JobPatches), nameof(AfterWrite)));
        context.Harmony.Patch(read, postfix: new HarmonyMethod(typeof(JobPatches), nameof(AfterRead)));
    }

    /// <summary>
    /// The job's row in the jobs panel reads "Executing".
    /// </summary>
    static void RowText(PermaJob job, ref string __result)
    {
        if (job.Type == ExecutionSystem.Row) __result = JobName;
    }

    /// <summary>
    /// Takes the job out of the character's list while the game writes it; <paramref name="__state"/> keeps its place (-1: none).
    /// </summary>
    static void BeforeWrite(CharacterCold cold, out int __state)
    {
        __state = cold.Jobs.FindIndex(j => j.Type == ExecutionSystem.Row);
        if (__state >= 0) cold.Jobs.RemoveAll(j => j.Type == ExecutionSystem.Row);
    }

    /// <summary>
    /// Puts the job back and writes its place under <see cref="SaveKey"/> (cleared first, so a removed job leaves no stale key).
    /// </summary>
    static void AfterWrite(object sc, CharacterCold cold, bool orders, int __state)
    {
        if (__state >= 0) cold.Jobs.Insert(Math.Min(__state, cold.Jobs.Count), PermaJob.Untargeted(ExecutionSystem.Row));

        var ai = s_ai switch
        {
            PropertyInfo p => p.GetValue(sc),
            FieldInfo f => f.GetValue(sc),
            _ => null,
        } as FcsRecord;
        if (ai is null) return;
        ai.Ints.Remove(SaveKey);
        if (orders && __state >= 0) ai.Ints[SaveKey] = __state;
    }

    /// <summary>
    /// After the game reads a character's jobs: puts the Executing job back in its place.
    /// </summary>
    static void AfterRead(FcsRecord ai, CharacterCold cold)
    {
        if (!ai.Ints.TryGetValue(SaveKey, out int place) || cold.Jobs.Exists(j => j.Type == ExecutionSystem.Row)) return;
        cold.Jobs.Insert(Math.Clamp(place, 0, cold.Jobs.Count), PermaJob.Untargeted(ExecutionSystem.Row));
    }
}
