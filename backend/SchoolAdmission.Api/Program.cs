using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;
using SchoolAdmission.Api.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
string[] origins = builder.Configuration["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
    ?? ["http://localhost:5173"];

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHostedService<HoldExpirationWorker>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

WebApplication app = builder.Build();
using (IServiceScope scope = app.Services.CreateScope())
{
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Classes.AnyAsync())
    {
        db.Classes.AddRange(
            new SchoolClass { Id = Guid.NewGuid(), Name = "Primary A", Grade = "Grade 1", Capacity = 30 },
            new SchoolClass { Id = Guid.NewGuid(), Name = "Primary B", Grade = "Grade 2", Capacity = 25 },
            new SchoolClass { Id = Guid.NewGuid(), Name = "Secondary A", Grade = "Grade 7", Capacity = 20 });
        await db.SaveChangesAsync();
    }
}

app.UseCors();
app.MapGet("/api/classes", async (IDbContextFactory<AppDbContext> factory, CancellationToken cancellationToken) =>
{
    await using AppDbContext db = await factory.CreateDbContextAsync(cancellationToken);
    DateTimeOffset now = DateTimeOffset.UtcNow;
    List<ClassResponse> classes = await db.Classes
        .AsNoTracking()
        .Select(x => new ClassResponse(x.Id, x.Name, x.Grade, x.Capacity,
            x.Capacity - x.ConfirmedAdmissions -
            x.Holds.Count(h => h.Status == HoldStatus.Active && h.ExpiresAt > now)))
        .OrderBy(x => x.Grade)
        .ToListAsync(cancellationToken);
    return Results.Ok(classes);
});

app.MapPost("/api/classes/{classId:guid}/holds", async (
    Guid classId, CreateHoldRequest request, IDbContextFactory<AppDbContext> factory,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.StudentName))
        return Results.BadRequest(new { message = "Student name is required." });

    await using AppDbContext db = await factory.CreateDbContextAsync(cancellationToken);
    await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
        await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
    SchoolClass? schoolClass = await db.Classes.SingleOrDefaultAsync(x => x.Id == classId, cancellationToken);
    if (schoolClass is null)
        return Results.NotFound(new { message = "Class not found." });

    DateTimeOffset now = DateTimeOffset.UtcNow;
    int activeHolds = await db.Holds.CountAsync(
        x => x.SchoolClassId == classId && x.Status == HoldStatus.Active && x.ExpiresAt > now, cancellationToken);
    if (schoolClass.ConfirmedAdmissions + activeHolds >= schoolClass.Capacity)
        return Results.Conflict(new { message = "There are no seats available." });

    SeatHold hold = new()
    {
        Id = Guid.NewGuid(), SchoolClassId = classId, StudentName = request.StudentName.Trim(),
        CreatedAt = now, ExpiresAt = now.AddMinutes(10), Status = HoldStatus.Active
    };
    db.Holds.Add(hold);
    try
    {
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        hold.SchoolClass = schoolClass;
        return Results.Created($"/api/holds/{hold.Id}", HoldResponse.From(hold));
    }
    catch (DbUpdateException)
    {
        return Results.Conflict(new { message = "The seat changed before it could be held. Please try again." });
    }
});

app.MapDelete("/api/holds/{holdId:guid}", async (
    Guid holdId, IDbContextFactory<AppDbContext> factory, CancellationToken cancellationToken) =>
{
    await using AppDbContext db = await factory.CreateDbContextAsync(cancellationToken);
    int changed = await db.Holds
        .Where(x => x.Id == holdId && x.Status == HoldStatus.Active)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, HoldStatus.Released), cancellationToken);
    return changed == 0 ? Results.NotFound(new { message = "Active hold not found." }) : Results.NoContent();
});

app.MapPost("/api/holds/{holdId:guid}/confirm", async (
    Guid holdId, IDbContextFactory<AppDbContext> factory, CancellationToken cancellationToken) =>
{
    await using AppDbContext db = await factory.CreateDbContextAsync(cancellationToken);
    await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
        await db.Database.BeginTransactionAsync(cancellationToken);
    SeatHold? hold = await db.Holds.Include(x => x.SchoolClass)
        .SingleOrDefaultAsync(x => x.Id == holdId, cancellationToken);
    if (hold is null || hold.Status != HoldStatus.Active || hold.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.NotFound(new { message = "Active hold not found or expired." });
    hold.Status = HoldStatus.Confirmed;
    hold.SchoolClass!.ConfirmedAdmissions++;
    try
    {
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(HoldResponse.From(hold));
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { message = "The seat changed before confirmation. Please try again." });
    }
});

app.Run();

public partial class Program { }
