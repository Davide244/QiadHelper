using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using QiadHelper.Core.Data.Cases;

namespace QiadHelper.Core.Data.Restrictions;

/// <summary>
///     A managed, optionally time-limited Discord restriction-role assignment (design §7.6).
///     Replaces the old two-bot dance (`?role5ever` + a separate reminder bot): the QIAD bot
///     applies the role, and <c>RestrictionExpiryService</c> removes it at <see cref="ExpiresAt" />.
/// </summary>
public class Restriction : QiadDataAbstract
{
    /// <summary>Case whose execution applied this restriction; null for standalone restrictions.</summary>
    public Guid? CaseId { get; set; }

    public ulong TargetDiscordUserId { get; set; }

    [MaxLength(64)] public string? TargetDiscordUsername { get; set; }

    public long? TargetRobloxUserId { get; set; }

    /// <summary>Guild + role that was applied. Must come from the admin whitelist.</summary>
    public ulong GuildId { get; set; }

    public ulong RoleId { get; set; }

    [Required] [MaxLength(1000)] public string Reason { get; set; } = string.Empty;

    public ulong AppliedById { get; set; }

    [MaxLength(64)] public string? AppliedByUsername { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null = permanent ("5ever"). Set = auto-removed at that time by the expiry service.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Which appeal attempt this is for the subject; drives the default duration.</summary>
    public int? AttemptNumber { get; set; }

    public RestrictionStatus Status { get; set; } = RestrictionStatus.Active;

    public ulong? RemovedById { get; set; }

    [MaxLength(64)] public string? RemovedByUsername { get; set; }

    public DateTime? RemovedAt { get; set; }

    // ---- Navigation ----

    [JsonIgnore] public virtual Case? Case { get; set; }
}

public enum RestrictionStatus
{
    Active = 0,

    /// <summary>Auto-removed at <see cref="Restriction.ExpiresAt" /> by the expiry service.</summary>
    Expired = 1,

    /// <summary>Lifted by staff before expiry.</summary>
    RemovedEarly = 2
}
