using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Web.Models;

namespace QiadHelper.Web.Controllers;

/// <summary>
///     The public landing page. Same visibility contract as the rest of the public surface:
///     numbered, non-sealed cases only, reasoning only when terminal.
/// </summary>
[AllowAnonymous]
public class HomeController : Controller
{
    private readonly QiadContext _context;

    public HomeController(QiadContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var visible = _context.Cases.AsNoTracking()
            .Where(c => c.CaseNumber != null && c.Status != CaseStatus.Sealed);

        var totalCases = await visible.CountAsync(c => c.NumberSeries == CaseNumberSeries.Cases);
        var totalIncidents = await visible.CountAsync(c => c.NumberSeries == CaseNumberSeries.Incidents);
        var pending = await visible.CountAsync(c =>
            c.NumberSeries == CaseNumberSeries.Cases &&
            c.Status != CaseStatus.Closed && c.Status != CaseStatus.Voided);
        var accepted = await visible.CountAsync(c =>
            c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Accepted);
        var rejected = await visible.CountAsync(c =>
            c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Rejected);

        var recent = await visible
            .Where(c => c.NumberSeries == CaseNumberSeries.Cases &&
                        (c.Status == CaseStatus.Closed || c.Status == CaseStatus.Voided))
            .OrderByDescending(c => c.ClosedAt)
            .Take(5)
            .Select(c => new PublicCaseRow(
                c.CaseNumber!.Value,
                c.Category!.Name,
                c.IsUnfair,
                c.ClaimedAt ?? c.SubmittedAt,
                c.ClosedAt,
                c.Status,
                c.Status == CaseStatus.Closed ? c.Verdict : null,
                c.Status == CaseStatus.Closed ? c.Reasoning :
                c.Status == CaseStatus.Voided ? c.VoidReasoning : null,
                null,
                null))
            .ToListAsync();

        return View(new HomeViewModel
                    {
                        TotalCases = totalCases,
                        TotalIncidents = totalIncidents,
                        PendingCases = pending,
                        AcceptanceRate = accepted + rejected == 0 ? 0 : (double)accepted / (accepted + rejected),
                        RecentCases = recent
                    });
    }
}
