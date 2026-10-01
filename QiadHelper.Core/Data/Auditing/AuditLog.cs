using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace QiadHelper.Core.Data.Auditing;

/// <summary>
///     Copied from QDF, minus the operative FKs: QIAD actors/targets are plain Discord user IDs.
/// </summary>
public class AuditLog : QiadDataAbstract
{
    public DateTime OccurredOnUtc { get; set; }

    [Required] [MaxLength(200)] public string Action { get; set; } = string.Empty;

    [Required] [MaxLength(100)] public string EntityName { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }

    // Actor info (who performed the action)
    [MaxLength(50)] public string? DiscordUserId { get; set; }

    [MaxLength(100)] public string? DiscordUsername { get; set; }

    // Target info (who was affected by the action)
    [MaxLength(50)] public string? TargetDiscordUserId { get; set; }

    [MaxLength(50)] public string? IpAddress { get; set; }

    [MaxLength(500)] public string? UserAgent { get; set; }

    [MaxLength(100)] public string? CorrelationId { get; set; }

    public bool IsSystem { get; set; }

    [MaxLength(1000)] public string? Summary { get; set; }

    // Navigation properties
    [JsonIgnore] public virtual ICollection<AuditLogDetail> Details { get; set; } = new List<AuditLogDetail>();
}
