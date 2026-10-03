using System.ComponentModel.DataAnnotations;
using TicketDesk.Api.Models;

namespace TicketDesk.Api.Dtos;

public sealed class CreateTicketRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
}

public sealed class UpdateTicketRequest
{
    [MaxLength(200)]
    public string? Title { get; set; }

    public string? Description { get; set; }

    public TicketPriority? Priority { get; set; }

    public TicketStatus? Status { get; set; }
}

public sealed class AssignTicketRequest
{
    /// <summary>Null = auto-assign to the least-loaded agent.</summary>
    public Guid? AssigneeId { get; set; }
}

public sealed class CreateCommentRequest
{
    [Required]
    public string Body { get; set; } = string.Empty;

    public bool IsInternal { get; set; }
}

public sealed class TicketDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid RequesterId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public Guid? AssigneeId { get; set; }
    public string? AssigneeName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public string SlaStatus { get; set; } = string.Empty;
    public int CommentCount { get; set; }
}

public sealed class CommentDto
{
    public Guid Id { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsInternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class AuditDto
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
