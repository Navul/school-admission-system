using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<SeatHold> Holds => Set<SeatHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SchoolClass>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Grade).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<SeatHold>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.StudentName).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => new { x.SchoolClassId, x.Status });
            entity.HasOne(x => x.SchoolClass)
                .WithMany(x => x.Holds)
                .HasForeignKey(x => x.SchoolClassId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

