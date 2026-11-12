using Meitou.Modding;

namespace Execution;

/// <summary>
/// The mod's settings, on the Settings screen's Mods page.
/// </summary>
public sealed class ExecutionSettings
{
    /// <summary>
    /// The choices of <see cref="JobTargets"/>.
    /// </summary>
    public const string HostilesOnly = "Hostiles only", HostilesAndStrangers = "Hostiles and strangers";

    /// <summary>
    /// Whom the Executing job goes after: only characters hostile to the squad, or also downed strangers (never the squad's own faction or its
    /// allies). The manual Execute order is not limited by this.
    /// </summary>
    [Setting("Executing job targets",
        Hint = "Whom characters with the Executing job finish off. Strangers' factions turn against you as they would for any killing.",
        Options = [HostilesOnly, HostilesAndStrangers])]
    public string JobTargets { get; set; } = HostilesOnly;
}
