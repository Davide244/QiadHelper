using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Notifications;
using QiadHelper.Web.Models;
using QiadHelper.Web.Security;
using QiadHelper.Web.Security.Authorization;

namespace QiadHelper.Web.Areas.Admin.Controllers;

[Area("Admin")]
[QiadAuthorize(QiadRank.Secretary)]
public class CategoriesController : Controller
{
    private readonly QiadContext _context;
    private readonly IQiadNotificationPublisher _publisher;

    public CategoriesController(QiadContext context, IQiadNotificationPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public async Task<IActionResult> Index()
    {
        var categories = await _context.CaseCategories.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        return View(categories);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null)
        {
            return View(new CategoryEditModel());
        }

        var category = await _context.CaseCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (category == null)
        {
            return NotFound();
        }

        return View(new CategoryEditModel
                    {
                        Id = category.Id,
                        Name = category.Name,
                        Description = category.Description,
                        SortOrder = category.SortOrder,
                        IsActive = category.IsActive,
                        ShowOnPublicForm = category.ShowOnPublicForm,
                        NumberSeries = category.NumberSeries,
                        SubmissionChannelId = category.SubmissionChannelId,
                        ForumChannelId = category.ForumChannelId,
                        ButtonLabel = category.ButtonLabel,
                        PanelText = category.PanelText,
                        OfferUnfairAppeals = category.OfferUnfairAppeals,
                        UnfairButtonLabel = category.UnfairButtonLabel,
                        VerdictRatifierMinRank = category.VerdictRatifierMinRank,
                        VoidRatifierMinRank = category.VoidRatifierMinRank,
                        RejectionRestrictionRoleId = category.RejectionRestrictionRoleId,
                        RejectionRestrictionDays = category.RejectionRestrictionDays
                    });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryEditModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        CaseCategory category;
        if (model.Id == null)
        {
            category = new CaseCategory();
            _context.CaseCategories.Add(category);
        }
        else
        {
            category = await _context.CaseCategories.FirstOrDefaultAsync(c => c.Id == model.Id)
                       ?? throw new InvalidOperationException("Category vanished mid-edit.");
        }

        category.Name = model.Name.Trim();
        category.Description = model.Description?.Trim();
        category.SortOrder = model.SortOrder;
        category.IsActive = model.IsActive;
        category.ShowOnPublicForm = model.ShowOnPublicForm;
        category.NumberSeries = model.NumberSeries;
        category.SubmissionChannelId = model.SubmissionChannelId is 0 ? null : model.SubmissionChannelId;
        category.ForumChannelId = model.ForumChannelId is 0 ? null : model.ForumChannelId;
        category.ButtonLabel = model.ButtonLabel.Trim();
        category.PanelText = model.PanelText?.Trim();
        category.OfferUnfairAppeals = model.OfferUnfairAppeals;
        category.UnfairButtonLabel = string.IsNullOrWhiteSpace(model.UnfairButtonLabel)
            ? null
            : model.UnfairButtonLabel.Trim();
        category.VerdictRatifierMinRank = model.VerdictRatifierMinRank;
        category.VoidRatifierMinRank = model.VoidRatifierMinRank;
        category.RejectionRestrictionRoleId =
            model.RejectionRestrictionRoleId is 0 ? null : model.RejectionRestrictionRoleId;
        category.RejectionRestrictionDays = model.RejectionRestrictionDays;
        category.ModifiedById = User.GetDiscordUserId();

        await _context.SaveChangesAsync();
        await _publisher.PublishAsync(new CaseCategoryPanelsChangedNotification());

        this.Flash($"Category \"{category.Name}\" saved. Intake panels are re-syncing.", true);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncPanels()
    {
        await _publisher.PublishAsync(new CaseCategoryPanelsChangedNotification());
        this.Flash("Panel sync queued.", true);

        return RedirectToAction(nameof(Index));
    }
}
