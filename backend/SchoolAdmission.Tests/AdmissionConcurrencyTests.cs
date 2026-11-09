using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SchoolAdmission.Api.Contracts;
using SchoolAdmission.Api.Data;

namespace SchoolAdmission.Tests;

public class AdmissionConcurrencyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AdmissionConcurrencyTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "600");
        _factory = factory;
    }

    private HttpClient CreateStudentClient(string studentId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Student-Id", studentId);
        return client;
    }

    [Fact]
    public async Task ConcurrencyTest_50SimultaneousRequestsFor10Seats_Exactly10SucceedAndNoOverbooking()
    {
        // Arrange: Create a class with strictly 10 seats
        var adminClient = _factory.CreateClient();
        var createResp = await adminClient.PostAsJsonAsync("/classes", new CreateClassRequest("Class 6 - Concurrency Shift", 10));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var createdClass = await createResp.Content.ReadFromJsonAsync<CreateClassResponse>();
        Assert.NotNull(createdClass);

        // Act: Fire 50 simultaneous seat hold requests from 50 distinct 17-digit student IDs
        int totalConcurrentRequests = 50;
        var tasks = Enumerable.Range(1, totalConcurrentRequests).Select(async i =>
        {
            string studentId = $"{20150000000000000L + i}"; // Valid 17-digit ID
            using var client = CreateStudentClient(studentId);
            return await client.PostAsync($"/classes/{createdClass.Id}/holds", null);
        });

        var responses = await Task.WhenAll(tasks);

        // Assert: Exactly 10 must return 201 Created, and 40 must return 409 Conflict
        int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        int conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(10, successCount);
        Assert.Equal(40, conflictCount);

        // Verify class seat summary never exceeds totalSeats
        var classStatus = await adminClient.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{createdClass.Id}");
        Assert.NotNull(classStatus);
        Assert.Equal(10, classStatus.TotalSeats);
        Assert.Equal(10, classStatus.HeldSeats);
        Assert.Equal(0, classStatus.ConfirmedSeats);
        Assert.Equal(0, classStatus.AvailableSeats);
    }

    [Fact]
    public async Task SingleStudent_CannotHoldMultipleActiveSeatsInSameClass()
    {
        var adminClient = _factory.CreateClient();
        var createResp = await adminClient.PostAsJsonAsync("/classes", new CreateClassRequest("Class 7 - Morning Shift", 5));
        var createdClass = (await createResp.Content.ReadFromJsonAsync<CreateClassResponse>())!;

        string studentId = "20152691234567891";
        using var studentClient = CreateStudentClient(studentId);

        // First hold succeeds
        var firstHold = await studentClient.PostAsync($"/classes/{createdClass.Id}/holds", null);
        Assert.Equal(HttpStatusCode.Created, firstHold.StatusCode);

        // Second hold by the same student in the same class fails with 409 Conflict
        var secondHold = await studentClient.PostAsync($"/classes/{createdClass.Id}/holds", null);
        Assert.Equal(HttpStatusCode.Conflict, secondHold.StatusCode);
    }

    [Fact]
    public async Task ConfirmHold_IsIdempotent_AndEnforcesOwnership()
    {
        var adminClient = _factory.CreateClient();
        var createResp = await adminClient.PostAsJsonAsync("/classes", new CreateClassRequest("Class 8 - Day Shift", 5));
        var createdClass = (await createResp.Content.ReadFromJsonAsync<CreateClassResponse>())!;

        string ownerStudentId = "20152691234567892";
        string otherStudentId = "20152691234567893";

        using var ownerClient = CreateStudentClient(ownerStudentId);
        using var otherClient = CreateStudentClient(otherStudentId);

        var holdResp = await ownerClient.PostAsync($"/classes/{createdClass.Id}/holds", null);
        var hold = (await holdResp.Content.ReadFromJsonAsync<CreateHoldResponse>())!;

        // Another student trying to confirm should be rejected (403 Forbidden)
        var unauthorizedConfirm = await otherClient.PostAsync($"/holds/{hold.HoldId}/confirm", null);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedConfirm.StatusCode);

        // Owner confirms multiple times -> both return 200 OK, but confirmedSeats increments only once
        var confirm1 = await ownerClient.PostAsync($"/holds/{hold.HoldId}/confirm", null);
        var confirm2 = await ownerClient.PostAsync($"/holds/{hold.HoldId}/confirm", null);

        Assert.Equal(HttpStatusCode.OK, confirm1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirm2.StatusCode);

        var classStatus = await adminClient.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{createdClass.Id}");
        Assert.NotNull(classStatus);
        Assert.Equal(0, classStatus.HeldSeats);
        Assert.Equal(1, classStatus.ConfirmedSeats);
        Assert.Equal(4, classStatus.AvailableSeats);
    }

    [Fact]
    public async Task HoldExpiration_AutomaticallyReleasesSeatAfterDuration()
    {
        Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "2");
        try
        {
            var adminClient = _factory.CreateClient();
            var createResp = await adminClient.PostAsJsonAsync("/classes", new CreateClassRequest("Class 9 - Expiry Shift", 1));
            var createdClass = (await createResp.Content.ReadFromJsonAsync<CreateClassResponse>())!;

            using var studentClient = CreateStudentClient("20152691234567894");
            var holdResp = await studentClient.PostAsync($"/classes/{createdClass.Id}/holds", null);
            Assert.Equal(HttpStatusCode.Created, holdResp.StatusCode);
            var hold = (await holdResp.Content.ReadFromJsonAsync<CreateHoldResponse>())!;

            // Wait 2.5 seconds for the 2-second hold to expire naturally
            await Task.Delay(2500);

            // Check hold status is EXPIRED and seat is back in availableSeats
            var holdDetail = await adminClient.GetFromJsonAsync<HoldDetailResponse>($"/holds/{hold.HoldId}");
            Assert.NotNull(holdDetail);
            Assert.Equal("EXPIRED", holdDetail.Status);

            var classStatus = await adminClient.GetFromJsonAsync<ClassSummaryResponse>($"/classes/{createdClass.Id}");
            Assert.NotNull(classStatus);
            Assert.Equal(0, classStatus.HeldSeats);
            Assert.Equal(1, classStatus.AvailableSeats);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOLD_DURATION_SECONDS", "600");
        }
    }
}