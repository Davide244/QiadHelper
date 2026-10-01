using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Security;

namespace QiadHelper.Web.Services.Security;

public interface ILoginTrackingService
{
    Task LogSuccessfulLoginAsync(string? discordUserId,
                                 string? discordUsername,
                                 string? ipAddress,
                                 string? userAgent);

    Task LogFailedLoginAsync(string? discordUserId,
                             string? discordUsername,
                             string failureReason,
                             string? ipAddress,
                             string? userAgent);
}

public sealed class LoginTrackingService : ILoginTrackingService
{
    private readonly QiadContext _db;
    private readonly ILogger<LoginTrackingService> _logger;

    public LoginTrackingService(QiadContext db, ILogger<LoginTrackingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task LogSuccessfulLoginAsync(string? discordUserId,
                                        string? discordUsername,
                                        string? ipAddress,
                                        string? userAgent) =>
        WriteAsync(true, discordUserId, discordUsername, null, ipAddress, userAgent);

    public Task LogFailedLoginAsync(string? discordUserId,
                                    string? discordUsername,
                                    string failureReason,
                                    string? ipAddress,
                                    string? userAgent) =>
        WriteAsync(false, discordUserId, discordUsername, failureReason, ipAddress, userAgent);

    private async Task WriteAsync(bool success,
                                  string? discordUserId,
                                  string? discordUsername,
                                  string? failureReason,
                                  string? ipAddress,
                                  string? userAgent)
    {
        try
        {
            _db.LoginAttempts.Add(new LoginAttempt
                                  {
                                      Id = Guid.NewGuid(),
                                      AttemptedAtUtc = DateTime.UtcNow,
                                      IsSuccessful = success,
                                      DiscordUserId = Truncate(discordUserId, 50),
                                      DiscordUsername = Truncate(discordUsername, 100),
                                      FailureReason = Truncate(failureReason, 500),
                                      IpAddress = Truncate(ipAddress, 50),
                                      UserAgent = Truncate(userAgent, 500)
                                  });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Login tracking must never break the login flow.
            _logger.LogWarning(ex, "Failed to record login attempt");
        }
    }

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];
}
