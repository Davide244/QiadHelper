using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data;

/// <summary>
///     Base class for all QIAD entities (copied from QDF's <c>QDFDataAbstract</c>).
///     One deliberate change: QIAD has no staff/operative table — staff are identified purely
///     by Discord — so the creator/modifier columns are Discord user IDs, not entity FKs.
/// </summary>
public abstract class QiadDataAbstract
{
    [Key] public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }

    /// <summary>Discord user ID of whoever created the row (null for system/imported rows).</summary>
    public ulong? CreatedById { get; set; }

    /// <summary>Discord user ID of whoever last modified the row.</summary>
    public ulong? ModifiedById { get; set; }
}
