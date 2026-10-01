using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Core.Services;
using QiadHelper.Web.Models;
using QiadHelper.Web.Security;
using QiadHelper.Web.Security.Authorization;

namespace QiadHelper.Web.Areas.Admin.Controllers;

/// <summary>
///     Rank→role mapping (design §11.1), the restriction-role whitelist, and the runtime
///     channel/guild configuration — the knobs that used to be hardcoded or tribal knowledge.
/// </summary>
[Area("Admin")]
[QiadAuthorize(QiadRank.Secretary)]
public class SettingsController : Controller
{
    private readonly IChannelConfigService _channelConfig;
    private readonly QiadContext _context;
    private readonly BotOptions _options;

    public SettingsController(QiadContext context,
                              IChannelConfigService channelConfig,
                              IOptions<BotOptions> options)
    {
        _context = context;
        _channelConfig = channelConfig;
        _options = options.Value;
    }

    public async Task<IActionResult> Index()
    {
        var channels = await _context.DiscordChannelConfigs.AsNoTracking()
            .ToDictionaryAsync(c => c.Key, c => c.ChannelId);

        var mainGuildId = channels.GetValueOrDefault(QiadConfigKeys.MainGuildId);
        if (mainGuildId == 0)
        {
            mainGuildId = _options.MainGuildId;
        }

        return View(new AdminSettingsViewModel
                    {
                        MainGuildId = mainGuildId,
                        RankRoles = await _context.QiadRankRoles.AsNoTracking()
                            .OrderByDescending(r => r.Rank)
                            .ToListAsync(),
                        RestrictionRoles = await _context.RestrictionRoleConfigs.AsNoTracking()
                            .OrderBy(r => r.Name)
                            .ToListAsync(),
                        Channels = channels
                    });
    }

    // ---- Rank → role mapping ----

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRankRole(ulong roleId, QiadRank rank, string? roleName)
    {
        var guildId = await ResolveGuildIdAsync();
        if (guildId == 0 || roleId == 0)
        {
            this.Flash("Set the MainGuildId config and provide a role ID first.", false);

            return RedirectToAction(nameof(Index));
        }

        var exists = await _context.QiadRankRoles
            .AnyAsync(r => r.GuildId == guildId && r.DiscordRoleId == roleId);
        if (exists)
        {
            this.Flash("That role is already mapped.", false);

            return RedirectToAction(nameof(Index));
        }

        _context.QiadRankRoles.Add(new QiadRankRole
                                   {
                                       GuildId = guildId,
                                       DiscordRoleId = roleId,
                                       Rank = rank,
                                       RoleName = roleName?.Trim(),
                                       CreatedById = User.GetDiscordUserId()
                                   });
        await _context.SaveChangesAsync();
        this.Flash($"Role {roleId} mapped to {rank.ShortName()}.", true);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRankRole(Guid id)
    {
        await _context.QiadRankRoles.Where(r => r.Id == id).ExecuteDeleteAsync();
        this.Flash("Rank mapping removed.", true);

        return RedirectToAction(nameof(Index));
    }

    // ---- Restriction-role whitelist ----

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddRestrictionRole(ulong roleId,
                                                        string name,
                                                        string? description,
                                                        double? defaultDays,
                                                        double? perAttemptDays)
    {
        var guildId = await ResolveGuildIdAsync();
        if (guildId == 0 || roleId == 0 || string.IsNullOrWhiteSpace(name))
        {
            this.Flash("Guild config, role ID and a name are required.", false);

            return RedirectToAction(nameof(Index));
        }

        var exists = await _context.RestrictionRoleConfigs
            .AnyAsync(r => r.GuildId == guildId && r.RoleId == roleId);
        if (exists)
        {
            this.Flash("That role is already whitelisted.", false);

            return RedirectToAction(nameof(Index));
        }

        _context.RestrictionRoleConfigs.Add(new RestrictionRoleConfig
                                            {
                                                GuildId = guildId,
                                                RoleId = roleId,
                                                Name = name.Trim(),
                                                Description = description?.Trim(),
                                                DefaultDuration = defaultDays is > 0
                                                    ? TimeSpan.FromDays(defaultDays.Value)
                                                    : null,
                                                PerAttemptIncrement = perAttemptDays is > 0
                                                    ? TimeSpan.FromDays(perAttemptDays.Value)
                                                    : null,
                                                IsActive = true,
                                                CreatedById = User.GetDiscordUserId()
                                            });
        await _context.SaveChangesAsync();
        this.Flash($"Restriction role \"{name}\" whitelisted.", true);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRestrictionRole(Guid id)
    {
        var role = await _context.RestrictionRoleConfigs.FirstOrDefaultAsync(r => r.Id == id);
        if (role != null)
        {
            role.IsActive = !role.IsActive;
            role.ModifiedById = User.GetDiscordUserId();
            await _context.SaveChangesAsync();
            this.Flash($"\"{role.Name}\" is now {(role.IsActive ? "active" : "inactive")}.", true);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRestrictionRole(Guid id)
    {
        await _context.RestrictionRoleConfigs.Where(r => r.Id == id).ExecuteDeleteAsync();
        this.Flash("Restriction role removed from the whitelist.", true);

        return RedirectToAction(nameof(Index));
    }

    // ---- Channel / guild configuration ----

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetChannel(string key, ulong channelId)
    {
        if (AdminSettingsViewModel.KnownChannelKeys.All(k => k.Key != key))
        {
            this.Flash("Unknown configuration key.", false);

            return RedirectToAction(nameof(Index));
        }

        await _channelConfig.SetChannelIdAsync(key, channelId, User.GetDiscordUsername());
        this.Flash($"{key} set to {channelId}.", true);

        return RedirectToAction(nameof(Index));
    }

    private async Task<ulong> ResolveGuildIdAsync()
    {
        var fromDb = await _channelConfig.GetChannelIdAsync(QiadConfigKeys.MainGuildId);

        return fromDb != 0 ? fromDb : _options.MainGuildId;
    }
}
