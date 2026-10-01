using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Services;
using QiadHelper.Core.Services.Cases;
using QiadHelper.Core.Services.Restrictions;

namespace QiadHelper.Bot.Modules;

/// <summary>
///     Staff buttons for the case workflow (design §5/§7.4). They live on the validation-queue
///     message (claim/void/misc, pre-thread) and on the review-thread starter post; handlers act
///     by case ID, so where the button sits does not matter. Every handler builds a
///     <see cref="StaffActor" /> from the clicker's resolved rank and lets the shared service
///     enforce the gate — a reviewer physically cannot perform an action above their tier.
///     Public updates come from the notification consumer; responses here are ephemeral.
/// </summary>
public class CaseThreadModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ICaseService _caseService;
    private readonly QiadContext _context;
    private readonly IRankResolver _rankResolver;
    private readonly IRestrictionService _restrictionService;

    public CaseThreadModule(ICaseService caseService,
                            IRestrictionService restrictionService,
                            IRankResolver rankResolver,
                            QiadContext context)
    {
        _caseService = caseService;
        _restrictionService = restrictionService;
        _rankResolver = rankResolver;
        _context = context;
    }

    // ---- Claim (validation passed → number minted, public "Pending") ----

    [ComponentInteraction("qiad-claim:*", true)]
    public async Task OnClaimAsync(string caseId)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.ClaimAsync(Guid.Parse(caseId), actor);
        await FollowupResultAsync(result,
            ok => $"📌 Claimed as **{Ref(ok)}**; the review thread is being created.");
    }

    // ---- Decision: pick verdict → internal reasoning modal → AwaitingRatification ----

    [ComponentInteraction("qiad-verdict:*", true)]
    public async Task OnVerdictClickedAsync(string caseId)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"qiad-verdict-pick:{caseId}")
            .WithPlaceholder("Select your decision…")
            .AddOption("Accepted", nameof(CaseVerdict.Accepted), "Appeal should be granted", new Emoji("🟢"))
            .AddOption("Rejected", nameof(CaseVerdict.Rejected), "Appeal should be denied", new Emoji("🔴"))
            .AddOption("Other", nameof(CaseVerdict.Other), "Partial / special outcome", new Emoji("⚪"));

        await RespondAsync("Pick your decision:",
            components: new ComponentBuilder().WithSelectMenu(menu).Build(),
            ephemeral: true);
    }

    [ComponentInteraction("qiad-verdict-pick:*", true)]
    public Task OnVerdictPickedAsync(string caseId, string[] selections) =>
        RespondWithModalAsync<VerdictModal>($"qiad-verdict-modal:{caseId}:{selections[0]}");

    [ModalInteraction("qiad-verdict-modal:*:*", true)]
    public async Task OnVerdictSubmittedAsync(string caseId, string verdict, VerdictModal modal)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.DraftVerdictAsync(
            Guid.Parse(caseId), actor, Enum.Parse<CaseVerdict>(verdict), modal.Reasoning);
        await FollowupResultAsync(result,
            ok => $"⚖️ Decision **{ok.Verdict}** filed for {Ref(ok)}; ratifiers pinged.");
    }

    // ---- Ratification (Secretary+ by default, per-category configurable) ----

    [ComponentInteraction("qiad-ratify:*:*", true)]
    public async Task OnRatifyAsync(string caseId, string decision)
    {
        if (decision == "uphold")
        {
            // Upholding needs the ratifier's own words: the public verdict text (the modal must
            // be the interaction's first response, so no defer here).
            await RespondWithModalAsync<RatifyModal>($"qiad-ratify-modal:{caseId}");

            return;
        }

        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.RatifyAsync(Guid.Parse(caseId), actor, false, null);
        await FollowupResultAsync(result,
            ok => $"↩️ Overturned; {Ref(ok)} is back under review.");
    }

    [ModalInteraction("qiad-ratify-modal:*", true)]
    public async Task OnRatifySubmittedAsync(string caseId, RatifyModal modal)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.RatifyAsync(Guid.Parse(caseId), actor, true, modal.PublicVerdictText);
        await FollowupResultAsync(result,
            ok => $"✅ {Ref(ok)} upheld; the claimer has been pinged to execute the decision.");
    }

    // ---- Void path (invalid submission; IA+ ratifies) ----

    [ComponentInteraction("qiad-void:*", true)]
    public async Task OnVoidClickedAsync(string caseId)
    {
        // The categorized reasons are the old #voiding-requests forum tags.
        var menu = new SelectMenuBuilder()
            .WithCustomId($"qiad-void-pick:{caseId}")
            .WithPlaceholder("Why should this submission be voided?")
            .AddOption("Cooldown", nameof(VoidReason.Cooldown), "The appeal cooldown was violated")
            .AddOption("Max attempts", nameof(VoidReason.MaxAttempts), "Appeal attempts are exhausted")
            .AddOption("Not an appeal", nameof(VoidReason.NotAnAppeal), "The submission is not an appeal")
            .AddOption("Non-QIAD matter", nameof(VoidReason.NonQiadMatter), "Outside QIAD's jurisdiction")
            .AddOption("No active ban", nameof(VoidReason.NoActiveBan), "No active punishment on record")
            .AddOption("Invalid category", nameof(VoidReason.InvalidCategory),
                "Wrong category (e.g. temporary punishments belong to unfair)")
            .AddOption("Duplicate", nameof(VoidReason.Duplicate), "Duplicate of another submission")
            .AddOption("Other", nameof(VoidReason.Other), "None of the tags fit; explain in the reasoning");

        await RespondAsync("Pick the void reason:",
            components: new ComponentBuilder().WithSelectMenu(menu).Build(),
            ephemeral: true);
    }

    [ComponentInteraction("qiad-void-pick:*", true)]
    public Task OnVoidReasonPickedAsync(string caseId, string[] selections) =>
        RespondWithModalAsync<VoidModal>($"qiad-void-modal:{caseId}:{selections[0]}");

    [ModalInteraction("qiad-void-modal:*:*", true)]
    public async Task OnVoidSubmittedAsync(string caseId, string reason, VoidModal modal)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.RequestVoidAsync(
            Guid.Parse(caseId), actor, Enum.Parse<VoidReason>(reason), modal.Reasoning);
        await FollowupResultAsync(result,
            ok => $"🗑️ Void request ({Enum.Parse<VoidReason>(reason).DisplayName()}) filed for {Ref(ok)}.");
    }

    [ComponentInteraction("qiad-void-ratify:*:*", true)]
    public async Task OnVoidRatifyAsync(string caseId, string decision)
    {
        if (decision == "approve")
        {
            // The ratifier authors the reason the appellant reads (modal = first response).
            await RespondWithModalAsync<VoidApproveModal>($"qiad-void-approve-modal:{caseId}");

            return;
        }

        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.RatifyVoidAsync(Guid.Parse(caseId), actor, false, null);
        await FollowupResultAsync(result, ok => $"↩️ Void request rejected; {Ref(ok)} continues.");
    }

    [ModalInteraction("qiad-void-approve-modal:*", true)]
    public async Task OnVoidApproveSubmittedAsync(string caseId, VoidApproveModal modal)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.RatifyVoidAsync(Guid.Parse(caseId), actor, true, modal.DmReason);
        await FollowupResultAsync(result, ok => $"🗑️ {Ref(ok)} voided; the appellant has been informed.");
    }

    // ---- Execution (upheld decision → claimer closes the case) ----

    [ComponentInteraction("qiad-execute:*", true)]
    public async Task OnExecuteAsync(string caseId)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.ExecuteAsync(Guid.Parse(caseId), actor);
        await FollowupResultAsync(result, ok => $"✅ {Ref(ok)} executed and closed.");
    }

    [ComponentInteraction("qiad-misc-close:*", true)]
    public Task OnMiscCloseClickedAsync(string caseId) =>
        RespondWithModalAsync<MiscCloseModal>($"qiad-misc-close-modal:{caseId}");

    [ModalInteraction("qiad-misc-close-modal:*", true)]
    public async Task OnMiscCloseSubmittedAsync(string caseId, MiscCloseModal modal)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.CloseMiscAsync(Guid.Parse(caseId), actor, modal.PublicReasoning);
        await FollowupResultAsync(result, ok => $"✅ {Ref(ok)} closed.");
    }

    // ---- Misc / protocol-bypass path (no ratification) ----

    [ComponentInteraction("qiad-misc:*", true)]
    public async Task OnMiscClickedAsync(string caseId)
    {
        var menu = new SelectMenuBuilder()
            .WithCustomId($"qiad-misc-pick:{caseId}")
            .WithPlaceholder("Which protocol bypass applies?")
            .AddOption("Compromised account", nameof(MiscFilingType.CompromisedAccount))
            .AddOption("<24h-old account autoban", nameof(MiscFilingType.NewAccountAutoban))
            .AddOption("Punishment already expired/appealed", nameof(MiscFilingType.PunishmentAlreadyExpiredOrAppealed))
            .AddOption("Other", nameof(MiscFilingType.Other));

        await RespondAsync("Pick the filing type:",
            components: new ComponentBuilder().WithSelectMenu(menu).Build(),
            ephemeral: true);
    }

    [ComponentInteraction("qiad-misc-pick:*", true)]
    public Task OnMiscPickedAsync(string caseId, string[] selections) =>
        RespondWithModalAsync<MiscModal>($"qiad-misc-modal:{caseId}:{selections[0]}");

    [ModalInteraction("qiad-misc-modal:*:*", true)]
    public async Task OnMiscSubmittedAsync(string caseId, string filingType, MiscModal modal)
    {
        await DeferAsync(true);
        var actor = await BuildActorAsync();
        var result = await _caseService.FileMiscAsync(
            Guid.Parse(caseId), actor, Enum.Parse<MiscFilingType>(filingType), modal.Reasoning,
            modal.EvidenceLink);
        await FollowupResultAsync(result,
            ok => $"⚡ {Ref(ok)} filed as misc ({filingType}); close it from its forum post.");
    }

    // ---- Execution: restriction role from the case (design §7.6) ----

    [ComponentInteraction("qiad-restrict:*", true)]
    public async Task OnRestrictClickedAsync(string caseId)
    {
        var roles = await _context.RestrictionRoleConfigs.AsNoTracking()
            .Where(r => r.IsActive && r.GuildId == Context.Guild.Id)
            .OrderBy(r => r.Name)
            .Take(25)
            .ToListAsync();

        if (roles.Count == 0)
        {
            await RespondAsync(
                "No restriction roles are whitelisted for this guild yet; add them in the admin panel first.",
                ephemeral: true);

            return;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId($"qiad-restrict-pick:{caseId}")
            .WithPlaceholder("Which restriction role?");
        foreach (var role in roles)
        {
            menu.AddOption(role.Name, role.RoleId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                role.DefaultDuration == null ? "default: permanent" : $"default: {DurationParsing.Describe(role.DefaultDuration)}");
        }

        await RespondAsync("Pick the restriction role to apply to the case subject:",
            components: new ComponentBuilder().WithSelectMenu(menu).Build(),
            ephemeral: true);
    }

    [ComponentInteraction("qiad-restrict-pick:*", true)]
    public Task OnRestrictPickedAsync(string caseId, string[] selections) =>
        RespondWithModalAsync<RestrictModal>($"qiad-restrict-modal:{caseId}:{selections[0]}");

    [ModalInteraction("qiad-restrict-modal:*:*", true)]
    public async Task OnRestrictSubmittedAsync(string caseId, string roleId, RestrictModal modal)
    {
        await DeferAsync(true);

        if (!DurationParsing.TryParse(modal.Duration, out var duration))
        {
            await FollowupAsync(
                "I couldn't parse that duration. Use forms like `12h`, `7d`, `4w`, `3mo`, or `perm`. " +
                "Leave it empty for the role's default.", ephemeral: true);

            return;
        }

        var c = await _caseService.GetAsync(Guid.Parse(caseId));
        if (c?.SubjectDiscordUserId == null)
        {
            await FollowupAsync("This case has no Discord subject to restrict.", ephemeral: true);

            return;
        }

        var actor = await BuildActorAsync();
        var result = await _restrictionService.ApplyAsync(new ApplyRestrictionRequest(
            c.SubjectDiscordUserId.Value,
            c.SubjectRobloxUsername,
            Context.Guild.Id,
            ulong.Parse(roleId, System.Globalization.CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(modal.Reason) ? $"Execution of {Ref(c)}" : modal.Reason,
            duration,
            CaseId: c.Id,
            TargetRobloxUserId: c.SubjectRobloxUserId), actor);

        await FollowupAsync(result.Success
                ? $"⛔ Restriction recorded ({DurationParsing.Describe(result.Restriction!.ExpiresAt == null ? null : result.Restriction.ExpiresAt - DateTime.UtcNow)}); the role is being applied and the user informed."
                : $"❌ {result.Error}",
            ephemeral: true);
    }

    // ---- helpers ----

    private async Task<StaffActor> BuildActorAsync() =>
        new(Context.User.Id, Context.User.Username, await _rankResolver.ResolveRankAsync(Context.User.Id));

    private async Task FollowupResultAsync(CaseActionResult result, Func<Case, string> onSuccess)
    {
        await FollowupAsync(result.Success ? onSuccess(result.Case!) : $"❌ {result.Error}", ephemeral: true);
    }

    private static string Ref(Case c)
    {
        if (c.CaseNumber != null)
        {
            return c.NumberSeries == CaseNumberSeries.Incidents
                ? $"Incident #{c.CaseNumber}"
                : $"Case #{c.CaseNumber}";
        }

        return $"REF-{c.Id.ToString("N")[..8].ToUpperInvariant()}";
    }
}

public class VerdictModal : IModal
{
    public string Title => "File your decision (internal)";

    // Discord hard limit: input labels max 45 characters.
    [InputLabel("Decision reasoning (internal, not public)")]
    [ModalTextInput("reasoning", TextInputStyle.Paragraph,
        "Why this decision? Evidence, timestamps, rule references. Staff-only.", maxLength: 3500)]
    public string Reasoning { get; set; } = string.Empty;
}

public class RatifyModal : IModal
{
    public string Title => "Uphold: write the public verdict";

    [InputLabel("Public verdict text (for the case database)")]
    [ModalTextInput("public_verdict", TextInputStyle.Paragraph,
        "Summary for the public database, e.g. \"The appeal was rejected as the ban was fairly issued.\"",
        maxLength: 3500)]
    public string PublicVerdictText { get; set; } = string.Empty;
}

public class VoidModal : IModal
{
    public string Title => "Void request";

    [InputLabel("Why is this submission invalid?")]
    [ModalTextInput("void_reasoning", TextInputStyle.Paragraph,
        "Accurate and descriptive; the appellant will read this.",
        maxLength: 1800)]
    public string Reasoning { get; set; } = string.Empty;
}

public class MiscModal : IModal
{
    public string Title => "Misc / protocol-bypass filing";

    [InputLabel("Filing details (internal)")]
    [ModalTextInput("misc_reasoning", TextInputStyle.Paragraph,
        "What happened and what was done. The public reasoning is written at close.",
        maxLength: 3500)]
    public string Reasoning { get; set; } = string.Empty;

    [InputLabel("Evidence link (optional)")]
    [RequiredInput(false)]
    [ModalTextInput("evidence_link", TextInputStyle.Short,
        "URL to the log/screenshots backing this filing", maxLength: 500)]
    public string? EvidenceLink { get; set; }
}

public class MiscCloseModal : IModal
{
    public string Title => "Close misc filing";

    [InputLabel("Public reasoning (case database)")]
    [ModalTextInput("public_reasoning", TextInputStyle.Paragraph, maxLength: 3500)]
    public string PublicReasoning { get; set; } = string.Empty;
}

public class VoidApproveModal : IModal
{
    public string Title => "Approve void";

    [InputLabel("Reason shown to the appellant")]
    [ModalTextInput("dm_reason", TextInputStyle.Paragraph,
        "Accurate and descriptive; DM'd to the appellant and public for numbered cases.",
        maxLength: 1800)]
    public string DmReason { get; set; } = string.Empty;
}

public class RestrictModal : IModal
{
    public string Title => "Apply restriction";

    [InputLabel("Duration (12h, 7d, 4w, 3mo, perm)")]
    [RequiredInput(false)]
    [ModalTextInput("duration", TextInputStyle.Short, "e.g. 7d; leave empty for the role's default", maxLength: 10)]
    public string? Duration { get; set; }

    [InputLabel("Reason")]
    [RequiredInput(false)]
    [ModalTextInput("reason", TextInputStyle.Paragraph, "Defaults to the case reference.", maxLength: 900)]
    public string? Reason { get; set; }
}
