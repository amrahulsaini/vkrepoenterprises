using System.IO.Compression;
using Microsoft.Extensions.FileProviders;
using MySqlConnector;
using VKmobileapi;
using VKmobileapi.Data;

LocalEnv.LoadBestEffort();
DbFactory.Init();   // capture DB config + build the default/master connections

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddResponseCompression(opts =>
{
    opts.EnableForHttps = true;
    opts.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    opts.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes.Concat(
        new[] { "application/json", "text/plain" });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    opts => opts.Level = CompressionLevel.Fastest);

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

app.UseResponseCompression();
app.UseCors();

// ── Multi-tenant routing ─────────────────────────────────────────────────────
// A request carrying a valid mobile tenant token (issued at login) is routed,
// for its whole lifetime, to that agency's own database. register / login
// carry no token yet — they bind the tenant themselves from the request body.
app.Use(async (ctx, next) =>
{
    var token = ctx.Request.Headers["X-Tenant-Token"].FirstOrDefault();
    if (!string.IsNullOrEmpty(token))
    {
        var slug = MobileToken.Verify(token);
        if (slug == null)
        {
            ctx.Response.StatusCode = 401;
            await ctx.Response.WriteAsJsonAsync(new { success = false, message = "Session expired — please sign in again." });
            return;
        }
        TenantContext.UseAgency(slug);
    }
    await next();
});

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
