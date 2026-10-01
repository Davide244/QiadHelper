using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QiadHelper.Core.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KeyPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedFromIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedIpAddresses = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    FailedAttemptsResetAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RateLimitPerMinute = table.Column<int>(type: "integer", nullable: false),
                    CurrentWindowRequestCount = table.Column<int>(type: "integer", nullable: false),
                    RateLimitWindowStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TotalUsageCount = table.Column<long>(type: "bigint", nullable: false),
                    RecentIpAddresses = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastRotatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AlertOnNewIp = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Action = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    DiscordUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DiscordUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TargetDiscordUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CaseCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ShowOnPublicForm = table.Column<bool>(type: "boolean", nullable: false),
                    NumberSeries = table.Column<int>(type: "integer", nullable: false),
                    SubmissionChannelId = table.Column<long>(type: "bigint", nullable: true),
                    PanelMessageId = table.Column<long>(type: "bigint", nullable: true),
                    ButtonLabel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PanelText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ForumChannelId = table.Column<long>(type: "bigint", nullable: true),
                    VerdictRatifierMinRank = table.Column<int>(type: "integer", nullable: false),
                    VoidRatifierMinRank = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscordChannelConfigs",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChannelId = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordChannelConfigs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "LoginAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsSuccessful = table.Column<bool>(type: "boolean", nullable: false),
                    DiscordUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DiscordUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QiadRankRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    DiscordRoleId = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    RoleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QiadRankRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestrictionRoleConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefaultDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    PerAttemptIncrement = table.Column<TimeSpan>(type: "interval", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestrictionRoleConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuditLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Field = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OldValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NewValue = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogDetails_AuditLogs_AuditLogId",
                        column: x => x.AuditLogId,
                        principalTable: "AuditLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseNumber = table.Column<int>(type: "integer", nullable: true),
                    NumberSeries = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubjectRobloxUserId = table.Column<long>(type: "bigint", nullable: true),
                    SubjectRobloxUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SubjectDiscordUserId = table.Column<long>(type: "bigint", nullable: true),
                    AppellantDiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    AppellantDiscordUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AppellantDmChannelId = table.Column<long>(type: "bigint", nullable: true),
                    ReportedBanReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AppealText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ClaimedById = table.Column<long>(type: "bigint", nullable: true),
                    ClaimedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Verdict = table.Column<int>(type: "integer", nullable: true),
                    Reasoning = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RatifiedById = table.Column<long>(type: "bigint", nullable: true),
                    RatifiedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RatifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidReasoning = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsMisc = table.Column<bool>(type: "boolean", nullable: false),
                    MiscFilingType = table.Column<int>(type: "integer", nullable: true),
                    DirectorsNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ForumChannelId = table.Column<long>(type: "bigint", nullable: true),
                    ForumThreadId = table.Column<long>(type: "bigint", nullable: true),
                    OpenedMessageId = table.Column<long>(type: "bigint", nullable: true),
                    ClosedMessageId = table.Column<long>(type: "bigint", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedById = table.Column<long>(type: "bigint", nullable: true),
                    ClosedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsImported = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cases_CaseCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CaseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CaseEvidenceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedById = table.Column<long>(type: "bigint", nullable: false),
                    SubmittedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsFromAppellant = table.Column<bool>(type: "boolean", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AttachmentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttachmentFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    SourceChannelId = table.Column<long>(type: "bigint", nullable: true),
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: true),
                    MirroredMessageId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseEvidenceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaseEvidenceItems_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Restrictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetDiscordUserId = table.Column<long>(type: "bigint", nullable: false),
                    TargetDiscordUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetRobloxUserId = table.Column<long>(type: "bigint", nullable: true),
                    GuildId = table.Column<long>(type: "bigint", nullable: false),
                    RoleId = table.Column<long>(type: "bigint", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    AppliedById = table.Column<long>(type: "bigint", nullable: false),
                    AppliedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RemovedById = table.Column<long>(type: "bigint", nullable: true),
                    RemovedByUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedById = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Restrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Restrictions_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_IsActive",
                table: "ApiKeys",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_IsActive_ExpiresAt",
                table: "ApiKeys",
                columns: new[] { "IsActive", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyPrefix",
                table: "ApiKeys",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogDetails_AuditLogId",
                table: "AuditLogDetails",
                column: "AuditLogId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CorrelationId",
                table: "AuditLogs",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_DiscordUserId",
                table: "AuditLogs",
                column: "DiscordUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityId",
                table: "AuditLogs",
                column: "EntityId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName",
                table: "AuditLogs",
                column: "EntityName");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredOnUtc",
                table: "AuditLogs",
                column: "OccurredOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CaseCategories_IsActive",
                table: "CaseCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CaseCategories_SortOrder",
                table: "CaseCategories",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_CaseEvidenceItems_CaseId",
                table: "CaseEvidenceItems",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_AppellantDiscordUserId",
                table: "Cases",
                column: "AppellantDiscordUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_CategoryId",
                table: "Cases",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_ForumThreadId",
                table: "Cases",
                column: "ForumThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_NumberSeries_CaseNumber",
                table: "Cases",
                columns: new[] { "NumberSeries", "CaseNumber" },
                unique: true,
                filter: "\"CaseNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Status",
                table: "Cases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Status_SubmittedAt",
                table: "Cases",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_SubjectDiscordUserId",
                table: "Cases",
                column: "SubjectDiscordUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_SubjectRobloxUserId",
                table: "Cases",
                column: "SubjectRobloxUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_SubmittedAt",
                table: "Cases",
                column: "SubmittedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_AttemptedAtUtc",
                table: "LoginAttempts",
                column: "AttemptedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_AttemptedAtUtc_IsSuccessful",
                table: "LoginAttempts",
                columns: new[] { "AttemptedAtUtc", "IsSuccessful" });

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_DiscordUserId",
                table: "LoginAttempts",
                column: "DiscordUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAttempts_IsSuccessful",
                table: "LoginAttempts",
                column: "IsSuccessful");

            migrationBuilder.CreateIndex(
                name: "IX_QiadRankRoles_GuildId_DiscordRoleId",
                table: "QiadRankRoles",
                columns: new[] { "GuildId", "DiscordRoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestrictionRoleConfigs_GuildId_RoleId",
                table: "RestrictionRoleConfigs",
                columns: new[] { "GuildId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Restrictions_CaseId",
                table: "Restrictions",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Restrictions_Status_ExpiresAt",
                table: "Restrictions",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Restrictions_TargetDiscordUserId",
                table: "Restrictions",
                column: "TargetDiscordUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "AuditLogDetails");

            migrationBuilder.DropTable(
                name: "CaseEvidenceItems");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "DiscordChannelConfigs");

            migrationBuilder.DropTable(
                name: "LoginAttempts");

            migrationBuilder.DropTable(
                name: "QiadRankRoles");

            migrationBuilder.DropTable(
                name: "RestrictionRoleConfigs");

            migrationBuilder.DropTable(
                name: "Restrictions");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Cases");

            migrationBuilder.DropTable(
                name: "CaseCategories");
        }
    }
}
