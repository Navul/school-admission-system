namespace SchoolAdmission.Api.Models;

public sealed class SchoolClass
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Grade { get; set; }
    public int Capacity { get; set; }
    public int ConfirmedAdmissions { get; set; }
    public byte[] Version { get; set; } = [];
    public ICollection<SeatHold> Holds { get; set; } = [];
}

