using Discord;
using Discord.Interactions;
using QiadHelper.Core.Services.Cases;

namespace QiadHelper.Bot.Modules;

/// <summary>
///     Evidence intake is DISABLED for now (product decision): the attach button is no longer
///     offered and the DM listener is not registered (see AddQiadBot). These handlers remain so
///     buttons on messages sent before the change respond gracefully instead of erroring.
/// </summary>
public class EvidenceModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string DisabledMessage = "Evidence intake is currently disabled.";

    [ComponentInteraction("qiad-evidence:*", true)]
    public Task OnAttachEvidenceClickedAsync(string caseId) =>
        RespondAsync(DisabledMessage, ephemeral: true);

    [ModalInteraction("qiad-evidence-modal:*", true)]
    public Task OnEvidenceSubmittedAsync(string caseId, EvidenceModal modal) =>
        RespondAsync(DisabledMessage, ephemeral: true);
}

public class EvidenceModal : IModal
{
    public string Title => "Attach evidence";

    [InputLabel("Evidence (text and/or links)")]
    [ModalTextInput("evidence_text", TextInputStyle.Paragraph,
        "Paste links or write your evidence.",
        maxLength: 3500)]
    public string Text { get; set; } = string.Empty;
}
