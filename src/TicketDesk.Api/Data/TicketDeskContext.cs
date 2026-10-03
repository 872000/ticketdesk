using Microsoft.EntityFrameworkCore;
using TicketDesk.Api.Models;

namespace TicketDesk.Api.Data;

public sealed class TicketDeskContext : DbContext
{
    public TicketDeskContext(DbContextOptions<TicketDeskContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> Comments => Set<TicketComment>();
    public DbSet<TicketAuditEntry> AuditEntries => Set<TicketAuditEntry>();
}
