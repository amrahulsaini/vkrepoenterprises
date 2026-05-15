using Microsoft.Extensions.FileProviders;
using MySqlConnector;
using VKmobileapi;
using VKmobileapi.Data;

LocalEnv.LoadBestEffort();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Allow any origin — lock down in production
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Serve uploaded PFP / KYC files under /uploads
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);
MobileRepository.UploadsPath = uploadsPath;
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath  = "/uploads"
});

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
