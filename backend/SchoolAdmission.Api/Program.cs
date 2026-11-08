using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Models;
using SchoolAdmission.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Database Connection Configuration (configurable via Environment Variable)
var rawConnStr = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=school_admission;Username=postgres;Password=postgrespassword";

string connectionString = NormalizeConnectionString(rawConnStr);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHostedService<HoldExpirationWorker>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();

// Auto-create/migrate database schema on startup so `docker compose up` works out of the box
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

int GetHoldDurationSeconds()
{
    var raw = Environment.GetEnvironmentVariable("HOLD_DURATION_SECONDS");
    return int.TryParse(raw, out var seconds) && seconds > 0 ? seconds : 600;
}

static string FormatUtcIso(DateTime dt)
{
    var utc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    return utc.ToString("yyyy-MM-ddTHH:mm:ssZ");
}

static bool TryGetValidStudentId(HttpContext ctx, out string studentId, out IResult? errorResult)
{
    studentId = ctx.Request.Headers["X-Student-Id"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(studentId))
    {
        errorResult = Results.BadRequest(new ErrorResponse("শিক্ষার্থীর জন্ম নিবন্ধন নম্বর (X-Student-Id header) দেওয়া আবশ্যক।"));
        return false;
    }

    if (!Regex.IsMatch(studentId, @"^\d{17}$"))
    {
        errorResult = Results.BadRequest(new ErrorResponse("জন্ম নিবন্ধন নম্বর অবশ্যই ১৭ অঙ্কের সংখ্যা হতে হবে।"));
        return false;
    }

    errorResult = null;
    return true;
}

static async Task SyncExpiredHoldsAsync(AppDbContext db, string? classId = null, string? holdId = null, string? studentId = null)
{
    var now = DateTime.UtcNow;
    var query = db.SeatHolds.Where(h => h.Status == HoldStatus.Held && h.ExpiresAt <= now);

    if (classId is not null) query = query.Where(h => h.ClassId == classId);
    if (holdId is not null) query = query.Where(h => h.HoldId == holdId);
    if (studentId is not null) query = query.Where(h => h.StudentId == studentId);

    await query.ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired));
}

// ============================================================================
// 1. POST /classes - Create a new class
// ============================================================================
app.MapPost("/classes", async (CreateClassRequest req, AppDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest(new ErrorResponse("Class-এর নাম খালি রাখা যাবে না।"));

    if (req.TotalSeats <= 0)
        return Results.BadRequest(new ErrorResponse("মোট আসন সংখ্যা (totalSeats) অন্তত ১ হতে হবে।"));

    var schoolClass = new SchoolClass
    {
        Id = $"c_{Guid.NewGuid():N}"[..10],
        Name = req.Name.Trim(),
        TotalSeats = req.TotalSeats,
        CreatedAt = DateTime.UtcNow
    };

    db.Classes.Add(schoolClass);
    await db.SaveChangesAsync();

    var response = new CreateClassResponse(schoolClass.Id, schoolClass.Name, schoolClass.TotalSeats);
    return Results.Created($"/classes/{schoolClass.Id}", response);
});

// ============================================================================
// 2. GET /classes - List all classes with live seat counts
// ============================================================================
app.MapGet("/classes", async (AppDbContext db) =>
{
    await SyncExpiredHoldsAsync(db);
    var now = DateTime.UtcNow;

    var classes = await db.Classes
        .AsNoTracking()
        .OrderBy(c => c.CreatedAt)
        .Select(c => new
        {
            c.Id,
            c.Name,
            c.TotalSeats,
            HeldSeats = c.Holds.Count(h => h.Status == HoldStatus.Held && h.ExpiresAt > now),
            ConfirmedSeats = c.Holds.Count(h => h.Status == HoldStatus.Confirmed)
        })
        .ToListAsync();

    var result = classes.Select(c => new ClassSummaryResponse(
        c.Id,
        c.Name,
        c.TotalSeats,
        c.HeldSeats,
        c.ConfirmedSeats,
        Math.Max(0, c.TotalSeats - c.HeldSeats - c.ConfirmedSeats)
    ));

    return Results.Ok(result);
});

