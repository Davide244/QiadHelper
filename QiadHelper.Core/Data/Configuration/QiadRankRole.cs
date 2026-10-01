using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data.Configuration;

/// <summary>
///     Maps a Discord role to a QIAD rank tier (design §11.1 — set in the admin UI).
///     Several roles may map to the same rank; a user's effective rank is the highest
///     mapped rank among their roles. Both the web claims transformation and the bot's
///     in-thread gates resolve rank through this table.
/// </summary>
public class QiadRankRole : QiadDataAbstract
{
    public ulong GuildId { get; set; }

    public ulong DiscordRoleId { get; set; }

    public QiadRank Rank { get; set; }

    /// <summary>Friendly label so admins recognise the row without resolving the role ID.</summary>
    [MaxLength(100)]
    public string? RoleName { get; set; }
}
