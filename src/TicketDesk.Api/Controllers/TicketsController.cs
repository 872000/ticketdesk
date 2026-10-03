using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Data;
using TicketDesk.Api.Dtos;
using TicketDesk.Api.Models;
using TicketDesk.Api.Services;

namespace TicketDesk.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly TicketDeskContext _db;
    private readonly SlaService _sla;
    private readonly AssignmentService _assignment;

    public TicketsController(TicketDeskContext db, SlaService sla, AssignmentService assignment)
    {
        _db = db;
        _sla = sla;
        _assignment = assignment;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsStaff => User.IsInRole(UserRoles.Agent) || User.IsInRole(UserRoles.Admin);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> List(
        [FromQuery] TicketStatus? status,
        [FromQuery] TicketPriority? priority,
        [FromQuery] Guid? assigneeId,
        CancellationToken ct)
    {
        var query = _db.Tickets.AsQueryable();

        if (!IsStaff)
        {
            query = query.Where(t => t.RequesterId == CurrentUserId);
        }

        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        if (priority.HasValue)
        {
            query = query.Where(t => t.Priority == priority.Value);
        }

        if (assigneeId.HasValue)
        {
            query = query.Where(t => t.AssigneeId == assigneeId.Value);
        }

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return Ok(tickets.Select(t => ToDto(t, users, now)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDto>> Get(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FindAsync(new object[] { id }, ct);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!IsStaff && ticket.RequesterId != CurrentUserId)
        {
            return Forbid();
        }

        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Ok(ToDto(ticket, users, DateTimeOffset.UtcNow));
    }

    [HttpPost]
    public async Task<ActionResult<TicketDto>> Create([FromBody] CreateTicketRequest request, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority,
            Status = TicketStatus.Open,
            RequesterId = CurrentUserId,
            CreatedAt = now,
            UpdatedAt = now,
            DueAt = _sla.ComputeDueDate(request.Priority, now)
        };

        _db.Tickets.Add(ticket);
        AddAudit(ticket.Id, CurrentUserId, "Created", $"Ticket created with priority {request.Priority}.", now);
        await _db.SaveChangesAsync(ct);

        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return CreatedAtAction(nameof(Get), new { id = ticket.Id }, ToDto(ticket, users, now));
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<TicketDto>> Update(Guid id, [FromBody] UpdateTicketRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FindAsync(new object[] { id }, ct);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!IsStaff && ticket.RequesterId != CurrentUserId)
        {
            return Forbid();
        }

        // Requesters may only edit their own open tickets; staff may edit anything.
        if (!IsStaff && ticket.Status != TicketStatus.Open)
        {
            return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var changes = new List<string>();

        if (request.Title is not null && request.Title != ticket.Title)
        {
            changes.Add($"title: '{ticket.Title}' -> '{request.Title}'");
            ticket.Title = request.Title;
        }

        if (request.Description is not null && request.Description != ticket.Description)
        {
            changes.Add("description updated");
            ticket.Description = request.Description;
        }

        if (request.Priority.HasValue && request.Priority.Value != ticket.Priority)
        {
            changes.Add($"priority: {ticket.Priority} -> {request.Priority.Value}");
            ticket.Priority = request.Priority.Value;
            ticket.DueAt = _sla.ComputeDueDate(ticket.Priority, ticket.CreatedAt);
            changes.Add($"SLA due date recomputed to {ticket.DueAt:u}");
        }

        if (request.Status.HasValue && request.Status.Value != ticket.Status)
        {
            if (!IsStaff)
            {
                return Forbid();
            }

            changes.Add($"status: {ticket.Status} -> {request.Status.Value}");
            ticket.Status = request.Status.Value;
            if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
            {
                ticket.ResolvedAt = now;
            }
        }

        if (changes.Count == 0)
        {
            return BadRequest("No changes supplied.");
        }

        ticket.UpdatedAt = now;
        AddAudit(ticket.Id, CurrentUserId, "Updated", string.Join("; ", changes), now);
        await _db.SaveChangesAsync(ct);

        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Ok(ToDto(ticket, users, now));
    }

    [HttpPost("{id:guid}/assign")]
    [Authorize(Roles = $"{UserRoles.Agent},{UserRoles.Admin}")]
    public async Task<ActionResult<TicketDto>> Assign(Guid id, [FromBody] AssignTicketRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FindAsync(new object[] { id }, ct);
        if (ticket is null)
        {
            return NotFound();
        }

        AppUser? agent;
        if (request.AssigneeId.HasValue)
        {
            agent = await _db.Users.FirstOrDefaultAsync(
                u => u.Id == request.AssigneeId.Value && (u.Role == UserRoles.Agent || u.Role == UserRoles.Admin), ct);
            if (agent is null)
            {
                return BadRequest("Assignee must be an agent or admin.");
            }
        }
        else
        {
            agent = await _assignment.PickAgentAsync(_db, ct);
            if (agent is null)
            {
                return BadRequest("No agents available for auto-assignment.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        ticket.AssigneeId = agent.Id;
        ticket.UpdatedAt = now;
        if (ticket.Status == TicketStatus.Open)
        {
            ticket.Status = TicketStatus.InProgress;
        }

        AddAudit(ticket.Id, CurrentUserId, "Assigned", $"Assigned to {agent.Name}.", now);
        await _db.SaveChangesAsync(ct);

        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Ok(ToDto(ticket, users, now));
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, [FromBody] CreateCommentRequest request, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FindAsync(new object[] { id }, ct);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!IsStaff && ticket.RequesterId != CurrentUserId)
        {
            return Forbid();
        }

        // Requesters cannot post internal notes.
        if (!IsStaff && request.IsInternal)
        {
            return Forbid();
        }

        var now = DateTimeOffset.UtcNow;
        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TicketId = id,
            AuthorId = CurrentUserId,
            Body = request.Body,
            IsInternal = request.IsInternal,
            CreatedAt = now
        };

        _db.Comments.Add(comment);
        AddAudit(ticket.Id, CurrentUserId, "Comment", request.IsInternal ? "Internal note added." : "Public comment added.", now);
        await _db.SaveChangesAsync(ct);

        var author = await _db.Users.FindAsync(new object[] { CurrentUserId }, ct);
        return CreatedAtAction(nameof(GetComments), new { id }, new CommentDto
        {
            Id = comment.Id,
            AuthorId = comment.AuthorId,
            AuthorName = author?.Name ?? "Unknown",
            Body = comment.Body,
            IsInternal = comment.IsInternal,
            CreatedAt = comment.CreatedAt
        });
    }

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommentDto>>> GetComments(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets.FindAsync(new object[] { id }, ct);
        if (ticket is null)
        {
            return NotFound();
        }

        if (!IsStaff && ticket.RequesterId != CurrentUserId)
        {
            return Forbid();
        }

        var query = _db.Comments.Where(c => c.TicketId == id);
        if (!IsStaff)
        {
            query = query.Where(c => !c.IsInternal);
        }

        var comments = await query.OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return Ok(comments.Select(c => new CommentDto
        {
            Id = c.Id,
            AuthorId = c.AuthorId,
            AuthorName = users.TryGetValue(c.AuthorId, out var name) ? name : "Unknown",
            Body = c.Body,
            IsInternal = c.IsInternal,
            CreatedAt = c.CreatedAt
        }).ToList());
    }

    [HttpGet("{id:guid}/audit")]
    [Authorize(Roles = $"{UserRoles.Agent},{UserRoles.Admin}")]
    public async Task<ActionResult<IReadOnlyList<AuditDto>>> GetAudit(Guid id, CancellationToken ct)
    {
        if (!await _db.Tickets.AnyAsync(t => t.Id == id, ct))
        {
            return NotFound();
        }

        var entries = await _db.AuditEntries
            .Where(a => a.TicketId == id)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return Ok(entries.Select(a => new AuditDto
        {
            Id = a.Id,
            ActorId = a.ActorId,
            ActorName = users.TryGetValue(a.ActorId, out var name) ? name : "Unknown",
            Action = a.Action,
            Details = a.Details,
            CreatedAt = a.CreatedAt
        }).ToList());
    }

    [HttpGet("sla/breaches")]
    [Authorize(Roles = $"{UserRoles.Agent},{UserRoles.Admin}")]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> Breaches(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var tickets = await _db.Tickets
            .Where(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress)
            .ToListAsync(ct);

        var flagged = tickets
            .Where(t => _sla.GetStatus(t, now) is SlaStatus.Breached or SlaStatus.NearBreach)
            .OrderBy(t => t.DueAt)
            .ToList();

        var users = await _db.Users.ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return Ok(flagged.Select(t => ToDto(t, users, now)).ToList());
    }

    private TicketDto ToDto(Ticket t, Dictionary<Guid, string> users, DateTimeOffset now) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = t.Description,
        Priority = t.Priority.ToString(),
        Status = t.Status.ToString(),
        RequesterId = t.RequesterId,
        RequesterName = users.TryGetValue(t.RequesterId, out var rn) ? rn : "Unknown",
        AssigneeId = t.AssigneeId,
        AssigneeName = t.AssigneeId.HasValue && users.TryGetValue(t.AssigneeId.Value, out var an) ? an : null,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt,
        DueAt = t.DueAt,
        SlaStatus = _sla.GetStatus(t, now).ToString(),
        CommentCount = 0
    };

    private void AddAudit(Guid ticketId, Guid actorId, string action, string details, DateTimeOffset now)
    {
        _db.AuditEntries.Add(new TicketAuditEntry
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ActorId = actorId,
            Action = action,
            Details = details,
            CreatedAt = now
        });
    }
}
