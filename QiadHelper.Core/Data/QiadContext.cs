using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using QiadHelper.Core.Data.Auditing;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Core.Data.Security;

namespace QiadHelper.Core.Data;

/// <summary>
///     QIAD's own database (design §2: separate solution, separate DB — nothing shared with QDF).
///     Also stores the web host's DataProtection keys so OAuth cookies survive container
///     redeploys without an extra volume (design §13.5).
/// </summary>
public class QiadContext : DbContext, IDataProtectionKeyContext
{
    public QiadContext(DbContextOptions<QiadContext> options) : base(options)
    {
    }

    // Cases
    public DbSet<Case> Cases { get; set; }
    public DbSet<CaseCategory> CaseCategories { get; set; }
    public DbSet<CaseEvidenceItem> CaseEvidenceItems { get; set; }

    // Restrictions
    public DbSet<Restriction> Restrictions { get; set; }
    public DbSet<RestrictionRoleConfig> RestrictionRoleConfigs { get; set; }

    // Audit System
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<AuditLogDetail> AuditLogDetails { get; set; }

    // Security
    public DbSet<LoginAttempt> LoginAttempts { get; set; }
    public DbSet<ApiKey> ApiKeys { get; set; }

    // Configuration
    public DbSet<DiscordChannelConfig> DiscordChannelConfigs { get; set; }
    public DbSet<QiadRankRole> QiadRankRoles { get; set; }

    // DataProtection key ring (cookie auth survival across container restarts)
    public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys
    {
        get;
        set;
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Discord snowflakes are ulong, which Npgsql has no native type for. Bit-cast them to
        // bigint: values round-trip exactly; ordering/inequality in SQL would be wrong for values
        // >= 2^63, which real snowflakes won't reach for decades (2^63 ≈ year 2084).
        configurationBuilder.Properties<ulong>().HaveConversion<UlongToInt64Converter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ============================================================
        // Cases
        // ============================================================

        modelBuilder.Entity<Case>()
            .HasOne(c => c.Category)
            .WithMany()
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // One number per series once assigned; pre-claim cases have none.
        modelBuilder.Entity<Case>()
            .HasIndex(c => new
                           {
                               c.NumberSeries, c.CaseNumber
                           })
            .HasFilter("\"CaseNumber\" IS NOT NULL")
            .IsUnique();

        modelBuilder.Entity<Case>()
            .HasIndex(c => c.Status);

        modelBuilder.Entity<Case>()
            .HasIndex(c => c.SubmittedAt);

        modelBuilder.Entity<Case>()
            .HasIndex(c => c.ForumThreadId);

        modelBuilder.Entity<Case>()
            .HasIndex(c => c.AppellantDiscordUserId);

        modelBuilder.Entity<Case>()
            .HasIndex(c => c.SubjectDiscordUserId);

        modelBuilder.Entity<Case>()
            .HasIndex(c => c.SubjectRobloxUserId);

        modelBuilder.Entity<Case>()
            .HasIndex(c => new
                           {
                               c.Status, c.SubmittedAt
                           });

        // CaseCategory indexes
        modelBuilder.Entity<CaseCategory>()
            .HasIndex(cc => cc.IsActive);

        modelBuilder.Entity<CaseCategory>()
            .HasIndex(cc => cc.SortOrder);

        // CaseEvidenceItem relationships
        modelBuilder.Entity<CaseEvidenceItem>()
            .HasOne(ei => ei.Case)
            .WithMany(c => c.EvidenceItems)
            .HasForeignKey(ei => ei.CaseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CaseEvidenceItem>()
            .HasIndex(ei => ei.CaseId);

        // ============================================================
        // Restrictions
        // ============================================================

        modelBuilder.Entity<Restriction>()
            .HasOne(r => r.Case)
            .WithMany()
            .HasForeignKey(r => r.CaseId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Restriction>()
            .HasIndex(r => r.TargetDiscordUserId);

        // The expiry service polls "Active AND ExpiresAt <= now".
        modelBuilder.Entity<Restriction>()
            .HasIndex(r => new
                           {
                               r.Status, r.ExpiresAt
                           });

        modelBuilder.Entity<RestrictionRoleConfig>()
            .HasIndex(rrc => new
                             {
                                 rrc.GuildId, rrc.RoleId
                             })
            .IsUnique();

        // ============================================================
        // Audit / Security (copied from QDF)
        // ============================================================

        modelBuilder.Entity<AuditLog>()
            .HasMany(al => al.Details)
            .WithOne(ald => ald.AuditLog)
            .HasForeignKey(ald => ald.AuditLogId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.OccurredOnUtc);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.EntityName);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.EntityId);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.DiscordUserId);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.CorrelationId);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(al => al.Action);

        modelBuilder.Entity<AuditLogDetail>()
            .HasIndex(ald => ald.AuditLogId);

        modelBuilder.Entity<LoginAttempt>()
            .HasIndex(la => la.AttemptedAtUtc);

        modelBuilder.Entity<LoginAttempt>()
            .HasIndex(la => la.IsSuccessful);

        modelBuilder.Entity<LoginAttempt>()
            .HasIndex(la => new
                            {
                                la.AttemptedAtUtc, la.IsSuccessful
                            });

        modelBuilder.Entity<LoginAttempt>()
            .HasIndex(la => la.DiscordUserId);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => ak.KeyHash)
            .IsUnique();

        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => ak.KeyPrefix);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => ak.IsActive);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(ak => new
                            {
                                ak.IsActive, ak.ExpiresAt
                            });

        // ============================================================
        // Configuration
        // ============================================================

        // A Discord role maps to exactly one rank; several roles may share a rank.
        modelBuilder.Entity<QiadRankRole>()
            .HasIndex(qrr => new
                             {
                                 qrr.GuildId, qrr.DiscordRoleId
                             })
            .IsUnique();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Keep the base-class timestamps honest without every caller remembering to.
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<QiadDataAbstract>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.CreatedAt == default:
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    break;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Bit-cast ulong ↔ bigint (see <see cref="ConfigureConventions" /> for the caveat).</summary>
    private sealed class UlongToInt64Converter : ValueConverter<ulong, long>
    {
        public UlongToInt64Converter() : base(v => unchecked((long)v), v => unchecked((ulong)v))
        {
        }
    }
}
