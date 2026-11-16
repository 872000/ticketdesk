using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Data;
using TicketDesk.Api.Models;
using TicketDesk.Api.Services;

namespace TicketDesk.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly TicketDeskContext _db;
    private readonly AssignmentService _assignment;

    public UsersController(TicketDeskContext db, AssignmentService assignment)
    {
        _db = db;
        _assignment = assignment;
    }

    /// <summary>Lists all users (id, name, email, role), ordered by name. Admin only.</summary>
    [HttpGet]
    [Authorize(Roles = UserRoles.Admin)]
    public async Task<ActionResult<IReadOnlyList<object>>> List(CancellationToken ct)
    {
        var users = await _db.Users.OrderBy(u => u.Name).ToListAsync(ct);
        return Ok(users.Select(u => new { u.Id, u.Name, u.Email, u.Role }).ToList());
    }

    /// <summary>Returns per-agent workload (open / in-progress / overdue counts). Agent or Admin only.</summary>
    [HttpGet("workload")]
    [Authorize(Roles = $"{UserRoles.Agent},{UserRoles.Admin}")]
    public async Task<ActionResult<IReadOnlyList<AgentWorkload>>> Workload(CancellationToken ct)
        => Ok(await _assignment.GetWorkloadAsync(_db, ct));

    /// <summary>Returns the currently authenticated user's own profile (id, name, email, role).</summary>
    [HttpGet("me")]
    public async Task<ActionResult<object>> Me(CancellationToken ct)
    {
        var id = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(new object[] { id }, ct);
        return user is null ? NotFound() : Ok(new { user.Id, user.Name, user.Email, user.Role });
    }
}
