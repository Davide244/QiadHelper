using Microsoft.EntityFrameworkCore;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;

namespace QiadHelper.Web.Data;

/// <summary>
///     First-run seed of the case-category catalogue (design §2.2, the live submission form's
///     set). Punishment categories offer the unfair-appeal button, so their intake panels carry
///     the live system's two buttons ("Start an Appeal" / "Start an Unfair Appeal"); unfair is a
///     flag on the case, not a category of its own. Categories are data; admins can reshape all
///     of this afterwards. The seed only fires on an empty table.
/// </summary>
public static class DefaultDataSeeder
{
    public static async Task SeedAsync(QiadContext context)
    {
        if (await context.CaseCategories.AnyAsync())
        {
            return;
        }

        CaseCategory Appeal(string name, string description, int sortOrder) => new()
            {
                Name = name,
                Description = description,
                ButtonLabel = "Start an Appeal",
                OfferUnfairAppeals = true,
                UnfairButtonLabel = "Start an Unfair Appeal",
                SortOrder = sortOrder
            };

        context.CaseCategories.AddRange(
            Appeal("Discord Ban Appeal", "Make a Discord Ban Appeal here.", 10),
            Appeal("Game Ban Appeal", "Make a Game Ban Appeal (QSERF) here.", 20),
            Appeal("Blacklist Appeal", "Appeal a permanent blacklist (all platforms).", 30),
            Appeal("Group Ban Appeal", "Appeal a Roblox group ban.", 40),
            Appeal("Raider Appeal", "Appeal a raider designation.", 50),
            Appeal("QSP Warn Appeal", "Appeal a QSP warning.", 60),
            new CaseCategory
            {
                Name = "Investigation",
                Description = "Report a high-profile user or admin abuse for investigation.",
                ButtonLabel = "File a report",
                NumberSeries = CaseNumberSeries.Incidents,
                SortOrder = 70
            },
            new CaseCategory
            {
                Name = "Infraction Audit",
                Description = "Staff-initiated audit of an issued infraction.",
                ButtonLabel = "Request an audit",
                ShowOnPublicForm = false,
                SortOrder = 80
            });

        await context.SaveChangesAsync();
    }
}
