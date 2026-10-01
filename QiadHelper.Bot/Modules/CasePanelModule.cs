using Discord;
using Discord.Interactions;
using QiadHelper.Bot.Services;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Bot.Modules;

/// <summary>
///     Appellant-facing intake (design §7.3): the persistent panel button opens a modal; a
///     submitted modal creates the case (no number yet) and the consumer takes it from there
///     (validation-queue post + confirmation DM; the review thread only exists once claimed).
/// </summary>
public class CasePanelModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly ICaseService _caseService;
    private readonly CaseDiscordService _discordService;
    private readonly IRobloxIdentityResolver _identityResolver;

    public CasePanelModule(ICaseService caseService,
                           CaseDiscordService discordService,
                           IRobloxIdentityResolver identityResolver)
    {
        _caseService = caseService;
        _discordService = discordService;
        _identityResolver = identityResolver;
    }

    /// <summary>Kind segment in the panel custom IDs: a normal appeal or an unfair appeal.</summary>
    public const string KindAppeal = "appeal";

    public const string KindUnfair = "unfair";

    [ComponentInteraction("qiad-open:*:*", true)]
    public Task OnOpenClickedAsync(string categoryId, string kind) =>
        RespondWithModalAsync<AppealModal>($"qiad-appeal:{categoryId}:{kind}");

    [ModalInteraction("qiad-appeal:*:*", true)]
    public async Task OnAppealSubmittedAsync(string categoryId, string kind, AppealModal modal)
    {
        await DeferAsync(true);

        if (!Guid.TryParse(categoryId, out var categoryGuid))
        {
            await FollowupAsync("This panel is broken (bad category id). Please tell staff.", ephemeral: true);

            return;
        }

        var isUnfair = kind == KindUnfair;

        try
        {
            // Verified identity via Guardsman when available; the typed username stays as fallback.
            var identity = await _identityResolver.ResolveAsync(Context.User.Id);

            await _caseService.SubmitAsync(new SubmitCaseRequest(
                categoryGuid,
                Context.User.Id,
                Context.User.Username,
                modal.AppealText,
                SubjectRobloxUsername: identity?.Username ?? modal.RobloxUsername,
                ReportedBanReason: modal.BanReason,
                SubjectRobloxUserId: identity?.RobloxUserId,
                IsUnfair: isUnfair), CancellationToken.None);

            await FollowupAsync(
                (isUnfair
                    ? "✅ Your **unfair appeal** has been received."
                    : "✅ Your submission has been received. ") +
                "**Check your DMs**: the bot keeps you informed there. " +
                "(If you have DMs disabled for this server, please enable them.)",
                ephemeral: true);
        }
        catch (InvalidOperationException ex)
        {
            await FollowupAsync($"Your submission could not be filed: {ex.Message}", ephemeral: true);
        }
    }

    [SlashCommand("qiad-sync-panels", "Re-posts/refreshes the intake panel messages for all active categories.")]
    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    public async Task SyncPanelsAsync()
    {
        await _discordService.SyncCategoryPanelsAsync(CancellationToken.None);
        await FollowupAsync("Panels synced.", ephemeral: true);
    }
}

public class AppealModal : IModal
{
    public string Title => "File your submission";

    [InputLabel("Your Roblox username")]
    [ModalTextInput("roblox_username", TextInputStyle.Short, "e.g. Builderman", maxLength: 64)]
    public string RobloxUsername { get; set; } = string.Empty;

    [InputLabel("Reported punishment/ban reason")]
    [RequiredInput(false)]
    [ModalTextInput("ban_reason", TextInputStyle.Short, "What reason were you given? (optional)", maxLength: 500)]
    public string? BanReason { get; set; }

    [InputLabel("Your appeal / statement")]
    [ModalTextInput("appeal_text", TextInputStyle.Paragraph,
        "Explain your case. Evidence can be attached afterwards via DM.", maxLength: 3500)]
    public string AppealText { get; set; } = string.Empty;
}
