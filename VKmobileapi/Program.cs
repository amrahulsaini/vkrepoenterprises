using MySqlConnector;
using VKmobileapi.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Allow any origin — lock down in production
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

app.UseCors();
app.MapControllers();

app.MapGet("/", () => Results.Ok(new { status = "VK Mobile API running" }));

// Warm up connection pool on startup so first real request reuses a live socket
_ = Task.Run(async () =>
{
    try
    {
        await using var conn = DbFactory.Create();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync();
    }
    catch { }
});

app.Run();
