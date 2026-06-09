using DbUp;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException(
        "Required configuration 'DATABASE_URL' is missing. Set the DATABASE_URL environment variable before starting the application.");

Program.RunMigrations(connectionString);

var app = builder.Build();

app.MapGet("/api/health", async () =>
{
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync();

        return Results.Ok(new { status = "ok", db = "reachable" });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "degraded", db = "unreachable", error = ex.Message },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public partial class Program
{
    internal static void RunMigrations(string connectionString)
    {
        var result = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                typeof(Program).Assembly,
                script => script.StartsWith("Api.Migrations.", StringComparison.Ordinal))
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw result.Error ?? new InvalidOperationException("Database migration failed.");
        }
    }
}
