using Discord.Interactions;
using QiadHelper.Bot.Services;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Services;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Bot.Modules;

/// <summary>
///     The slash-command equivalent of the staff button workflow, for the live-QIAD command style
///     (<c>/add-decision</c>, <c>/issue-verdict</c>, <c>/reject-case</c>, <c>/claim-case</c>, …).
///     Registered only when <see cref="Configuration.StaffWorkflowMode" /> is Commands or Both
///     (see <see cref="Infrastructure.InteractionHandler" />).
///
///     These handlers are deliberately thin: they resolve the target case (from the thread the
///     command is run in, or the queue reference), then either call the shared
///     <see cref="ICaseService" /> or open the SAME modal the buttons use — reusing the existing
///     <c>qiad-*-modal</c> custom IDs, so the modal-submit handlers in
///     <see cref="CaseThreadModule" /> run unchanged. All rank/state gating lives in the service.
///
///     Direct-action commands are auto-deferred ephemerally (see the handler's
///     <c>_ephemeralDeferGroups</c>) and use <see cref="InteractionModuleBase{T}.FollowupAsync" />.
///     Modal-opening commands are exempt from auto-defer (<c>_modalOpeningSlashCommands</c>) and
///     own their first response (a modal, or an ephemeral error).
/// </summary>
public class WorkflowCommandModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string NotInThread =
        "Run this in the case's review thread (or void/misc post), or pass a valid reference.";

    private readonly ICaseService _caseService;
    private readonly IRankResolver _rankResolver;

    public WorkflowCommandModule(ICaseService caseService, IRankResolver rankResolver)
    {
        _caseService = caseService;
        _rankResolver = rankResolver;
    }

    // ---- Direct actions (auto-deferred ephemerally → FollowupAsync) ----

    [SlashCommand("claim-case", "Claim a submission from the validation queue by its reference.")]
    public async Task ClaimCaseAsync(
        [Summary("ref", "The case's REF-XXXXXXXX shown on the validation-queue post")] string reference)
    {
        var c = await _caseService.GetByReferenceAsync(reference);
        if (c == null)
        {
            await FollowupAsync("No unclaimed submission matches that reference.", ephemeral: true);

            return;
        }

        var result = await _caseService.ClaimAsync(c.Id, await BuildActorAsync());
        await FollowupResultAsync(result,
            ok => $"📌 Claimed as **{Ref(ok)}**; the review thread is being created.");
    }

    [SlashCommand("overturn", "Overturn the drafted decision. Run in the case's review thread.")]
    public async Task OverturnAsync()
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await FollowupAsync(NotInThread, ephemeral: true);

            return;
        }

        var result = await _caseService.RatifyAsync(c.Id, await BuildActorAsync(), false, null);
        await FollowupResultAsync(result, ok => $"↩️ Overturned; {Ref(ok)} is back under review.");
    }

    [SlashCommand("execute", "Execute an upheld decision to close the case. Run in its review thread.")]
    public async Task ExecuteAsync()
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await FollowupAsync(NotInThread, ephemeral: true);

            return;
        }

        var result = await _caseService.ExecuteAsync(c.Id, await BuildActorAsync());
        await FollowupResultAsync(result, ok => $"✅ {Ref(ok)} executed and closed.");
    }

    [SlashCommand("deny-void", "Reject the pending void request. Run in the void-request post.")]
    public async Task DenyVoidAsync()
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await FollowupAsync(NotInThread, ephemeral: true);

            return;
        }

        var result = await _caseService.RatifyVoidAsync(c.Id, await BuildActorAsync(), false, null);
        await FollowupResultAsync(result, ok => $"↩️ Void request rejected; {Ref(ok)} continues.");
    }

    // ---- Modal actions (NOT deferred → RespondWithModalAsync / RespondAsync as first response) ----

    [SlashCommand("add-decision", "File your decision on the claimed case. Run in its review thread.")]
    public async Task AddDecisionAsync([Summary("verdict", "Your decision")] CaseVerdict verdict)
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await RespondAsync(NotInThread, ephemeral: true);

            return;
        }

        await RespondWithModalAsync<VerdictModal>($"qiad-verdict-modal:{c.Id}:{verdict}");
    }

    [SlashCommand("issue-verdict", "Uphold and write the public verdict. Run in the case's review thread.")]
    public async Task IssueVerdictAsync()
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await RespondAsync(NotInThread, ephemeral: true);

            return;
        }

        await RespondWithModalAsync<RatifyModal>($"qiad-ratify-modal:{c.Id}");
    }

    [SlashCommand("reject-case", "Approve the void and write the appellant's reason. Run in the void-request post.")]
    public async Task RejectCaseAsync()
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await RespondAsync(NotInThread, ephemeral: true);

            return;
        }

        await RespondWithModalAsync<VoidApproveModal>($"qiad-void-approve-modal:{c.Id}");
    }

    [SlashCommand("close-misc", "Close the misc filing with its public reasoning. Run in the misc post.")]
    public async Task CloseMiscAsync()
    {
        var c = await ResolveThreadCaseAsync();
        if (c == null)
        {
            await RespondAsync(NotInThread, ephemeral: true);

            return;
        }

        await RespondWithModalAsync<MiscCloseModal>($"qiad-misc-close-modal:{c.Id}");
    }

    [SlashCommand("void-request", "File a void request. Run in the review thread, or pass a queued case's ref.")]
    public async Task VoidRequestAsync(
        [Summary("reason", "Why the submission should be voided")] VoidReason reason,
        [Summary("ref", "Queue REF-XXXXXXXX (omit when run in the review thread)")] string? reference = null)
    {
        var c = reference != null
            ? await _caseService.GetByReferenceAsync(reference)
            : await ResolveThreadCaseAsync();
        if (c == null)
        {
            await RespondAsync(NotInThread, ephemeral: true);

            return;
        }

        await RespondWithModalAsync<VoidModal>($"qiad-void-modal:{c.Id}:{reason}");
    }

    [SlashCommand("misc-file", "File a misc/protocol-bypass filing. Run in the review thread, or pass a ref.")]
    public async Task MiscFileAsync(
        [Summary("type", "The protocol-bypass type")] MiscFilingType type,
        [Summary("ref", "Queue REF-XXXXXXXX (omit when run in the review thread)")] string? reference = null)
    {
        var c = reference != null
            ? await _caseService.GetByReferenceAsync(reference)
            : await ResolveThreadCaseAsync();
        if (c == null)
        {
            await RespondAsync(NotInThread, ephemeral: true);

            return;
        }

        await RespondWithModalAsync<MiscModal>($"qiad-misc-modal:{c.Id}:{type}");
    }

    // ---- helpers ----

    /// <summary>The case whose review thread / void post / misc post this command was run in.</summary>
    private Task<Case?> ResolveThreadCaseAsync() => _caseService.GetByThreadAsync(Context.Channel.Id);

    private async Task<StaffActor> BuildActorAsync() =>
        new(Context.User.Id, Context.User.Username, await _rankResolver.ResolveRankAsync(Context.User.Id));

    private async Task FollowupResultAsync(CaseActionResult result, Func<Case, string> onSuccess) =>
        await FollowupAsync(result.Success ? onSuccess(result.Case!) : $"❌ {result.Error}", ephemeral: true);

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
