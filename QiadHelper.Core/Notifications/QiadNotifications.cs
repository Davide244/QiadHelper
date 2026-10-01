using QiadHelper.Core.Data.Cases;

namespace QiadHelper.Core.Notifications;

/// <summary>
///     Typed notifications for Discord side effects (design §7.5). Application services publish
///     these after committing state; the bot-side consumer performs the Discord work (threads,
///     tags, DMs, panel syncs). Keeping every Discord side effect behind this seam gives one code
///     path whether an action originated from a bot interaction or the web admin UI — and it is
///     the boundary that would become a Redis bus again if QIAD ever splits into two processes.
/// </summary>
public abstract record QiadNotification;

/// <summary>
///     A new case was filed; the consumer posts it into the validation-queue channel (claim/void/
///     misc buttons) and confirms via DM. The review thread is only created once claimed.
/// </summary>
public sealed record CaseSubmittedNotification(Guid CaseId) : QiadNotification;

/// <summary>
///     A case moved between statuses; the consumer swaps thread tags, posts thread/status updates,
///     DMs the appellant, and (on close) posts the public announcement.
/// </summary>
public sealed record CaseStatusChangedNotification(Guid CaseId, CaseStatus OldStatus, CaseStatus NewStatus)
    : QiadNotification;

/// <summary>Evidence was filed (usually via the appellant's DM button); mirrored into the thread.</summary>
public sealed record CaseEvidenceFiledNotification(Guid CaseId, Guid EvidenceItemId) : QiadNotification;

/// <summary>An ad-hoc outbound DM to the appellant (e.g. staff note from the admin UI).</summary>
public sealed record CaseDmOutboundNotification(Guid CaseId, string Content) : QiadNotification;

/// <summary>A restriction row was created; the consumer applies the Discord role and DMs the target.</summary>
public sealed record RestrictionAppliedNotification(Guid RestrictionId) : QiadNotification;

/// <summary>A restriction ended (expiry or early lift); the consumer removes the role / DMs as needed.</summary>
public sealed record RestrictionLiftedNotification(Guid RestrictionId, bool WasExpiry) : QiadNotification;

/// <summary>Category intake config changed; the consumer re-syncs the persistent button panels.</summary>
public sealed record CaseCategoryPanelsChangedNotification : QiadNotification;
