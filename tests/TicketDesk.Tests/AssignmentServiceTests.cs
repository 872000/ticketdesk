using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Data;
using TicketDesk.Api.Models;
using TicketDesk.Api.Services;

namespace TicketDesk.Tests;

public sealed class AssignmentServiceTests : IDisposable
{
    private readonly TicketDeskContext _db;
    private readonly AssignmentService _assignment = new();

    public AssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<TicketDeskContext>()
            .UseInMemoryDatabase($"assign-{Guid.NewGuid()}")
            .Options;
        _db = new TicketDeskContext(options);

        _db.Users.AddRange(
            new AppUser { Id = Guid.NewGuid(), Name = "Ben Agent", Email = "b@x.com", Role = UserRoles.Agent },
            new AppUser { Id = Guid.NewGuid(), Name = "Cara Agent", Email = "c@x.com", Role = UserRoles.Agent });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task PickAgent_PrefersLeastLoadedAgent()
    {
        var agents = await _db.Users.OrderBy(u => u.Name).ToListAsync();
        var busy = agents[0];
        _db.Tickets.Add(new Ticket
        {
            Id = Guid.NewGuid(),
            Title = "Existing",
            Description = "d",
            Status = TicketStatus.InProgress,
            RequesterId = Guid.NewGuid(),
            AssigneeId = busy.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            DueAt = DateTimeOffset.UtcNow.AddHours(8)
        });
        await _db.SaveChangesAsync();

        var picked = await _assignment.PickAgentAsync(_db);

        Assert.NotNull(picked);
        Assert.Equal(agents[1].Id, picked.Id);
    }

    [Fact]
    public async Task PickAgent_TieBreak_IsDeterministicByName()
    {
        var first = await _assignment.PickAgentAsync(_db);
        var second = await _assignment.PickAgentAsync(_db);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Ben Agent", first.Name);
    }

    [Fact]
    public async Task PickAgent_SpreadsLoad_AcrossTickets()
    {
        // Simulate three auto-assignments the way the controller would apply them.
        for (var i = 0; i < 3; i++)
        {
            var agent = await _assignment.PickAgentAsync(_db);
            Assert.NotNull(agent);
            _db.Tickets.Add(new Ticket
            {
                Id = Guid.NewGuid(),
                Title = $"T{i}",
                Description = "d",
                Status = TicketStatus.Open,
                RequesterId = Guid.NewGuid(),
                AssigneeId = agent.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                DueAt = DateTimeOffset.UtcNow.AddHours(8)
            });
            await _db.SaveChangesAsync();
        }

        var agents = await _db.Users.OrderBy(u => u.Name).ToListAsync();
        var counts = agents
            .Select(a => _db.Tickets.Count(t => t.AssigneeId == a.Id))
            .ToList();

        Assert.Equal(new[] { 2, 1 }, counts);
    }

    [Fact]
    public async Task PickAgent_NoAgents_ReturnsNull()
    {
        _db.Users.RemoveRange(_db.Users);
        await _db.SaveChangesAsync();

        Assert.Null(await _assignment.PickAgentAsync(_db));
    }

    [Fact]
    public async Task GetWorkload_ReportsOpenInProgressAndOverdue()
    {
        var agent = await _db.Users.OrderBy(u => u.Name).FirstAsync();
        var now = DateTimeOffset.UtcNow;
        _db.Tickets.AddRange(
            new Ticket { Id = Guid.NewGuid(), Title = "A", Description = "d", Status = TicketStatus.Open, RequesterId = Guid.NewGuid(), AssigneeId = agent.Id, CreatedAt = now.AddHours(-10), UpdatedAt = now, DueAt = now.AddHours(-1) },
            new Ticket { Id = Guid.NewGuid(), Title = "B", Description = "d", Status = TicketStatus.InProgress, RequesterId = Guid.NewGuid(), AssigneeId = agent.Id, CreatedAt = now, UpdatedAt = now, DueAt = now.AddHours(5) },
            new Ticket { Id = Guid.NewGuid(), Title = "C", Description = "d", Status = TicketStatus.Resolved, RequesterId = Guid.NewGuid(), AssigneeId = agent.Id, CreatedAt = now, UpdatedAt = now, DueAt = now.AddHours(5), ResolvedAt = now });
        await _db.SaveChangesAsync();

        var workload = await _assignment.GetWorkloadAsync(_db);
        var entry = workload.Single(w => w.AgentId == agent.Id);

        Assert.Equal(1, entry.Open);
        Assert.Equal(1, entry.InProgress);
        Assert.Equal(1, entry.Overdue);
    }
}
