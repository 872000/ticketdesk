using TicketDesk.Api.Models;
using TicketDesk.Api.Services;

namespace TicketDesk.Tests;

public sealed class SlaServiceTests
{
    private readonly SlaService _sla = new();

    [Theory]
    [InlineData(TicketPriority.Critical, 4)]
    [InlineData(TicketPriority.High, 8)]
    [InlineData(TicketPriority.Medium, 24)]
    [InlineData(TicketPriority.Low, 72)]
    public void DueHours_MatchesPriority(TicketPriority priority, double expectedHours)
    {
        Assert.Equal(expectedHours, SlaService.DueHours(priority));
    }

    [Fact]
    public void ComputeDueDate_AddsPriorityHoursToCreation()
    {
        var created = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        var due = _sla.ComputeDueDate(TicketPriority.High, created);

        Assert.Equal(created.AddHours(8), due);
    }

    [Fact]
    public void GetStatus_FreshTicket_IsOnTrack()
    {
        var created = DateTimeOffset.UtcNow;
        var ticket = new Ticket
        {
            Priority = TicketPriority.Medium,
            Status = TicketStatus.Open,
            CreatedAt = created,
            DueAt = created.AddHours(24)
        };

        Assert.Equal(SlaStatus.OnTrack, _sla.GetStatus(ticket, created.AddHours(1)));
    }

    [Fact]
    public void GetStatus_WithinLastQuarterOfWindow_IsNearBreach()
    {
        var created = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        var ticket = new Ticket
        {
            Priority = TicketPriority.Medium,
            Status = TicketStatus.InProgress,
            CreatedAt = created,
            DueAt = created.AddHours(24)
        };

        // 20 hours in: only 4 of 24 hours remain (< 25% of the window).
        Assert.Equal(SlaStatus.NearBreach, _sla.GetStatus(ticket, created.AddHours(20)));
    }

    [Fact]
    public void GetStatus_PastDue_IsBreached()
    {
        var created = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var ticket = new Ticket
        {
            Priority = TicketPriority.High,
            Status = TicketStatus.Open,
            CreatedAt = created,
            DueAt = created.AddHours(8)
        };

        Assert.Equal(SlaStatus.Breached, _sla.GetStatus(ticket, created.AddHours(9)));
    }

    [Fact]
    public void GetStatus_ResolvedBeforeDue_IsMet()
    {
        var created = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var ticket = new Ticket
        {
            Priority = TicketPriority.Critical,
            Status = TicketStatus.Resolved,
            CreatedAt = created,
            DueAt = created.AddHours(4),
            ResolvedAt = created.AddHours(3)
        };

        Assert.Equal(SlaStatus.Met, _sla.GetStatus(ticket, created.AddDays(1)));
    }

    [Fact]
    public void GetStatus_ResolvedAfterDue_IsBreached()
    {
        var created = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var ticket = new Ticket
        {
            Priority = TicketPriority.Critical,
            Status = TicketStatus.Resolved,
            CreatedAt = created,
            DueAt = created.AddHours(4),
            ResolvedAt = created.AddHours(5)
        };

        Assert.Equal(SlaStatus.Breached, _sla.GetStatus(ticket, created.AddDays(1)));
    }

    [Fact]
    public void GetStatus_PriorityChange_RecomputesDueDate()
    {
        var created = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

        var due = _sla.ComputeDueDate(TicketPriority.Critical, created);

        Assert.Equal(created.AddHours(4), due);
    }
}
