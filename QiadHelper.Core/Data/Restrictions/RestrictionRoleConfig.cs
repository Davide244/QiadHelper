using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data.Restrictions;

/// <summary>
///     Admin whitelist of Discord roles the bot may manage as restrictions (design §6).
///     The bot refuses to apply any role not listed and active here.
/// </summary>
public class RestrictionRoleConfig : QiadDataAbstract
{
    public ulong GuildId { get; set; }

    public ulong RoleId { get; set; }

    /// <summary>Friendly name shown in pickers (e.g. "Appeal Restriction").</summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)] public string? Description { get; set; }

    /// <summary>Default duration when staff don't override. Null = permanent by default.</summary>
    public TimeSpan? DefaultDuration { get; set; }

    /// <summary>
    ///     Added per prior attempt: effective default = DefaultDuration + (attempt-1) × this.
    ///     Implements the "more prior attempts → longer restriction" reference rule.
    /// </summary>
    public TimeSpan? PerAttemptIncrement { get; set; }

    public bool IsActive { get; set; } = true;
}
