using TicketDesk.Api.Models;

namespace TicketDesk.Api.Services;

/// <summary>
/// Computes SLA due dates per priority and classifies tickets as on-track,
/// near-breach, breached, or met.
/// </summary>
public sealed class SlaService
{
    /// <summary>Hours from creation until the SLA is due, by priority.</summary>
    public static double DueHours(TicketPriority priority) => priority switch
    {
        TicketPriority.Critical => 4,
        TicketPriority.High => 8,
        TicketPriority.Medium => 24,
        TicketPriority.Low => 72,
        _ => 72
    };

    /// <summary>
    /// Returns the exact due timestamp for a ticket, adding the priority's SLA
    /// window (<see cref="DueHours"/>) to the ticket's creation time.
    /// </summary>
    public DateTimeOffset ComputeDueDate(TicketPriority priority, DateTimeOffset createdAt)
        => createdAt.AddHours(DueHours(priority));

    /// <summary>
    /// A ticket is "near breach" when less than 25% of its SLA window remains.
    /// Resolved/closed tickets report Met when finished before the due date.
    /// </summary>
    public SlaStatus GetStatus(Ticket ticket, DateTimeOffset now)
    {
        if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return (ticket.ResolvedAt ?? now) <= ticket.DueAt ? SlaStatus.Met : SlaStatus.Breached;
        }

        if (now > ticket.DueAt)
        {
            return SlaStatus.Breached;
        }

        var window = ticket.DueAt - ticket.CreatedAt;
        return (ticket.DueAt - now) <= window * 0.25 ? SlaStatus.NearBreach : SlaStatus.OnTrack;
    }
}
