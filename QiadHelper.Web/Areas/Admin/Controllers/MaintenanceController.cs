using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using QiadHelper.Core;
using QiadHelper.Web.Models;
using QiadHelper.Web.Security;
using QiadHelper.Web.Security.Authorization;
using QiadHelper.Web.Services.Backup;

namespace QiadHelper.Web.Areas.Admin.Controllers;

/// <summary>
///     Director-only full-database export/import, the in-app defence against unstable hosting.
///     A restore replaces EVERYTHING, so it is deliberately a three-act flow: acknowledge on
///     upload, review the counts, then type the confirmation phrase.
/// </summary>
[Area("Admin")]
[QiadAuthorize(QiadRank.Director)]
public class MaintenanceController : Controller
{
    public const string ConfirmationPhrase = "REPLACE EVERYTHING";

    private readonly IDatabaseBackupService _backupService;
    private readonly ILogger<MaintenanceController> _logger;

    public MaintenanceController(IDatabaseBackupService backupService, ILogger<MaintenanceController> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(bool acknowledgeSensitive = false)
    {
        if (!acknowledgeSensitive)
        {
            this.Flash("Tick the acknowledgement first: the export contains appellant identities and audit data.",
                false);

            return RedirectToAction(nameof(Index));
        }

        var backup = await _backupService.BuildExportAsync(
            User.GetDiscordUserId(), User.GetDiscordUsername());

        var bytes = JsonSerializer.SerializeToUtf8Bytes(backup, DatabaseBackupService.JsonOptions);
        var fileName = $"qiad-backup-{backup.ExportedAtUtc:yyyy-MM-dd-HHmm}.json";

        return File(bytes, "application/json", fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(268_435_456)] // 256 MB
    public async Task<IActionResult> Upload(IFormFile? file, bool haveCurrentExport = false)
    {
        if (!haveCurrentExport)
        {
            this.Flash("Tick the acknowledgement first: download a fresh export before restoring.", false);

            return RedirectToAction(nameof(Index));
        }

        if (file == null || file.Length == 0)
        {
            this.Flash("Choose a backup file to upload.", false);

            return RedirectToAction(nameof(Index));
        }

        QiadBackupFile backup;
        try
        {
            await using var stream = file.OpenReadStream();
            backup = await _backupService.ParseAsync(stream);
        }
        catch (InvalidDataException ex)
        {
            this.Flash(ex.Message, false);

            return RedirectToAction(nameof(Index));
        }

        // Stash the validated file for the confirmation step; the token travels via TempData.
        var token = Guid.NewGuid().ToString("N");
        await using (var stash = System.IO.File.Create(StashPath(token)))
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(stash);
        }

        TempData["ImportToken"] = token;

        return RedirectToAction(nameof(ConfirmImport));
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmImport()
    {
        var token = TempData.Peek("ImportToken") as string;
        if (token == null || !System.IO.File.Exists(StashPath(token)))
        {
            this.Flash("No pending import. Upload a backup file first.", false);

            return RedirectToAction(nameof(Index));
        }

        await using var stream = System.IO.File.OpenRead(StashPath(token));
        var backup = await _backupService.ParseAsync(stream);

        return View(new ImportConfirmViewModel
                    {
                        Token = token,
                        ExportedAtUtc = backup.ExportedAtUtc,
                        ExportedBy = backup.ExportedBy,
                        FileCounts = backup.Counts(),
                        CurrentCounts = await _backupService.CurrentCountsAsync()
                    });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string token, string confirmPhrase, bool acknowledgeIrreversible = false)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32 || !token.All(char.IsAsciiLetterOrDigit) ||
            !System.IO.File.Exists(StashPath(token)))
        {
            this.Flash("The pending import expired. Upload the backup file again.", false);

            return RedirectToAction(nameof(Index));
        }

        if (!acknowledgeIrreversible || !string.Equals(confirmPhrase?.Trim(), ConfirmationPhrase, StringComparison.Ordinal))
        {
            this.Flash($"Restore not confirmed. Type {ConfirmationPhrase} exactly and tick the acknowledgement.",
                false);
            TempData["ImportToken"] = token;

            return RedirectToAction(nameof(ConfirmImport));
        }

        try
        {
            QiadBackupFile backup;
            await using (var stream = System.IO.File.OpenRead(StashPath(token)))
            {
                backup = await _backupService.ParseAsync(stream);
            }

            await _backupService.RestoreAsync(backup, User.GetDiscordUserId(), User.GetDiscordUsername());

            this.Flash(
                $"Database restored from the backup exported {backup.ExportedAtUtc:u}. Everything previous was replaced.",
                true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database restore failed");
            this.Flash($"Restore FAILED and was rolled back; the database is unchanged. Error: {ex.Message}", false);
        }
        finally
        {
            try { System.IO.File.Delete(StashPath(token)); }
            catch { }
        }

        return RedirectToAction(nameof(Index));
    }

    private static string StashPath(string token) =>
        Path.Combine(Path.GetTempPath(), $"qiad-import-{token}.json");
}
