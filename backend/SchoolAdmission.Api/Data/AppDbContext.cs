using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<SeatHold> SeatHolds => Set<SeatHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SchoolClass>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired();
            entity.Property(c => c.TotalSeats).IsRequired();
        });

        modelBuilder.Entity<SeatHold>(entity =>
        {
            entity.HasKey(h => h.HoldId);
            entity.HasIndex(h => new { h.ClassId, h.Status });
            entity.HasIndex(h => new { h.ClassId, h.StudentId });
            entity.HasIndex(h => h.StudentId);

            entity.HasOne(h => h.SchoolClass)
                  .WithMany(c => c.Holds)
                  .HasForeignKey(h => h.ClassId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}