// ============================================================================
// 3. GET /classes/{classId} - Single class seat status
// ============================================================================
app.MapGet("/classes/{classId}", async (string classId, AppDbContext db) =>
{
    await SyncExpiredHoldsAsync(db, classId: classId);
    var now = DateTime.UtcNow;

    var c = await db.Classes
        .AsNoTracking()
        .Where(x => x.Id == classId)
        .Select(x => new
        {
            x.Id,
            x.Name,
            x.TotalSeats,
            HeldSeats = x.Holds.Count(h => h.Status == HoldStatus.Held && h.ExpiresAt > now),
            ConfirmedSeats = x.Holds.Count(h => h.Status == HoldStatus.Confirmed)
        })
        .FirstOrDefaultAsync();

    if (c is null)
        return Results.NotFound(new ErrorResponse("Class খুঁজে পাওয়া যায়নি।"));

    return Results.Ok(new ClassSummaryResponse(
        c.Id,
        c.Name,
        c.TotalSeats,
        c.HeldSeats,
        c.ConfirmedSeats,
        Math.Max(0, c.TotalSeats - c.HeldSeats - c.ConfirmedSeats)
    ));
});

// ============================================================================
// 4. POST /classes/{classId}/holds - Hold a seat (Concurrency-safe via FOR UPDATE)
// ============================================================================
app.MapPost("/classes/{classId}/holds", async (string classId, HttpContext ctx, AppDbContext db) =>
{
    if (!TryGetValidStudentId(ctx, out var studentId, out var errorResult))
        return errorResult!;

    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

    // Acquire row-level lock on the target class row in PostgreSQL.
    // This serializes concurrent requests for the same class across 1 or multiple backend instances.
    var lockedClass = await db.Classes
        .FromSqlInterpolated($"SELECT * FROM classes WHERE id = {classId} FOR UPDATE")
        .FirstOrDefaultAsync();

    if (lockedClass is null)
        return Results.NotFound(new ErrorResponse("Class খুঁজে পাওয়া যায়নি।"));

    var now = DateTime.UtcNow;

    // Immediately expire any overdue holds for this class inside the locked transaction
    await db.SeatHolds
        .Where(h => h.ClassId == classId && h.Status == HoldStatus.Held && h.ExpiresAt <= now)
        .ExecuteUpdateAsync(s => s.SetProperty(h => h.Status, HoldStatus.Expired));

    // Rule: A student cannot have more than one active HELD or CONFIRMED application in the same class
    bool alreadyHasActiveApplication = await db.SeatHolds.AnyAsync(h =>
        h.ClassId == classId &&
        h.StudentId == studentId &&
        (h.Status == HoldStatus.Confirmed || (h.Status == HoldStatus.Held && h.ExpiresAt > now)));

    if (alreadyHasActiveApplication)
    {
        return Results.Conflict(new ErrorResponse("এই ক্লাসে আপনার ইতিমধ্যে একটি চলমান আবেদন বা নিশ্চিত ভর্তি রয়েছে।"));
    }

    int activeHeldAndConfirmed = await db.SeatHolds.CountAsync(h =>
        h.ClassId == classId &&
        (h.Status == HoldStatus.Confirmed || (h.Status == HoldStatus.Held && h.ExpiresAt > now)));

    if (activeHeldAndConfirmed >= lockedClass.TotalSeats)
    {
        return Results.Conflict(new ErrorResponse("দুঃখিত, এই ক্লাসে কোনো আসন খালি নেই।"));
    }

    var holdDuration = GetHoldDurationSeconds();
    var hold = new SeatHold
    {
        HoldId = $"h_{Guid.NewGuid():N}"[..10],
        ClassId = classId,
        StudentId = studentId,
        Status = HoldStatus.Held,
        CreatedAt = now,
        ExpiresAt = now.AddSeconds(holdDuration)
    };

    db.SeatHolds.Add(hold);
    await db.SaveChangesAsync();
    await tx.CommitAsync();

    var response = new CreateHoldResponse(
        hold.HoldId,
        hold.ClassId,
        hold.StudentId,
        hold.Status,
        FormatUtcIso(hold.ExpiresAt)
    );

    return Results.Created($"/holds/{hold.HoldId}", response);
});

// ============================================================================
// 5. GET /holds/{holdId} - Check current hold status
// ============================================================================
app.MapGet("/holds/{holdId}", async (string holdId, AppDbContext db) =>
{
    await SyncExpiredHoldsAsync(db, holdId: holdId);

    var hold = await db.SeatHolds
        .AsNoTracking()
        .FirstOrDefaultAsync(h => h.HoldId == holdId);

    if (hold is null)
        return Results.NotFound(new ErrorResponse("আবেদনটি খুঁজে পাওয়া যায়নি।"));

    return Results.Ok(new HoldDetailResponse(
        hold.HoldId,
        hold.ClassId,
        hold.Status,
        FormatUtcIso(hold.ExpiresAt)
    ));
});

