using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Security;

namespace QiadHelper.Web.Services.Security;

/// <summary>
///     Copied from QDF (hashing, IP allowlist, per-key rate limit, brute-force lockout).
///     Differences: "qiad_" key prefix, no legacy environment-variable keys, Discord-id creators.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    private const int MaxFailedAttempts = 5;
    private const int MaxRecentIps = 10;
    private static readonly TimeSpan FailedAttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromHours(1);
    private readonly QiadContext _db;
    private readonly ILogger<ApiKeyService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public ApiKeyService(QiadContext db, IServiceScopeFactory scopeFactory, ILogger<ApiKeyService> logger)
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ApiKeyValidationResult> ValidateKeyAsync(string apiKey,
                                                               string? ipAddress,
                                                               CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiKeyValidationResult(false, FailureReason: "No API key provided");
        }

        var keyHash = HashKey(apiKey);
        var dbKey = await _db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == keyHash, ct);

        if (dbKey == null)
        {
            var keyPreview = apiKey.Length > 12 ? apiKey[..12] + "..." : apiKey;
            _logger.LogWarning("No matching API key found for prefix {KeyPreview} from IP {IP}",
                keyPreview, ipAddress ?? "unknown");

            return new ApiKeyValidationResult(false, FailureReason: "Invalid API key");
        }

        if (dbKey.LockedUntil.HasValue && dbKey.LockedUntil > DateTime.UtcNow)
        {
            return new ApiKeyValidationResult(false,
                FailureReason: $"Key is temporarily locked: {dbKey.LockReason}");
        }

        if (!dbKey.IsActive)
        {
            return new ApiKeyValidationResult(false, FailureReason: "API key is disabled");
        }

        if (dbKey.ExpiresAt.HasValue && dbKey.ExpiresAt.Value < DateTime.UtcNow)
        {
            return new ApiKeyValidationResult(false, FailureReason: "API key has expired");
        }

        if (!string.IsNullOrWhiteSpace(dbKey.AllowedIpAddresses) && !string.IsNullOrWhiteSpace(ipAddress) &&
            !IsIpAllowed(ipAddress, dbKey.AllowedIpAddresses))
        {
            _ = IncrementFailedAttemptsInScopeAsync(dbKey.Id, $"Unauthorized IP: {ipAddress}");

            return new ApiKeyValidationResult(false, FailureReason: "IP address not allowed for this key");
        }

        // Per-key quota layered on top of the global partitioned limiter in Program.cs.
        var (allowed, retryAfterSeconds) = await CheckRateLimitAsync(dbKey, ct);
        if (!allowed)
        {
            return new ApiKeyValidationResult(false,
                FailureReason: $"Rate limit exceeded. Retry after {retryAfterSeconds}s");
        }

        if (dbKey.AlertOnNewIp && !string.IsNullOrWhiteSpace(ipAddress))
        {
            CheckNewIpAlert(dbKey, ipAddress);
        }

        _ = UpdateUsageStatisticsInScopeAsync(dbKey.Id, ipAddress);

        if (dbKey.FailedAttempts > 0)
        {
            _ = ResetFailedAttemptsInScopeAsync(dbKey.Id);
        }

        return new ApiKeyValidationResult(true, dbKey);
    }

    public async Task<ApiKeyCreationResult> CreateKeyAsync(
        string name,
        string? description,
        ulong? createdById,
        DateTime? expiresAt = null,
        string? allowedIps = null,
        ApiKeyScope scope = ApiKeyScope.ReadOnly,
        int rateLimitPerMinute = 60,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new ApiKeyCreationResult(false, Error: "Name is required");
        }

        var fullKey = GenerateSecureKey();
        var keyHash = HashKey(fullKey);
        var keyPrefix = fullKey[..12] + "...";

        var apiKey = new ApiKey
                     {
                         Name = name.Trim(),
                         Description = description?.Trim(),
                         KeyPrefix = keyPrefix,
                         KeyHash = keyHash,
                         CreatedById = createdById,
                         ExpiresAt = expiresAt,
                         AllowedIpAddresses = allowedIps?.Trim(),
                         Scope = scope,
                         RateLimitPerMinute = rateLimitPerMinute,
                         IsActive = true,
                         AlertOnNewIp = !string.IsNullOrWhiteSpace(allowedIps)
                     };

        try
        {
            _db.ApiKeys.Add(apiKey);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Created new API key '{Name}' ({Prefix}) by {CreatedById}. " +
                                   "Expires: {ExpiresAt}, Scope: {Scope}, Rate limit: {RateLimit}/min",
                name, keyPrefix, createdById, expiresAt?.ToString("u") ?? "Never", scope, rateLimitPerMinute);

            return new ApiKeyCreationResult(true, apiKey.Id, fullKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create API key '{Name}'", name);

            return new ApiKeyCreationResult(false, Error: "Failed to create API key");
        }
    }

    public async Task<IReadOnlyList<ApiKeyInfo>> GetAllKeysAsync(CancellationToken ct = default)
    {
        return await _db.ApiKeys
            .AsNoTracking()
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyInfo(
                k.Id,
                k.Name,
                k.Description,
                k.KeyPrefix,
                k.CreatedAt,
                k.CreatedById,
                k.LastUsedAt,
                k.LastUsedFromIp,
                k.ExpiresAt,
                k.IsActive,
                k.AllowedIpAddresses,
                k.Scope,
                k.TotalUsageCount,
                k.FailedAttempts,
                k.LockedUntil,
                k.RateLimitPerMinute,
                k.LockReason
            ))
            .ToListAsync(ct);
    }

    public async Task<bool> RevokeKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        var affected = await _db.ApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsActive, false), ct);

        if (affected > 0)
        {
            _logger.LogWarning("API key {KeyId} REVOKED", keyId);

            return true;
        }

        return false;
    }

    public async Task<bool> DeleteKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        var affected = await _db.ApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteDeleteAsync(ct);

        if (affected > 0)
        {
            _logger.LogWarning("API key {KeyId} DELETED permanently", keyId);

            return true;
        }

        return false;
    }

    public async Task<bool> UnlockKeyAsync(Guid keyId, CancellationToken ct = default)
    {
        var affected = await _db.ApiKeys
            .Where(k => k.Id == keyId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.LockedUntil, (DateTime?)null)
                .SetProperty(k => k.LockReason, (string?)null)
                .SetProperty(k => k.FailedAttempts, 0), ct);

        if (affected > 0)
        {
            _logger.LogWarning("API key {KeyId} manually UNLOCKED", keyId);

            return true;
        }

        return false;
    }

    private async Task<(bool allowed, int retryAfterSeconds)> CheckRateLimitAsync(ApiKey key, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var windowStart = key.RateLimitWindowStart ?? DateTime.MinValue;

        // New 1-minute window: reset and count this request.
        if ((now - windowStart).TotalMinutes >= 1)
        {
            await _db.ApiKeys
                .Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(k => k.RateLimitWindowStart, now)
                    .SetProperty(k => k.CurrentWindowRequestCount, 1), ct);

            return (true, 0);
        }

        // Re-fetch current count from database to avoid race conditions
        var currentCount = await _db.ApiKeys
            .Where(k => k.Id == key.Id)
            .Select(k => k.CurrentWindowRequestCount)
            .FirstOrDefaultAsync(ct);

        if (key.RateLimitPerMinute > 0 && currentCount >= key.RateLimitPerMinute)
        {
            var retryAfter = (int)(60 - (now - windowStart).TotalSeconds);

            return (false, Math.Max(1, retryAfter));
        }

        await _db.ApiKeys
            .Where(k => k.Id == key.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(k => k.CurrentWindowRequestCount, k => k.CurrentWindowRequestCount + 1), ct);

        return (true, 0);
    }

    private async Task IncrementFailedAttemptsInScopeAsync(Guid keyId, string reason)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QiadContext>();

            var key = await db.ApiKeys.FindAsync(keyId);
            if (key == null)
            {
                return;
            }

            var now = DateTime.UtcNow;

            if (key.FailedAttemptsResetAt.HasValue &&
                now - key.FailedAttemptsResetAt.Value > FailedAttemptWindow)
            {
                key.FailedAttempts = 0;
            }

            key.FailedAttempts++;
            key.FailedAttemptsResetAt = now;

            if (key.FailedAttempts >= MaxFailedAttempts)
            {
                key.LockedUntil = now.Add(LockoutDuration);
                key.LockReason = $"Too many failed attempts ({key.FailedAttempts}). Last: {reason}";
                _logger.LogError(
                    "API key {KeyPrefix} LOCKED due to {FailedAttempts} failed attempts. Reason: {Reason}",
                    key.KeyPrefix, key.FailedAttempts, reason);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to increment failed attempts for key {KeyId}", keyId);
        }
    }

    private async Task ResetFailedAttemptsInScopeAsync(Guid keyId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QiadContext>();

            await db.ApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(k => k.FailedAttempts, 0)
                    .SetProperty(k => k.FailedAttemptsResetAt, (DateTime?)null)
                    .SetProperty(k => k.LockedUntil, (DateTime?)null)
                    .SetProperty(k => k.LockReason, (string?)null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset failed attempts for key {KeyId}", keyId);
        }
    }

    private void CheckNewIpAlert(ApiKey key, string ipAddress)
    {
        var recentIps = key.RecentIpAddresses?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];

        if (!recentIps.Contains(ipAddress))
        {
            _logger.LogWarning("SECURITY ALERT: API key {KeyPrefix} ({Name}) used from NEW IP address: {IP}. " +
                               "Recent IPs: {RecentIPs}",
                key.KeyPrefix, key.Name, ipAddress, key.RecentIpAddresses ?? "(none)");
        }
    }

    private async Task UpdateUsageStatisticsInScopeAsync(Guid keyId, string? ipAddress)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QiadContext>();

            var key = await db.ApiKeys.FindAsync(keyId);
            if (key == null)
            {
                return;
            }

            key.LastUsedAt = DateTime.UtcNow;
            key.LastUsedFromIp = ipAddress;
            key.TotalUsageCount++;

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var recentIps = key.RecentIpAddresses?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                                ?? [];

                if (!recentIps.Contains(ipAddress))
                {
                    recentIps.Insert(0, ipAddress);
                    if (recentIps.Count > MaxRecentIps)
                    {
                        recentIps = recentIps.Take(MaxRecentIps).ToList();
                    }

                    key.RecentIpAddresses = string.Join(",", recentIps);
                }
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update usage statistics for API key {KeyId}", keyId);
        }
    }

    private static bool IsIpAllowed(string clientIp, string allowedIps)
    {
        if (string.IsNullOrWhiteSpace(allowedIps) || allowedIps.Trim() == "*")
        {
            return true;
        }

        var allowed = allowedIps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var entry in allowed)
        {
            if (entry == "*")
            {
                return true;
            }

            if (entry.Contains('/'))
            {
                if (IsIpInCidrRange(clientIp, entry))
                {
                    return true;
                }
            }
            else if (entry == clientIp)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsIpInCidrRange(string ipAddress, string cidrNotation)
    {
        try
        {
            var parts = cidrNotation.Split('/');
            if (parts.Length != 2)
            {
                return false;
            }

            var networkAddress = IPAddress.Parse(parts[0]);
            var prefixLength = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var clientAddress = IPAddress.Parse(ipAddress);

            var networkBytes = networkAddress.GetAddressBytes();
            var clientBytes = clientAddress.GetAddressBytes();

            if (networkBytes.Length != clientBytes.Length)
            {
                return false;
            }

            var bytesToCheck = prefixLength / 8;
            var remainingBits = prefixLength % 8;

            for (var i = 0; i < bytesToCheck; i++)
            {
                if (networkBytes[i] != clientBytes[i])
                {
                    return false;
                }
            }

            if (remainingBits > 0 && bytesToCheck < networkBytes.Length)
            {
                var mask = (byte)(0xFF << (8 - remainingBits));
                if ((networkBytes[bytesToCheck] & mask) != (clientBytes[bytesToCheck] & mask))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Generates a cryptographically secure API key.
    ///     Format: qiad_[32 random chars]
    /// </summary>
    private static string GenerateSecureKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var bytes = RandomNumberGenerator.GetBytes(32);
        var result = new StringBuilder("qiad_", 37);

        foreach (var b in bytes)
        {
            result.Append(chars[b % chars.Length]);
        }

        return result.ToString();
    }

    /// <summary>
    ///     Hashes an API key using SHA-256.
    /// </summary>
    private static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
