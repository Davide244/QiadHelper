using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Notifications;
using QiadHelper.Core.Services.Cases;
using QiadHelper.Web.Models;
using QiadHelper.Web.Security;
using QiadHelper.Web.Security.Authorization;

namespace QiadHelper.Web.Areas.Admin.Controllers;

[Area("Admin")]
[QiadAuthorize(QiadRank.ProbationaryAffairs)]
public class CasesController : Controller
{
    private const int PageSize = 50;
    private readonly ICaseService _caseService;
    private readonly QiadContext _context;
    private readonly IQiadNotificationPublisher _publisher;

    public CasesController(QiadContext context, ICaseService caseService, IQiadNotificationPublisher publisher)
    {
        _context = context;
        _caseService = caseService;
        _publisher = publisher;
    }

    public async Task<IActionResult> Index(CaseStatus? status = null, int page = 1)
    {
        var query = _context.Cases.AsNoTracking()
            .Include(c => c.Category)
            .AsQueryable();

        if (status != null)
        {
            query = query.Where(c => c.Status == status);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(c => c.SubmittedAt)
            .Skip((Math.Max(page, 1) - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return View(new AdminCaseListViewModel
                    {
                        Cases = new PagedResult<Case>
                                {
                                    Items = items,
                                    Page = Math.Max(page, 1),
                                    PageSize = PageSize,
                                    TotalCount = total
                                },
                        StatusFilter = status
                    });
    }

    public async Task<IActionResult> Detail(Guid id)
    {
        var c = await _context.Cases.AsNoTracking()
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c == null)
        {
            return NotFound();
        }

        var evidence = await _context.CaseEvidenceItems.AsNoTracking()
            .Where(e => e.CaseId == id)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        var restrictions = await _context.Restrictions.AsNoTracking()
            .Where(r => r.CaseId == id)
            .OrderByDescending(r => r.AppliedAt)
            .ToListAsync();

        return View(new AdminCaseDetailViewModel
                    {
                        Case = c,
                        Evidence = evidence,
                        Restrictions = restrictions
                    });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Seal(Guid id, bool seal)
    {
        var result = await _caseService.SetSealedAsync(id, User.ToStaffActor(), seal);
        this.Flash(result.Success
            ? seal ? "Case sealed. It is hidden from the public database." : "Case unsealed."
            : result.Error!, result.Success);

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendDm(Guid id, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            this.Flash("The message cannot be empty.", false);

            return RedirectToAction(nameof(Detail), new { id });
        }

        await _publisher.PublishAsync(new CaseDmOutboundNotification(id, message.Trim()));
        this.Flash("Message queued. The bot will DM the appellant.", true);

        return RedirectToAction(nameof(Detail), new { id });
    }
}

internal static class ControllerFlashExtensions
{
    public static void Flash(this Controller controller, string message, bool ok)
    {
        controller.TempData["Flash"] = message;
        controller.TempData["FlashKind"] = ok ? "ok" : "err";
    }
}
