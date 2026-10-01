using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QiadHelper.Core.Data;
using QiadHelper.Core.Data.Cases;
using QiadHelper.Core.Data.Security;
using QiadHelper.Web.Models;
using QiadHelper.Web.Security.Authorization;

namespace QiadHelper.Web.Areas.API.Controllers;

/// <summary>
///     The small external API surface (design §8.3): scoped, hashed, rate-limited keys reading
///     exactly the same redacted projection the public pages render. Nothing else is exposed.
/// </summary>
[Area("API")]
[ApiController]
[Route("api/public")]
[Authorize(Policy = "ApiKeyOrUser")]
public class PublicCasesAPIController : ControllerBase
{
    private const int MaxPageSize = 100;
    private readonly QiadContext _context;

    public PublicCasesAPIController(QiadContext context)
    {
        _context = context;
    }

    [HttpGet("cases")]
    [RequireScope(ApiKeyScope.ReadCases)]
    public async Task<ActionResult<IEnumerable<PublicCaseRow>>> GetCases(int page = 1, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var rows = await _context.Cases.AsNoTracking()
            .Where(c => c.CaseNumber != null &&
                        c.Status != CaseStatus.Sealed &&
                        c.NumberSeries == CaseNumberSeries.Cases)
            .OrderByDescending(c => c.CaseNumber)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
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
                c.Status == CaseStatus.Closed ? c.DirectorsNote : null,
                c.Status == CaseStatus.Closed || c.Status == CaseStatus.Voided ? c.ClosedByUsername : null))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("incidents")]
    [RequireScope(ApiKeyScope.ReadCases)]
    public async Task<ActionResult<IEnumerable<PublicIncidentRow>>> GetIncidents(int page = 1, int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var rows = await _context.Cases.AsNoTracking()
            .Where(c => c.CaseNumber != null &&
                        c.Status != CaseStatus.Sealed &&
                        c.NumberSeries == CaseNumberSeries.Incidents)
            .OrderByDescending(c => c.CaseNumber)
            .Skip((Math.Max(page, 1) - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new PublicIncidentRow(
                c.CaseNumber!.Value,
                c.SubmittedAt,
                c.Status,
                c.Status == CaseStatus.Closed || c.Status == CaseStatus.Voided ? c.Reasoning : null))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("stats")]
    [RequireScope(ApiKeyScope.ReadStats)]
    public async Task<ActionResult<object>> GetStats()
    {
        var visible = _context.Cases.AsNoTracking()
            .Where(c => c.CaseNumber != null && c.Status != CaseStatus.Sealed);

        var totalCases = await visible.CountAsync(c => c.NumberSeries == CaseNumberSeries.Cases);
        var totalIncidents = await visible.CountAsync(c => c.NumberSeries == CaseNumberSeries.Incidents);
        var accepted = await visible.CountAsync(c =>
            c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Accepted);
        var rejected = await visible.CountAsync(c =>
            c.Status == CaseStatus.Closed && c.Verdict == CaseVerdict.Rejected);

        return Ok(new
                  {
                      totalCases,
                      totalIncidents,
                      accepted,
                      rejected,
                      acceptanceRate = accepted + rejected == 0 ? 0 : (double)accepted / (accepted + rejected)
                  });
    }
}
