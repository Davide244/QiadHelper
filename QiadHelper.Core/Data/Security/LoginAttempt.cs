using System.ComponentModel.DataAnnotations;

namespace QiadHelper.Core.Data.Security;

public class LoginAttempt
{
    public Guid Id { get; set; }
    public DateTime AttemptedAtUtc { get; set; }
    public bool IsSuccessful { get; set; }

    [MaxLength(50)] public string? DiscordUserId { get; set; }

    [MaxLength(100)] public string? DiscordUsername { get; set; }

    [MaxLength(50)] public string? IpAddress { get; set; }

    [MaxLength(500)] public string? UserAgent { get; set; }

    [MaxLength(500)] public string? FailureReason { get; set; }
}
