using TicketDesk.Api.Data;
using TicketDesk.Api.Models;

namespace TicketDesk.Api.Data;

public static class SeedData
{
    public static readonly Guid RequesterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Agent1Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Agent2Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid AdminId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static void Seed(TicketDeskContext db)
    {
        if (db.Users.Any())
        {
            return;
        }

        db.Users.AddRange(
            new AppUser { Id = RequesterId, Name = "Ava Requester", Email = "ava.requester@example.com", Role = UserRoles.Requester },
            new AppUser { Id = Agent1Id, Name = "Ben Agent", Email = "ben.agent@example.com", Role = UserRoles.Agent },
            new AppUser { Id = Agent2Id, Name = "Cara Agent", Email = "cara.agent@example.com", Role = UserRoles.Agent },
            new AppUser { Id = AdminId, Name = "Dana Admin", Email = "dana.admin@example.com", Role = UserRoles.Admin });

        db.SaveChanges();
    }
}
