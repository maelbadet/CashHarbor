using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using CashHarbor.Application;
using CashHarbor.client;
using CashHarbor.client.Components;
using CashHarbor.Infra;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(dataDirectory);
builder.Configuration["Storage:Directory"] = dataDirectory;
var keyDirectory = Path.GetFullPath(builder.Configuration["Storage:KeyDirectory"] ?? Path.Combine(dataDirectory, "keys"));
Directory.CreateDirectory(keyDirectory);
var protection = builder.Services.AddDataProtection().SetApplicationName("CashHarbor.v1").PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));
var certificatePath = builder.Configuration["Security:KeyCertificatePath"];
if (!string.IsNullOrWhiteSpace(certificatePath))
{
    var passwordFile = builder.Configuration["Security:KeyCertificatePasswordFile"] ?? throw new InvalidOperationException("Certificate password file is required.");
    protection.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certificatePath, File.ReadAllText(passwordFile).TrimEnd('\r', '\n'), X509KeyStorageFlags.EphemeralKeySet));
}
else if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("Production requires Security:KeyCertificatePath and Security:KeyCertificatePasswordFile. See docs/DEPLOYMENT.md.");
Uri? publicOrigin = null;
if (!builder.Environment.IsDevelopment() && (!Uri.TryCreate(builder.Configuration["Security:PublicOrigin"], UriKind.Absolute, out publicOrigin) || publicOrigin.Scheme != "https" || publicOrigin.AbsolutePath != "/" || publicOrigin.Query.Length > 0 || publicOrigin.UserInfo.Length > 0))
    throw new InvalidOperationException("Security:PublicOrigin must be the exact HTTPS origin.");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor; options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.TryParse(builder.Configuration["Security:ProxyAddress"], out var proxy) ? proxy : IPAddress.Loopback);
});
builder.Services.AddDbContextFactory<VaultDb>(options => options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "cashharbor.db")};Foreign Keys=True;Default Timeout=10"));
builder.Services.AddSingleton<StorageGate>(); builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IWorkspaceStore, EncryptedWorkspaceStore>(); builder.Services.AddScoped<IIdentityService, IdentityService>(); builder.Services.AddScoped<FinanceService>();
builder.Services.AddScoped<LegacyAccessMigrator>();
builder.Services.AddScoped<LoanService>();
builder.Services.AddSingleton<ReminderGate>();
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddSingleton<IReminderMailer, SmtpReminderMailer>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<SpaceContext>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PortfolioGate>();
builder.Services.AddScoped<IPortfolio, Portfolio>();
builder.Services.AddHttpClient<IMarketPrices, MarketPrices>(c => { c.Timeout = TimeSpan.FromSeconds(8); c.DefaultRequestHeaders.UserAgent.ParseAdd("CashHarbor/1.0"); });
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "__Host-CashHarbor"; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30); options.SlidingExpiration = false; options.LoginPath = "/connexion";
    options.Events.OnValidatePrincipal = async context =>
    {
        if (!Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) || !await context.HttpContext.RequestServices.GetRequiredService<IIdentityService>().IsValidAsync(id, context.Principal?.FindFirstValue("version") ?? "")) context.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(); builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-CashHarbor-CSRF"; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Strict; options.Cookie.HttpOnly = true;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("write", http => RateLimitPartition.GetFixedWindowLimiter(http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => new() { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => { o.ValueLengthLimit = 4096; o.ValueCountLimit = 40; o.MultipartBodyLengthLimit = 65536; });
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 65536;
    if (builder.Configuration["Security:TlsCertificatePath"] is { Length: > 0 } tlsPath)
    {
        var password = File.ReadAllText(builder.Configuration["Security:TlsCertificatePasswordFile"] ?? throw new InvalidOperationException("TLS password file required.")).TrimEnd('\r', '\n');
        // Windows Schannel needs a key container for TLS; Linux supports ephemeral keys.
        o.ConfigureHttpsDefaults(https => https.ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(tlsPath, password, OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet));
    }
});
builder.Services.AddRazorComponents();
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VaultDb>>(); await using var db = await factory.CreateDbContextAsync();
    await DatabaseSchema.InitializeAsync(db, TimeProvider.System);
}
if (args.Contains("--classify-accounts")) { await AccessClassification.RunAsync(app.Services); return; }
if (args.Contains("--setup") || args.Contains("--reset-user")) { await Provisioning.RunAsync(app.Services, args.Contains("--reset-user")); return; }
app.UseExceptionHandler("/erreur"); app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use(async (context, next) =>
{
    if (context.Request.Path != "/health" && (!context.Request.IsHttps || (publicOrigin != null && !string.Equals(context.Request.Host.Value, publicOrigin.Authority, StringComparison.OrdinalIgnoreCase))))
    { context.Response.StatusCode = 400; await context.Response.WriteAsync("HTTPS et hôte configuré requis."); return; }
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
    context.Response.Headers.XContentTypeOptions = "nosniff"; context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer"; context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
    context.Response.Headers.CacheControl = "no-store"; await next();
});
app.UseStaticFiles();
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseAntiforgery();
app.Use(async (context, next) =>
{
    var key = context.Request.Query["espace"].ToString();
    if (key.Length > 0 && key is not ("personnel" or "couple")) { context.Response.StatusCode = 400; return; }
    if (context.User.Identity?.IsAuthenticated == true && HttpMethods.IsGet(context.Request.Method) &&
        (key.Length > 0 || context.Request.Query["vue"].ToString() is "commun" or "personnel"))
        context.Response.Cookies.Append(SpaceContext.CookieName, SpaceContext.Resolve(context.Request), new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(180) });
    await next();
});
app.MapGet("/health", () => Results.Text("ok")); app.MapCashHarborEndpoints(); app.MapRazorComponents<App>(); app.Run();
public partial class Program;
