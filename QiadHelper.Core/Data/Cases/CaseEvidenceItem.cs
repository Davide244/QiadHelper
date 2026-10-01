using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace QiadHelper.Core.Data.Cases;

/// <summary>
///     A single piece of evidence on a case — slimmed from QDF's two-level Evidence model down to
///     one table, since the case itself is the bundle. Items arrive from the appellant's DM
///     attach-button or from staff in the review thread, and are mirrored into the forum thread.
/// </summary>
public class CaseEvidenceItem : QiadDataAbstract
{
    public Guid CaseId { get; set; }

    /// <summary>Discord user who filed the item.</summary>
    public ulong SubmittedById { get; set; }

    [MaxLength(64)] public string? SubmittedByUsername { get; set; }

    /// <summary>True when filed by the appellant (via DM), false when filed by staff in-thread.</summary>
    public bool IsFromAppellant { get; set; }

    /// <summary>Free-text portion (statement, context, or an evidence-forum link).</summary>
    [MaxLength(4000)]
    public string? Text { get; set; }

    /// <summary>Discord CDN URL of an attached file, when the item carries one.</summary>
    [MaxLength(1000)]
    public string? AttachmentUrl { get; set; }

    [MaxLength(260)] public string? AttachmentFileName { get; set; }

    // ---- Discord provenance (recorded automatically) ----

    /// <summary>Where the item was originally posted (DM channel or thread).</summary>
    public ulong? SourceChannelId { get; set; }

    public ulong? SourceMessageId { get; set; }

    /// <summary>The mirrored copy in the staff review thread, once posted.</summary>
    public ulong? MirroredMessageId { get; set; }

    // ---- Navigation ----

    [JsonIgnore] public virtual Case? Case { get; set; }
}
