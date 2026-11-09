using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolAdmission.Api.Models;

public static class HoldStatus
{
    public const string Held = "HELD";
    public const string Confirmed = "CONFIRMED";
    public const string Expired = "EXPIRED";
    public const string Cancelled = "CANCELLED";
}

[Table("seat_holds")]
public class SeatHold
{
    [Key]
    [Column("hold_id")]
    public string HoldId { get; set; } = $"h_{Guid.NewGuid():N}"[..10];

    [Required]
    [Column("class_id")]
    public string ClassId { get; set; } = string.Empty;

    [ForeignKey(nameof(ClassId))]
    public SchoolClass? SchoolClass { get; set; }

    [Required]
    [MaxLength(17)]
    [Column("student_id")]
    public string StudentId { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public string Status { get; set; } = HoldStatus.Held;

    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}