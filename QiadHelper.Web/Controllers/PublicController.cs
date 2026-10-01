using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Web.Models;

namespace QiadHelper.Web.Controllers;

/// <summary>
///     The public, redacted case database (design §8.2) — replaces the Google Sheet. Anonymous,
///     read-only. The visibility contract lives in <see cref="PublicCasesQuery" />: pre-number
///     and sealed cases never appear; open numbered cases expose number + "Pending" only.
/// </summary>
[AllowAnonymous]
public class PublicController : Controller
{
    private const int PageSize = 50;
    private readonly QiadContext _context;

    public PublicController(QiadContext context)
    {
        _context = context;
    }

    /// <summary>Numbered, non-sealed cases in the given series — the single public query root.</summary>
    private IQueryable<Case> PublicCasesQuery(CaseNumberSeries series) =>
        _context.Cases.AsNoTracking()
            .Where(c => c.CaseNumber != null &&
                        c.Status != CaseStatus.Sealed &&
                        c.NumberSeries == series);

    public async Task<IActionResult> Index(int page = 1, string? type = null, string? verdict = null, int? number = null)
    {
        var query = PublicCasesQuery(CaseNumberSeries.Cases);

        if (!string.IsNullOrWhiteSpace(type))
        {
            query = query.Where(c => c.Category!.Name == type);
        }

        if (number != null)
        {
            query = query.Where(c => c.CaseNumber == number);
        }

        query = verdict switch
        {
            "Accepted" => query.Where(c => c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Accepted),
            "Rejected" => query.Where(c => c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Rejected),
            "Other"    => query.Where(c => c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Other),
            "Pending"  => query.Where(c => c.Status != CaseStatus.Closed && c.Status != CaseStatus.Voided),
            "Voided"   => query.Where(c => c.Status == CaseStatus.Voided),
            _          => query
        };

        var total = await query.CountAsync();
        var rows = await ProjectAsync(query
            .OrderByDescending(c => c.CaseNumber)
            .Skip((Math.Max(page, 1) - 1) * PageSize)
            .Take(PageSize));

        var categories = await _context.CaseCategories.AsNoTracking()
            .Where(cc => cc.NumberSeries == CaseNumberSeries.Cases)
            .OrderBy(cc => cc.SortOrder)
            .Select(cc => cc.Name)
            .ToListAsync();

        return View(new PublicCasesViewModel
                    {
                        Cases = new PagedResult<PublicCaseRow>
                                {
                                    Items = rows,
                                    Page = Math.Max(page, 1),
                                    PageSize = PageSize,
                                    TotalCount = total
                                },
                        Categories = categories,
                        CategoryFilter = type,
                        VerdictFilter = verdict,
                        NumberSearch = number
                    });
    }

    public async Task<IActionResult> Incidents(int page = 1)
    {
        var query = PublicCasesQuery(CaseNumberSeries.Incidents);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(c => c.CaseNumber)
            .Skip((Math.Max(page, 1) - 1) * PageSize)
            .Take(PageSize)
            .Select(c => new PublicIncidentRow(
                c.CaseNumber!.Value,
                c.SubmittedAt,
                c.Status,
                c.Status == CaseStatus.Closed || c.Status == CaseStatus.Voided ? c.Reasoning : null))
            .ToListAsync();

        return View(new PagedResult<PublicIncidentRow>
                    {
                        Items = rows,
                        Page = Math.Max(page, 1),
                        PageSize = PageSize,
                        TotalCount = total
                    });
    }

    public async Task<IActionResult> Stats()
    {
        // Small dataset (~1.6k rows): fetch a minimal projection, aggregate in memory.
        var facts = await _context.Cases.AsNoTracking()
            .Where(c => c.CaseNumber != null && c.Status != CaseStatus.Sealed)
            .Select(c => new
                         {
                             c.NumberSeries,
                             c.Status,
                             c.Verdict,
                             c.IsUnfair,
                             Opened = c.ClaimedAt ?? c.SubmittedAt,
                             c.ClosedAt,
                             Category = c.Category!.Name
                         })
            .ToListAsync();

        var cases = facts.Where(f => f.NumberSeries == CaseNumberSeries.Cases).ToList();
        var closed = cases.Where(f => f.Status == CaseStatus.Closed).ToList();

        var byYear = cases
            .GroupBy(f => f.Opened.Year)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var accepted = g.Count(f => f.Status == CaseStatus.Closed && f.Verdict == CaseVerdict.Accepted);
                var rejected = g.Count(f => f.Status == CaseStatus.Closed && f.Verdict == CaseVerdict.Rejected);
                var other = g.Count(f => f.Status == CaseStatus.Closed && f.Verdict == CaseVerdict.Other);
                var voided = g.Count(f => f.Status == CaseStatus.Voided);
                var unfair = g.Count(f => f.IsUnfair);
                var decided = accepted + rejected;

                return new YearStats(g.Key, g.Count(), accepted, rejected, other, voided, unfair,
                    decided == 0 ? 0 : (double)accepted / decided);
            })
            .ToList();

        var categoriesByYear = cases
            .GroupBy(f => (f.Category, f.Opened.Year))
            .Select(g => new CategoryYearStats(g.Key.Category, g.Key.Year, g.Count()))
            .OrderBy(s => s.Category).ThenBy(s => s.Year)
            .ToList();

        var resolutions = closed
            .Where(f => f.ClosedAt != null)
            .Select(f => f.ClosedAt!.Value - f.Opened)
            .Where(t => t >= TimeSpan.Zero)
            .ToList();

        var decidedTotal = closed.Count(f => f.Verdict is CaseVerdict.Accepted or CaseVerdict.Rejected);
        var acceptedTotal = closed.Count(f => f.Verdict == CaseVerdict.Accepted);

        return View(new PublicStatsViewModel
                    {
                        TotalCases = cases.Count,
                        TotalIncidents = facts.Count(f => f.NumberSeries == CaseNumberSeries.Incidents),
                        PendingCases = cases.Count(f =>
                            f.Status is not (CaseStatus.Closed or CaseStatus.Voided)),
                        OverallAcceptanceRate = decidedTotal == 0 ? 0 : (double)acceptedTotal / decidedTotal,
                        AverageResolutionTime = resolutions.Count == 0
                            ? null
                            : TimeSpan.FromTicks((long)resolutions.Average(t => t.Ticks)),
                        ByYear = byYear,
                        CategoriesByYear = categoriesByYear
                    });
    }

    private static Task<List<PublicCaseRow>> ProjectAsync(IQueryable<Case> query) =>
        query.Select(c => new PublicCaseRow(
                c.CaseNumber!.Value,
                c.Category!.Name,
                c.IsUnfair,
                c.ClaimedAt ?? c.SubmittedAt,
                c.ClosedAt,
                c.Status,
                c.Status == CaseStatus.Closed ? c.Verdict : null,
                // Reasoning becomes public only once the case is terminal (§8.2).
                c.Status == CaseStatus.Closed ? c.Reasoning :
                c.Status == CaseStatus.Voided ? c.VoidReasoning : null,
                c.Status == CaseStatus.Closed ? c.DirectorsNote : null,
                c.Status == CaseStatus.Closed || c.Status == CaseStatus.Voided ? c.ClosedByUsername : null))
            .ToListAsync();
}
