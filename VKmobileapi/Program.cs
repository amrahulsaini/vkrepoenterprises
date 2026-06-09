using System.IO.Compression;
using Microsoft.Extensions.FileProviders;
using MySqlConnector;
using VKmobileapi;
using VKmobileapi.Data;

LocalEnv.LoadBestEffort();
DbFactory.Init();

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

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

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

static bool IsTenantBoundByBody(PathString path)
{
    var s = path.Value ?? "";
    return s.Equals("/api/mobile/register", StringComparison.OrdinalIgnoreCase)
        || s.Equals("/api/mobile/login",    StringComparison.OrdinalIgnoreCase)
        || s.Equals("/api/mobile/agencies", StringComparison.OrdinalIgnoreCase)
        || s.Equals("/api/mobile/kyc/aadhaar/otp",    StringComparison.OrdinalIgnoreCase)
        || s.Equals("/api/mobile/kyc/aadhaar/verify", StringComparison.OrdinalIgnoreCase)
        || s.Equals("/api/mobile/kyc/resubmit",       StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("/api/mobile/otp/", StringComparison.OrdinalIgnoreCase)
        || s.Equals("/api/mobile/check-mobile", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("/api/mobile/cache/", StringComparison.OrdinalIgnoreCase)
        || s.StartsWith("/uploads/",        StringComparison.OrdinalIgnoreCase)
        || s == "/";
}
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
    else if (ctx.Request.Path.StartsWithSegments("/api/mobile") && !IsTenantBoundByBody(ctx.Request.Path))
    {
        ctx.Response.StatusCode = 401;
        await ctx.Response.WriteAsJsonAsync(new { success = false, message = "Please sign in again." });
        return;
    }
    await next();
});

app.MapControllers();

app.MapGet("/", () => Results.Ok(new { status = "VK Mobile API running" }));

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
