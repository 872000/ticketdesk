namespace TicketDesk.Api.Models;

public enum TicketPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
    Closed = 3
}

public enum SlaStatus
{
    OnTrack,
    NearBreach,
    Breached,
    Met
}

public static class UserRoles
{
    public const string Requester = "Requester";
    public const string Agent = "Agent";
    public const string Admin = "Admin";
}
