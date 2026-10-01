using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Core.Services.Restrictions;

public sealed record ApplyRestrictionRequest(
    ulong TargetDiscordUserId,
    string? TargetDiscordUsername,
    ulong GuildId,
    ulong RoleId,
    string Reason,
    TimeSpan? Duration,
    Guid? CaseId = null,
    int? AttemptNumber = null,
    long? TargetRobloxUserId = null);

public sealed record RestrictionActionResult(bool Success, string? Error, Restriction? Restriction)
{
    public static RestrictionActionResult Ok(Restriction r) => new(true, null, r);

    public static RestrictionActionResult Fail(string error) => new(false, error, null);
}

/// <summary>
///     Managed restriction roles (design §7.6). The service owns the rows; the Discord role
///     add/remove happens bot-side — applied via the notification consumer (single code path
///     whether the request came from a bot command or the admin UI), removed by the expiry
///     service or the lift flow.
/// </summary>
public interface IRestrictionService
{
    /// <summary>
    ///     Records a restriction and publishes <c>RestrictionAppliedNotification</c>. When
    ///     <paramref name="request" />.Duration is null, the whitelist default (plus per-attempt
    ///     increment) applies; if that is also null the restriction is permanent ("5ever").
    ///     The role must be on the active whitelist. PA+.
    /// </summary>
    Task<RestrictionActionResult> ApplyAsync(ApplyRestrictionRequest request,
                                             StaffActor actor,
                                             CancellationToken ct = default);

    /// <summary>Lifts an active restriction early and publishes the lift notification. PA+.</summary>
    Task<RestrictionActionResult> LiftAsync(Guid restrictionId, StaffActor actor, CancellationToken ct = default);

    /// <summary>Active restrictions for a user — feeds the validation step's "active punishment" check (§5).</summary>
    Task<IReadOnlyList<Restriction>> GetActiveForUserAsync(ulong discordUserId, CancellationToken ct = default);
}
