using QiadHelper.Core.Data.Security;

namespace QiadHelper.Web.Services.Security;

public sealed record ApiKeyValidationResult(bool IsValid, ApiKey? Key = null, string? FailureReason = null);

public sealed record ApiKeyCreationResult(bool Success, Guid? KeyId = null, string? FullKey = null, string? Error = null);

public sealed record ApiKeyInfo(
    Guid Id,
    string Name,
    string? Description,
    string KeyPrefix,
    DateTime CreatedAt,
    ulong? CreatedById,
    DateTime? LastUsedAt,
    string? LastUsedFromIp,
    DateTime? ExpiresAt,
    bool IsActive,
    string? AllowedIpAddresses,
    ApiKeyScope Scope,
    long TotalUsageCount,
    int FailedAttempts,
    DateTime? LockedUntil,
    int RateLimitPerMinute,
    string? LockReason);

public interface IApiKeyService
{
    /// <summary>Validates a key, consuming rate-limit quota and updating usage statistics.</summary>
    Task<ApiKeyValidationResult> ValidateKeyAsync(string apiKey, string? ipAddress, CancellationToken ct = default);

    Task<ApiKeyCreationResult> CreateKeyAsync(
        string name,
        string? description,
        ulong? createdById,
        DateTime? expiresAt = null,
        string? allowedIps = null,
        ApiKeyScope scope = ApiKeyScope.ReadOnly,
        int rateLimitPerMinute = 60,
        CancellationToken ct = default);

    Task<IReadOnlyList<ApiKeyInfo>> GetAllKeysAsync(CancellationToken ct = default);

    Task<bool> RevokeKeyAsync(Guid keyId, CancellationToken ct = default);

    Task<bool> DeleteKeyAsync(Guid keyId, CancellationToken ct = default);

    Task<bool> UnlockKeyAsync(Guid keyId, CancellationToken ct = default);
}
