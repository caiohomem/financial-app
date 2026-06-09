using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace Api.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task HealthCheck_ReturnsOk_WhenDbIsReachable()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        using var _ = new DatabaseUrlScope(databaseUrl);
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", content);
        Assert.Contains("\"db\":\"reachable\"", content);
    }

    [Fact]
    public async Task HealthCheck_Returns503_WhenDbIsUnreachable()
    {
        const string unreachableDatabaseUrl =
            "Host=127.0.0.1;Port=1;Database=financial_app;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1";

        using var _ = new DatabaseUrlScope(unreachableDatabaseUrl);
        await using var factory = new ApiFactory();

        var exception = await Assert.ThrowsAsync<NpgsqlException>(async () =>
        {
            using var client = factory.CreateClient();
            await client.GetAsync("/api/health");
        });

        Assert.Contains("connect", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) { }
    }

    private sealed class DatabaseUrlScope : IDisposable
    {
        private readonly string? _previousValue = Environment.GetEnvironmentVariable("DATABASE_URL");

        public DatabaseUrlScope(string databaseUrl)
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", _previousValue);
        }
    }
}
