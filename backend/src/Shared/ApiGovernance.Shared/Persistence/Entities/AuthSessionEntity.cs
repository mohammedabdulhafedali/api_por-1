namespace ApiGovernance.Shared.Persistence.Entities;

/// <summary>
/// Server-side revocable session ticket store entry (ITicketStore) for ASP.NET Core Identity
/// cookie authentication per 04_Authentication.md and 08_DB_and_plan_delta.md (Task I01).
/// </summary>
public sealed class AuthSessionEntity
{
    public string Id { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string SecurityStamp { get; set; } = string.Empty;
    public byte[] TicketBytes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

