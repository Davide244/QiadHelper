using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Bot;
using QiadHelper.Core;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Data.Restrictions;
using QiadHelper.Web.Models;
using QiadHelper.Web.Security.Authorization;

namespace QiadHelper.Web.Areas.Admin.Controllers;

[Area("Admin")]
[QiadAuthorize(QiadRank.ProbationaryAffairs)]
public class DashboardController : Controller
{
    private readonly QiadBot _bot;
    private readonly QiadContext _context;

    public DashboardController(QiadContext context, QiadBot bot)
    {
        _context = context;
        _bot = bot;
    }

    public async Task<IActionResult> Index()
    {
        var byStatus = await _context.Cases.AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count);

        return View(new AdminDashboardViewModel
                    {
                        Submitted = byStatus.GetValueOrDefault(CaseStatus.Submitted),
                        UnderReview = byStatus.GetValueOrDefault(CaseStatus.UnderReview),
                        AwaitingRatification = byStatus.GetValueOrDefault(CaseStatus.AwaitingRatification),
                        AwaitingVoidRatification = byStatus.GetValueOrDefault(CaseStatus.AwaitingVoidRatification),
                        AwaitingExecution = byStatus.GetValueOrDefault(CaseStatus.AwaitingExecution),
                        ClosedTotal = byStatus.GetValueOrDefault(CaseStatus.Closed) +
                                      byStatus.GetValueOrDefault(CaseStatus.Voided) +
                                      byStatus.GetValueOrDefault(CaseStatus.Sealed),
                        ActiveRestrictions = await _context.Restrictions.AsNoTracking()
                            .CountAsync(r => r.Status == RestrictionStatus.Active),
                        BotConnected = _bot.IsConnected
                    });
    }
}