// ============================================================================
// 6. POST /holds/{holdId}/confirm - Confirm admission (Idempotent & Ownership Checked)
// ============================================================================
app.MapPost("/holds/{holdId}/confirm", async (string holdId, HttpContext ctx, AppDbContext db) =>
{
    if (!TryGetValidStudentId(ctx, out var studentId, out var errorResult))
        return errorResult!;

    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

    var hold = await db.SeatHolds
        .FromSqlInterpolated($"SELECT * FROM seat_holds WHERE hold_id = {holdId} FOR UPDATE")
        .FirstOrDefaultAsync();

    if (hold is null)
        return Results.NotFound(new ErrorResponse("আবেদনটি খুঁজে পাওয়া যায়নি।"));

    if (hold.StudentId != studentId)
        return Results.Json(new ErrorResponse("শুধুমাত্র নিজের আবেদন নিশ্চিত করা যাবে।"), statusCode: StatusCodes.Status403Forbidden);

    // Idempotency rule: multiple confirm calls on an already-confirmed hold return 200 OK without incrementing confirmedSeats twice
    if (hold.Status == HoldStatus.Confirmed)
    {
        await tx.CommitAsync();
        return Results.Ok(new ConfirmHoldResponse(hold.HoldId, HoldStatus.Confirmed));
    }

    var now = DateTime.UtcNow;
    if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= now)
    {
        hold.Status = HoldStatus.Expired;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Conflict(new ErrorResponse("আবেদনের সময়সীমা শেষ হয়ে গেছে (EXPIRED)।"));
    }

    if (hold.Status != HoldStatus.Held)
    {
        return Results.Conflict(new ErrorResponse($"এই আবেদনটি নিশ্চিত করা সম্ভব নয় (বর্তমান অবস্থা: {hold.Status})।"));
    }

    hold.Status = HoldStatus.Confirmed;
    await db.SaveChangesAsync();
    await tx.CommitAsync();

    return Results.Ok(new ConfirmHoldResponse(hold.HoldId, HoldStatus.Confirmed));
});

// ============================================================================
// 7. DELETE /holds/{holdId} - Cancel hold
// ============================================================================
app.MapDelete("/holds/{holdId}", async (string holdId, HttpContext ctx, AppDbContext db) =>
{
    if (!TryGetValidStudentId(ctx, out var studentId, out var errorResult))
        return errorResult!;

    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

    var hold = await db.SeatHolds
        .FromSqlInterpolated($"SELECT * FROM seat_holds WHERE hold_id = {holdId} FOR UPDATE")
        .FirstOrDefaultAsync();

    if (hold is null)
        return Results.NotFound(new ErrorResponse("আবেদনটি খুঁজে পাওয়া যায়নি।"));

    if (hold.StudentId != studentId)
        return Results.Json(new ErrorResponse("শুধুমাত্র নিজের আবেদন বাতিল করা যাবে।"), statusCode: StatusCodes.Status403Forbidden);

    var now = DateTime.UtcNow;
    if (hold.Status == HoldStatus.Held && hold.ExpiresAt <= now)
    {
        hold.Status = HoldStatus.Expired;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Conflict(new ErrorResponse("আবেদনের মেয়াদ ইতিমধ্যে শেষ হয়ে গেছে।"));
    }

    if (hold.Status == HoldStatus.Confirmed)
    {
        return Results.Conflict(new ErrorResponse("নিশ্চিত হওয়া ভর্তি বাতিল করা যাবে না।"));
    }

    if (hold.Status == HoldStatus.Held)
    {
        hold.Status = HoldStatus.Cancelled;
        await db.SaveChangesAsync();
    }

    await tx.CommitAsync();
    return Results.NoContent();
});

// ============================================================================
// 8. GET /me/holds - All applications for the current student
// ============================================================================
app.MapGet("/me/holds", async (HttpContext ctx, AppDbContext db) =>
{
    if (!TryGetValidStudentId(ctx, out var studentId, out var errorResult))
        return errorResult!;

    await SyncExpiredHoldsAsync(db, studentId: studentId);

    var holds = await db.SeatHolds
        .AsNoTracking()
        .Include(h => h.SchoolClass)
        .Where(h => h.StudentId == studentId)
        .OrderByDescending(h => h.CreatedAt)
        .Select(h => new MyHoldResponse(
            h.HoldId,
            h.ClassId,
            h.SchoolClass != null ? h.SchoolClass.Name : string.Empty,
            h.Status,
            FormatUtcIso(h.ExpiresAt)
        ))
        .ToListAsync();

    return Results.Ok(holds);
});

app.Run();

static string NormalizeConnectionString(string input)
{
    if (input.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        input.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(input);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty
        };
        return builder.ConnectionString;
    }
    return input;
}

// Expose Program class for WebApplicationFactory in automated tests
public partial class Program { }