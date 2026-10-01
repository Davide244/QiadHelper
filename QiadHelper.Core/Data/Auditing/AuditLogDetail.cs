using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace QiadHelper.Core.Data.Auditing;

public class AuditLogDetail : QiadDataAbstract
{
    public Guid AuditLogId { get; set; }

    [Required] [MaxLength(100)] public string Field { get; set; } = string.Empty;

    [MaxLength(2000)] public string? OldValue { get; set; }

    [MaxLength(2000)] public string? NewValue { get; set; }

    // Navigation
    [JsonIgnore] public virtual AuditLog? AuditLog { get; set; }
}
