namespace SchoolAdmission.Api.Models;

public enum HoldStatus
{
    Active,
    Confirmed,
    Released,
    Expired
}

public sealed class SeatHold
{
    public Guid Id { get; set; }
    public Guid SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }
    public required string StudentName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public HoldStatus Status { get; set; }
}

