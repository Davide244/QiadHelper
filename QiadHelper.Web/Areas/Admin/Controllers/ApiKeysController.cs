using Microsoft.AspNetCore.Mvc;
using QiadHelper.Core;
using QiadHelper.Core.Data.Security;
using QiadHelper.Web.Security;
using QiadHelper.Web.Security.Authorization;
using QiadHelper.Web.Services.Security;

namespace QiadHelper.Web.Areas.Admin.Controllers;

[Area("Admin")]
[QiadAuthorize(QiadRank.Director)]
public class ApiKeysController : Controller
{
    private readonly IApiKeyService _apiKeyService;

    public ApiKeysController(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _apiKeyService.GetAllKeysAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name,
                                            string? description,
                                            ApiKeyScope scope = ApiKeyScope.ReadOnly,
                                            int? expiresInDays = 90,
                                            string? allowedIps = null,
                                            int rateLimitPerMinute = 60)
    {
        var result = await _apiKeyService.CreateKeyAsync(
            name,
            description,
            User.GetDiscordUserId(),
            expiresInDays is > 0 ? DateTime.UtcNow.AddDays(expiresInDays.Value) : null,
            allowedIps,
            scope,
            rateLimitPerMinute);

        if (result.Success)
        {
            // Shown exactly once — the key is stored hashed and cannot be recovered.
            TempData["NewApiKey"] = result.FullKey;
            this.Flash($"API key \"{name}\" created. Copy it now; it will not be shown again.", true);
        }
        else
        {
            this.Flash(result.Error ?? "Failed to create the key.", false);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        this.Flash(await _apiKeyService.RevokeKeyAsync(id) ? "Key revoked." : "Key not found.", true);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(Guid id)
    {
        this.Flash(await _apiKeyService.UnlockKeyAsync(id) ? "Key unlocked." : "Key not found.", true);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        this.Flash(await _apiKeyService.DeleteKeyAsync(id) ? "Key permanently deleted." : "Key not found.", true);

        return RedirectToAction(nameof(Index));
    }
}
