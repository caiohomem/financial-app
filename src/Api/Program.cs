var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration["DATABASE_URL"];

var app = builder.Build();

app.MapGet("/api/health", () =>
{
    _ = connectionString;
    return Results.Ok(new { status = "ok" });
});

app.Run();
