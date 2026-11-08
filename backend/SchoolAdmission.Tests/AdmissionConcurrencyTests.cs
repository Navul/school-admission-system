using Xunit;

namespace SchoolAdmission.Tests;

public sealed class AdmissionConcurrencyTests
{
    [Fact]
    public async Task Competing_requests_are_independent_operations()
    {
        Task<int> first = Task.Run(() => Interlocked.Increment(ref _operations));
        Task<int> second = Task.Run(() => Interlocked.Increment(ref _operations));
        int[] results = await Task.WhenAll(first, second);

        Assert.Equal(2, results.Length);
        Assert.Equal(2, _operations);
    }

    private static int _operations;
}

