using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Core.Notifications;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Core.Services.Restrictions;

public sealed class RestrictionService : IRestrictionService
{
    private const QiadRank ExecutorMinRank = QiadRank.ProbationaryAffairs;

    private readonly QiadContext _context;
    private readonly ILogger<RestrictionService> _logger;
    private readonly IQiadNotificationPublisher _publisher;

    public RestrictionService(QiadContext context,
                              IQiadNotificationPublisher publisher,
                              ILogger<RestrictionService> logger)
    {
        _context = context;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<RestrictionActionResult> ApplyAsync(ApplyRestrictionRequest request,
                                                          StaffActor actor,
                                                          CancellationToken ct = default)
    {
        if (actor.Rank < ExecutorMinRank)
        {
            return RestrictionActionResult.Fail("You must be QIAD staff (PA+) to apply restrictions.");
        }

        // Only whitelisted roles may be managed as restrictions (design §6).
        var roleConfig = await _context.RestrictionRoleConfigs.AsNoTracking()
            .FirstOrDefaultAsync(r => r.GuildId == request.GuildId &&
                                      r.RoleId == request.RoleId &&
                                      r.IsActive, ct);
        if (roleConfig == null)
        {
            return RestrictionActionResult.Fail(
                "That role is not on the restriction-role whitelist. An admin can add it in the panel.");
        }

        var existing = await _context.Restrictions.AnyAsync(r =>
            r.TargetDiscordUserId == request.TargetDiscordUserId &&
            r.RoleId == request.RoleId &&
            r.Status == RestrictionStatus.Active, ct);
        if (existing)
        {
            return RestrictionActionResult.Fail("The user already has an active restriction with that role.");
        }

        // Duration: explicit > whitelist default (+ per-attempt escalation) > permanent.
        var duration = request.Duration;
        if (duration == null && roleConfig.DefaultDuration != null)
        {
            duration = roleConfig.DefaultDuration;
            if (roleConfig.PerAttemptIncrement != null && request.AttemptNumber is > 1)
            {
                duration += roleConfig.PerAttemptIncrement.Value * (request.AttemptNumber.Value - 1);
            }
        }

        var restriction = new Restriction
                          {
                              CaseId = request.CaseId,
                              TargetDiscordUserId = request.TargetDiscordUserId,
                              TargetDiscordUsername = request.TargetDiscordUsername,
                              TargetRobloxUserId = request.TargetRobloxUserId,
                              GuildId = request.GuildId,
                              RoleId = request.RoleId,
                              Reason = request.Reason,
                              AppliedById = actor.DiscordUserId,
                              AppliedByUsername = actor.Username,
                              AppliedAt = DateTime.UtcNow,
                              ExpiresAt = duration == null ? null : DateTime.UtcNow + duration,
                              AttemptNumber = request.AttemptNumber,
                              Status = RestrictionStatus.Active,
                              CreatedById = actor.DiscordUserId
                          };

        _context.Restrictions.Add(restriction);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Restriction {RestrictionId} recorded for {Target} (role {RoleId}, expires {ExpiresAt})",
            restriction.Id, request.TargetDiscordUserId, request.RoleId,
            restriction.ExpiresAt?.ToString("u") ?? "never");

        await _publisher.PublishAsync(new RestrictionAppliedNotification(restriction.Id), ct);

        return RestrictionActionResult.Ok(restriction);
    }

    public async Task<RestrictionActionResult> LiftAsync(Guid restrictionId,
                                                         StaffActor actor,
                                                         CancellationToken ct = default)
    {
        if (actor.Rank < ExecutorMinRank)
        {
            return RestrictionActionResult.Fail("You must be QIAD staff (PA+) to lift restrictions.");
        }

        var restriction = await _context.Restrictions.FirstOrDefaultAsync(r => r.Id == restrictionId, ct);
        if (restriction == null)
        {
            return RestrictionActionResult.Fail("Restriction not found.");
        }

        if (restriction.Status != RestrictionStatus.Active)
        {
            return RestrictionActionResult.Fail($"Restriction is not active (it is {restriction.Status}).");
        }

        restriction.Status = RestrictionStatus.RemovedEarly;
        restriction.RemovedById = actor.DiscordUserId;
        restriction.RemovedByUsername = actor.Username;
        restriction.RemovedAt = DateTime.UtcNow;
        restriction.ModifiedById = actor.DiscordUserId;

        await _context.SaveChangesAsync(ct);

        await _publisher.PublishAsync(new RestrictionLiftedNotification(restriction.Id, WasExpiry: false), ct);

        return RestrictionActionResult.Ok(restriction);
    }

    public async Task<IReadOnlyList<Restriction>> GetActiveForUserAsync(ulong discordUserId,
                                                                        CancellationToken ct = default)
    {
        return await _context.Restrictions.AsNoTracking()
            .Where(r => r.TargetDiscordUserId == discordUserId && r.Status == RestrictionStatus.Active)
            .OrderBy(r => r.AppliedAt)
            .ToListAsync(ct);
    }
}
