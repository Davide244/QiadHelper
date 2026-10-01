using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data.Security;

/// <summary>
///     Represents an API key for authenticating external integrations against the small public
///     API surface. Keys are stored hashed for security - the actual key is NEVER stored.
///     (Copied from QDF; unlike QDF, the QIAD bot needs no key at all — it runs in-process.)
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    ///     A friendly name for this key (e.g., "Stats widget", "Development")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Optional description of what this key is used for.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    ///     The first 8 characters of the key for identification (e.g., "qiad_abc1...")
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>
    ///     SHA-256 hash of the full API key. The actual key is never stored.
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>
    ///     When this key was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///     Discord user ID of whoever created this key.
    /// </summary>
    public ulong? CreatedById { get; set; }

    /// <summary>
    ///     When this key was last used for authentication.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    ///     The IP address that last used this key.
    /// </summary>
    [MaxLength(45)]
    public string? LastUsedFromIp { get; set; }

    /// <summary>
    ///     Expiration date. Keys SHOULD always have an expiration for security.
    ///     Default: 90 days from creation.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    ///     Whether this key is currently active. Disabled keys cannot authenticate.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    ///     Restrict this key to specific IP addresses (comma-separated CIDR notation supported).
    ///     Example: "192.168.1.0/24,10.0.0.5"
    ///     Null or empty means no IP restriction (NOT RECOMMENDED for production).
    /// </summary>
    [MaxLength(500)]
    public string? AllowedIpAddresses { get; set; }

    /// <summary>
    ///     Permission scope for this key.
    /// </summary>
    public ApiKeyScope Scope { get; set; } = ApiKeyScope.ReadOnly;

    /// <summary>
    ///     Number of failed authentication attempts with this key prefix.
    ///     Used to detect brute-force attempts.
    /// </summary>
    public int FailedAttempts { get; set; }

    /// <summary>
    ///     When the failed attempts counter was last reset.
    /// </summary>
    public DateTime? FailedAttemptsResetAt { get; set; }

    /// <summary>
    ///     If set, the key is temporarily locked until this time due to suspicious activity.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>
    ///     Reason for the lock (e.g., "Too many failed attempts", "Unusual IP detected")
    /// </summary>
    [MaxLength(200)]
    public string? LockReason { get; set; }

    /// <summary>
    ///     Maximum requests per minute allowed for this key. 0 = unlimited.
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;

    /// <summary>
    ///     Number of requests made in the current rate limit window.
    /// </summary>
    public int CurrentWindowRequestCount { get; set; }

    /// <summary>
    ///     Start of the current rate limit window.
    /// </summary>
    public DateTime? RateLimitWindowStart { get; set; }

    /// <summary>
    ///     Total number of successful authentications with this key.
    /// </summary>
    public long TotalUsageCount { get; set; }

    /// <summary>
    ///     Comma-separated list of unique IPs that have used this key (last 10).
    ///     Used for anomaly detection.
    /// </summary>
    [MaxLength(500)]
    public string? RecentIpAddresses { get; set; }

    /// <summary>
    ///     When this key was last rotated/regenerated.
    /// </summary>
    public DateTime? LastRotatedAt { get; set; }

    /// <summary>
    ///     If true, alerts will be sent when this key is used from a new IP.
    /// </summary>
    public bool AlertOnNewIp { get; set; }
}

/// <summary>
///     Permission scopes for API keys. QIAD's external surface is deliberately small:
///     read access to the public case data plus an admin scope for management tooling.
/// </summary>
[Flags]
public enum ApiKeyScope
{
    /// <summary>No permissions</summary>
    None = 0,

    /// <summary>Read the public (redacted) case + incident data</summary>
    ReadCases = 1 << 0, // 1

    /// <summary>Read aggregate statistics</summary>
    ReadStats = 1 << 1, // 2

    /// <summary>Administrative operations (manage keys, settings)</summary>
    Admin = 1 << 6, // 64

    /// <summary>Read-only access to all public data</summary>
    ReadOnly = ReadCases | ReadStats,

    /// <summary>Full API access (all permissions)</summary>
    Full = ReadCases | ReadStats | Admin
}
