using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolAdmission.Api.Models;

[Table("classes")]
public class SchoolClass
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = $"c_{Guid.NewGuid():N}"[..10];

    [Required]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("total_seats")]
    public int TotalSeats { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<SeatHold> Holds { get; set; } = new();
}