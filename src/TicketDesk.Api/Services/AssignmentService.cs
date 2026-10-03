using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Data;
using TicketDesk.Api.Models;

namespace TicketDesk.Api.Services;

/// <summary>
/// Workload-balanced auto-assignment: unassigned tickets go to the agent with
/// the fewest open tickets (ties broken by name for determinism).
/// </summary>
public sealed class AssignmentService
{
    public async Task<AppUser?> PickAgentAsync(TicketDeskContext db, CancellationToken ct = default)
    {
        var agents = await db.Users
            .Where(u => u.Role == UserRoles.Agent)
            .OrderBy(u => u.Name)
            .ToListAsync(ct);

        if (agents.Count == 0)
        {
            return null;
        }

        AppUser? best = null;
        var bestLoad = int.MaxValue;

        foreach (var agent in agents)
        {
            var load = await db.Tickets.CountAsync(
                t => t.AssigneeId == agent.Id &&
                     (t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress), ct);

            if (load < bestLoad)
            {
                bestLoad = load;
                best = agent;
            }
        }

        return best;
    }

    public async Task<IReadOnlyList<AgentWorkload>> GetWorkloadAsync(TicketDeskContext db, CancellationToken ct = default)
    {
        var agents = await db.Users
            .Where(u => u.Role == UserRoles.Agent)
            .OrderBy(u => u.Name)
            .ToListAsync(ct);

        var result = new List<AgentWorkload>();
        foreach (var agent in agents)
        {
            var tickets = await db.Tickets
                .Where(t => t.AssigneeId == agent.Id)
                .ToListAsync(ct);

            result.Add(new AgentWorkload(
                agent.Id,
                agent.Name,
                tickets.Count(t => t.Status == TicketStatus.Open),
                tickets.Count(t => t.Status == TicketStatus.InProgress),
                tickets.Count(t => t.Status is TicketStatus.Open or TicketStatus.InProgress &&
                                   t.DueAt < DateTimeOffset.UtcNow)));
        }

        return result;
    }
}

public sealed record AgentWorkload(Guid AgentId, string Name, int Open, int InProgress, int Overdue);
