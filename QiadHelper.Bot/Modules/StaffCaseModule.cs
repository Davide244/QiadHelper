using Discord;
using Discord.Interactions;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Bot.Services;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Services;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Bot.Modules;

/// <summary>
///     Staff-created "dummy" cases: the matter is handled outside the system (a DM to an IA, an
///     in-person request), and this entry exists so the public database is complete. The case is
///     numbered immediately (public "Pending", claimed by the filer), nobody is DM'd, and the
///     normal thread buttons conclude it: verdict + ratification, void, seal.
/// </summary>
public class StaffCaseModule : InteractionModuleBase<SocketInteractionContext>
{
    private const QiadRank MinRank = QiadRank.ProbationaryAffairs;

    private readonly ICaseService _caseService;
    private readonly QiadContext _context;
    private readonly IRobloxIdentityResolver _identityResolver;
    private readonly IRankResolver _rankResolver;

    public StaffCaseModule(ICaseService caseService,
                           QiadContext context,
                           IRankResolver rankResolver,
                           IRobloxIdentityResolver identityResolver)
    {
        _caseService = caseService;
        _context = context;
        _rankResolver = rankResolver;
        _identityResolver = identityResolver;
    }

    [SlashCommand("open-case", "Create a tracking (dummy) case for a matter handled outside the system. Staff only.")]
    public async Task OpenCaseAsync(
        [Summary(description: "The Discord user the case concerns, if known (no DMs are sent)")] IUser? user = null)
    {
        if (await _rankResolver.ResolveRankAsync(Context.User.Id) < MinRank)
        {
            await FollowupAsync("You must be QIAD staff (PA+) to create tracking cases.", ephemeral: true);

            return;
        }

        if (user?.IsBot == true)
        {
            await FollowupAsync("Bots cannot be case subjects.", ephemeral: true);

            return;
        }

        // Staff may file into ANY active category, including staff-only ones like audits.
        var categories = await _context.CaseCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Take(25)
            .ToListAsync();

        if (categories.Count == 0)
        {
            await FollowupAsync("No active case categories are configured.", ephemeral: true);

            return;
        }

        var menu = new SelectMenuBuilder()
            .WithCustomId($"qiad-staff-pick:{user?.Id ?? 0}")
            .WithPlaceholder("Which category does this case belong to?");
        foreach (var category in categories)
        {
            menu.AddOption(category.Name, $"{category.Id}:{CasePanelModule.KindAppeal}",
                category.NumberSeries == CaseNumberSeries.Incidents ? "incident series" : null);
            if (category.OfferUnfairAppeals)
            {
                menu.AddOption($"Unfair {category.Name}", $"{category.Id}:{CasePanelModule.KindUnfair}",
                    "contests the punishment as unjust");
            }
        }

        await FollowupAsync(
            $"Tracking case{(user != null ? $" concerning {user.Mention}" : "")}. Pick the category:",
            components: new ComponentBuilder().WithSelectMenu(menu).Build(),
            ephemeral: true);
    }

    [ComponentInteraction("qiad-staff-pick:*", true)]
    public Task OnCategoryPickedAsync(string appellantId, string[] selections) =>
        // selections[0] is "{categoryId}:{kind}", so the modal id ends up with three segments.
        RespondWithModalAsync<StaffOpenCaseModal>($"qiad-staff-modal:{selections[0]}:{appellantId}");

    [ModalInteraction("qiad-staff-modal:*:*:*", true)]
    public async Task OnStaffCaseSubmittedAsync(string categoryId,
                                                string kind,
                                                string appellantId,
                                                StaffOpenCaseModal modal)
    {
        await DeferAsync(true);

        // The select/modal round trip is unauthenticated; re-check the gate on the final step.
        if (await _rankResolver.ResolveRankAsync(Context.User.Id) < MinRank)
        {
            await FollowupAsync("You must be QIAD staff (PA+) to create tracking cases.", ephemeral: true);

            return;
        }

        if (!Guid.TryParse(categoryId, out var categoryGuid) ||
            !ulong.TryParse(appellantId, System.Globalization.CultureInfo.InvariantCulture, out var appellantUserId))
        {
            await FollowupAsync("Something went stale in this dialog. Run /open-case again.", ephemeral: true);

            return;
        }

        IUser? appellant = null;
        RobloxIdentity? identity = null;
        if (appellantUserId != 0)
        {
            appellant = Context.Client.GetUser(appellantUserId)
                        ?? (IUser?)await Context.Client.Rest.GetUserAsync(appellantUserId);
            identity = await _identityResolver.ResolveAsync(appellantUserId);
        }

        try
        {
            var created = await _caseService.SubmitAsync(new SubmitCaseRequest(
                categoryGuid,
                appellantUserId,
                appellant?.Username,
                modal.Details,
                SubjectRobloxUsername: identity?.Username ??
                                       (string.IsNullOrWhiteSpace(modal.RobloxUsername) ? null : modal.RobloxUsername),
                ReportedBanReason: modal.BanReason,
                SubjectRobloxUserId: identity?.RobloxUserId,
                FiledById: Context.User.Id,
                FiledByUsername: Context.User.Username,
                IsTracking: true,
                IsUnfair: kind == CasePanelModule.KindUnfair,
                ExternalEvidenceUrl: modal.EvidenceLink,
                RequestContext: modal.RequestContext), CancellationToken.None);

            var reference = created.NumberSeries == CaseNumberSeries.Incidents
                ? $"Incident #{created.CaseNumber}"
                : $"Case #{created.CaseNumber}";

            await FollowupAsync(
                $"📄 Recorded as **{reference}**; the review thread is being created.",
                ephemeral: true);
        }
        catch (InvalidOperationException ex)
        {
            await FollowupAsync($"Could not create the case: {ex.Message}", ephemeral: true);
        }
    }
}

public class StaffOpenCaseModal : IModal
{
    public string Title => "Create a tracking case";

    [InputLabel("Subject's Roblox username (if known)")]
    [RequiredInput(false)]
    [ModalTextInput("roblox_username", TextInputStyle.Short, "Leave empty if unknown or verified", maxLength: 64)]
    public string? RobloxUsername { get; set; }

    [InputLabel("Reported punishment/ban reason")]
    [RequiredInput(false)]
    [ModalTextInput("ban_reason", TextInputStyle.Short, "What punishment is this about? (optional)", maxLength: 500)]
    public string? BanReason { get; set; }

    [InputLabel("Case details")]
    [ModalTextInput("details", TextInputStyle.Paragraph,
        "What was reported/appealed, as received (e.g. from the DM to the IA).", maxLength: 3500)]
    public string Details { get; set; } = string.Empty;

    // The old misc-appeals forum format: "Evidence Link" + "Date Of Request" / "Request Location".
    [InputLabel("Evidence link (optional)")]
    [RequiredInput(false)]
    [ModalTextInput("evidence_link", TextInputStyle.Short, "URL to logs/screenshots, if any", maxLength: 500)]
    public string? EvidenceLink { get; set; }

    [InputLabel("Request date & location (optional)")]
    [RequiredInput(false)]
    [ModalTextInput("request_context", TextInputStyle.Short, "e.g. 30.05.2026 via DMs to an IA", maxLength: 200)]
    public string? RequestContext { get; set; }
}
