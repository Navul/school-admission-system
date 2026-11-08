using System.ComponentModel.DataAnnotations;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Contracts;

public sealed record ClassResponse(
    Guid Id,
    string Name,
    string Grade,
    int Capacity,
    int AvailableSeats);

public sealed record CreateHoldRequest(
    [property: Required, StringLength(100, MinimumLength = 2)] string StudentName);

public sealed record HoldResponse(
    Guid Id,
    Guid ClassId,
    string ClassName,
    string StudentName,
    DateTimeOffset ExpiresAt,
    string Status)
{
    public static HoldResponse From(SeatHold hold) =>
        new(hold.Id, hold.SchoolClassId, hold.SchoolClass!.Name, hold.StudentName,
            hold.ExpiresAt, hold.Status.ToString());
}

