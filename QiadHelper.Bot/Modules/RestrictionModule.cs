using Discord;
using Discord.Interactions;
using QiadHelper.Bot.Services;
using QiadHelper.Core.Services;
using QiadHelper.Core.Services.Cases;
using QiadHelper.Core.Services.Restrictions;

namespace QiadHelper.Bot.Modules;

/// <summary>
///     Standalone restriction commands (design §7.6) — restrictions can exist without a case.
///     Replaces `?role5ever [Username] [Role]` + the separate reminder bot in one command.
/// </summary>
public class RestrictionModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IRobloxIdentityResolver _identityResolver;
    private readonly IRankResolver _rankResolver;
    private readonly IRestrictionService _restrictionService;

    public RestrictionModule(IRestrictionService restrictionService,
                             IRankResolver rankResolver,
                             IRobloxIdentityResolver identityResolver)
    {
        _restrictionService = restrictionService;
        _rankResolver = rankResolver;
        _identityResolver = identityResolver;
    }

    [SlashCommand("restrict", "Apply a whitelisted restriction role, with optional auto-expiry.")]
    public async Task RestrictAsync(
        [Summary(description: "Who to restrict")] IUser user,
        [Summary(description: "The restriction role (must be whitelisted)")] IRole role,
        [Summary(description: "Duration: 12h, 7d, 4w, 3mo, perm; empty uses the role's default")] string? duration = null,
        [Summary(description: "Why")] string? reason = null,
        [Summary(description: "Appeal attempt number (drives escalating default durations)")] int? attempt = null)
    {
        if (!DurationParsing.TryParse(duration, out var parsed))
        {
            await FollowupAsync("I couldn't parse that duration. Use `12h`, `7d`, `4w`, `3mo`, or `perm`.",
                ephemeral: true);

            return;
        }

        var actor = await BuildActorAsync();
        var identity = await _identityResolver.ResolveAsync(user.Id);
        var result = await _restrictionService.ApplyAsync(new ApplyRestrictionRequest(
            user.Id,
            user.Username,
            Context.Guild.Id,
            role.Id,
            reason ?? "No reason given.",
            parsed,
            AttemptNumber: attempt,
            TargetRobloxUserId: identity?.RobloxUserId), actor);

        if (!result.Success)
        {
            await FollowupAsync($"❌ {result.Error}", ephemeral: true);

            return;
        }

        var r = result.Restriction!;
        await FollowupAsync(
            $"⛔ Restriction recorded for {user.Mention} with {role.Mention}: " +
            (r.ExpiresAt == null
                ? "**permanent** until lifted."
                : $"expires <t:{new DateTimeOffset(r.ExpiresAt.Value).ToUnixTimeSeconds()}:R> (auto-removed, no reminder needed).") +
            " The role is being applied and the user informed.",
            ephemeral: true);
    }

    [SlashCommand("unrestrict", "Lift a user's active restriction(s) early.")]
    public async Task UnrestrictAsync(
        [Summary(description: "Whose restriction to lift")] IUser user,
        [Summary(description: "Only lift this role (empty lifts all active restrictions)")] IRole? role = null)
    {
        var actor = await BuildActorAsync();
        var active = await _restrictionService.GetActiveForUserAsync(user.Id);
        var targets = active.Where(r => role == null || r.RoleId == role.Id).ToList();

        if (targets.Count == 0)
        {
            await FollowupAsync($"{user.Mention} has no matching active restriction.", ephemeral: true);

            return;
        }

        var lifted = 0;
        var errors = new List<string>();
        foreach (var restriction in targets)
        {
            var result = await _restrictionService.LiftAsync(restriction.Id, actor);
            if (result.Success)
            {
                lifted++;
            }
            else
            {
                errors.Add(result.Error!);
            }
        }

        var summary = $"✅ Lifted {lifted} restriction(s) for {user.Mention}.";
        if (errors.Count > 0)
        {
            summary += $"\n❌ {string.Join("\n❌ ", errors)}";
        }

        await FollowupAsync(summary, ephemeral: true);
    }

    [SlashCommand("restrictions", "Show a user's active restrictions (feeds the validation checklist).")]
    public async Task RestrictionsAsync([Summary(description: "Who to look up")] IUser user)
    {
        var active = await _restrictionService.GetActiveForUserAsync(user.Id);
        if (active.Count == 0)
        {
            await FollowupAsync($"{user.Mention} has **no active restrictions**.", ephemeral: true);

            return;
        }

        var embed = new EmbedBuilder()
            .WithTitle($"⛔ Active restrictions: {user.Username}")
            .WithColor(Color.Red)
            .WithTimestamp(DateTimeOffset.UtcNow);

        foreach (var r in active.Take(25))
        {
            var expiry = r.ExpiresAt == null
                ? "permanent"
                : $"expires <t:{new DateTimeOffset(r.ExpiresAt.Value).ToUnixTimeSeconds()}:R>";
            embed.AddField($"<t:{new DateTimeOffset(r.AppliedAt).ToUnixTimeSeconds()}:d> · role <@&{r.RoleId}>",
                $"{r.Reason}\n*Applied by {r.AppliedByUsername}, {expiry}*");
        }

        await FollowupAsync(embed: embed.Build(), ephemeral: true);
    }

    private async Task<StaffActor> BuildActorAsync() =>
        new(Context.User.Id, Context.User.Username, await _rankResolver.ResolveRankAsync(Context.User.Id));
}
