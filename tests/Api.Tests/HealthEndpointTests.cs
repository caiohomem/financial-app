using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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

        await using var factory = new ApiFactory(databaseUrl);
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

        await using var factory = new ApiFactory(unreachableDatabaseUrl);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"status\":\"degraded\"", content);
        Assert.Contains("\"db\":\"unreachable\"", content);
        Assert.Contains("\"error\":", content);
    }

    private sealed class ApiFactory(string databaseUrl) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DATABASE_URL"] = databaseUrl
                });
            });
        }
    }
}
