using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using CashHarbor.Application;
using CashHarbor.Domain;
using CashHarbor.Infra;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

// Dependency-free executable regression suite. Real SQLite and real HTTPS host.
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var runDirectory = Path.Combine(root, ".test-artifacts", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runDirectory);
var passed = 0; var executed = 0;
async Task Test(string name, Func<Task> test)
{
    executed++;
    try { await test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.ExitCode = 1; }
}
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Reject(Func<Task> work) { try { await work(); } catch (RuleException) { return; } throw new Exception("Expected a rejected business rule."); }
var clock = new TestClock(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));
var services = new ServiceCollection();
services.AddLogging(); services.AddSingleton<TimeProvider>(clock); services.AddSingleton<StorageGate>();
services.AddDataProtection().SetApplicationName("CashHarbor.v1").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(runDirectory, "keys")));
services.AddDbContextFactory<VaultDb>(o => o.UseSqlite($"Data Source={Path.Combine(runDirectory, "test.db")}"));
services.AddScoped<IIdentityService, IdentityService>(); services.AddScoped<IWorkspaceStore, EncryptedWorkspaceStore>(); services.AddScoped<FinanceService>();
await using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<VaultDb>>();
await using (var db = await factory.CreateDbContextAsync()) await db.Database.EnsureCreatedAsync();
var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
var finance = scope.ServiceProvider.GetRequiredService<FinanceService>();
var secret = Totp.NewSecret(); var password = "A-unique-test-password-123!";
await identity.ProvisionAsync("first@example.test", password, secret, Totp.Code(secret, clock.Now.ToUnixTimeSeconds() / 30), false);
Guid actor; await using (var db = await factory.CreateDbContextAsync()) actor = (await db.Users.SingleAsync()).Id;
clock.Now = clock.Now.AddSeconds(60);

await Test("RFC 6238 reference vectors", () =>
{
    var key = Totp.Encode(Encoding.ASCII.GetBytes("12345678901234567890"));
    var vectors = new (long Time, string Code)[] { (59, "94287082"), (1111111109, "07081804"), (1111111111, "14050471"), (1234567890, "89005924"), (2000000000, "69279037"), (20000000000, "65353130") };
    foreach (var v in vectors) Check(Totp.Code(key, v.Time / 30, 8) == v.Code, "RFC vector mismatch.");
    return Task.CompletedTask;
});
await Test("MFA, replay prevention, and durable account lockout", async () =>
{
    Check(await identity.LoginAsync("first@example.test", password, "bad") == null, "MFA must be mandatory.");
    var code = Totp.Code(secret, clock.Now.ToUnixTimeSeconds() / 30);
    var session = await identity.LoginAsync(" FIRST@example.test ", password, code);
    Check(session != null, "Valid credentials rejected.");
    Check(await identity.LoginAsync("first@example.test", password, code) == null, "TOTP replay accepted.");
    await identity.RevokeSessionsAsync(actor);
    Check(!await identity.IsValidAsync(actor, session!.Version), "Revoked session remains valid.");
    for (var i = 0; i < 5; i++) await identity.LoginAsync("first@example.test", "wrong", code);
    clock.Now = clock.Now.AddSeconds(60);
    using var secondScope = provider.CreateScope(); var secondIdentity = secondScope.ServiceProvider.GetRequiredService<IIdentityService>();
    Check(await secondIdentity.LoginAsync("first@example.test", password, Totp.Code(secret, clock.Now.ToUnixTimeSeconds() / 30)) == null, "Lockout did not persist.");
    clock.Now = clock.Now.AddMinutes(16);
    Check(await secondIdentity.LoginAsync("first@example.test", password, Totp.Code(secret, clock.Now.ToUnixTimeSeconds() / 30)) != null, "Lockout did not expire.");
});
await Test("Only two provisioned users; reset revokes sessions", async () =>
{
    var secondSecret = Totp.NewSecret();
    await identity.ProvisionAsync("second@example.test", password, secondSecret, Totp.Code(secondSecret, clock.Now.ToUnixTimeSeconds() / 30), false);
    await Reject(() => identity.ProvisionAsync("third@example.test", password, secondSecret, Totp.Code(secondSecret, clock.Now.ToUnixTimeSeconds() / 30), false));
    clock.Now = clock.Now.AddMinutes(1);
    var old = await identity.LoginAsync("first@example.test", password, Totp.Code(secret, clock.Now.ToUnixTimeSeconds() / 30));
    var replacement = Totp.NewSecret();
    await identity.ProvisionAsync("first@example.test", password, replacement, Totp.Code(replacement, clock.Now.ToUnixTimeSeconds() / 30), true);
    Check(!await identity.IsValidAsync(actor, old!.Version), "Reset did not revoke sessions.");
});
var today = finance.Today;
await Test("Market history: currency, ETF type isolation, UTC dates, caching and provider failure", MarketHistoryRegression.Run);
await Test("Portfolio privacy, encrypted storage and constant-capital simulation", async () =>
{
    var directory = Path.Combine(runDirectory, "portfolio-tests"); Directory.CreateDirectory(directory);
    var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Directory"] = directory }).Build();
    var portfolio = new Portfolio(scope.ServiceProvider.GetRequiredService<IWorkspaceStore>(), provider.GetRequiredService<IDataProtectionProvider>(), configuration, new PortfolioGate());
    Guid partner; await using (var db = await factory.CreateDbContextAsync()) partner = (await db.Users.SingleAsync(u => u.Id != actor)).Id;
    await portfolio.SaveAsync(actor, false, new(Guid.Empty, "SECRET_ETH", "crypto", "ethereum", .5m, 750, today.AddYears(-2)));
    await portfolio.SaveAsync(actor, true, new(Guid.Empty, "COMMON_ETF", "etf", "TEST", 2m, 750, today.AddMonths(-10), 450));
    Check((await portfolio.ReadAsync(actor, false)).Count == 1 && (await portfolio.ReadAsync(partner, false)).Count == 0, "Personal portfolio leaked.");
    Check((await portfolio.ReadAsync(partner, true)).Single().Name == "COMMON_ETF", "Common portfolio missing.");
    var id = (await portfolio.ReadAsync(actor, false)).Single().Id;
    await Reject(() => portfolio.DeleteAsync(partner, false, id));
    foreach (var file in Directory.GetFiles(directory)) Check(!(await File.ReadAllTextAsync(file)).Contains("SECRET_ETH"), "Portfolio stored unencrypted.");
    Check(InvestmentMath.Value(.5m, 3000m) == 1500m, "Fractional quantity lost precision.");
    Check(InvestmentMath.Scenario(750, 0, today.AddYears(-2), today) == 750, "Zero rate changed capital.");
    Check(InvestmentMath.Scenario(750, 2, today, today) == 750, "Same-day simulation accrued interest.");
    Check(InvestmentMath.Scenario(750, 2, new(2025, 1, 1), new(2026, 1, 1)) is > 764m and < 766m, "Annual scenario incorrect.");
    var plans = new BudgetPlans(scope.ServiceProvider.GetRequiredService<IWorkspaceStore>(), provider.GetRequiredService<IDataProtectionProvider>(), configuration, new PortfolioGate());
    await plans.SaveAsync(actor, false, "2026-11", new(2500, 50, 30, 500, 1200, 100, new() { ["Courses"] = 400 }));
    Check((await plans.ReadAsync(actor, false, "2026-11")).Income == 2500 && (await plans.ReadAsync(partner, false, "2026-11")).Income == 0 && (await plans.ReadAsync(actor, false, "2026-12")).Income == 0, "Monthly plan leaked across users or months.");
    await plans.SaveAsync(actor, true, "2026-11", new(4000)); Check((await plans.ReadAsync(partner, true, "2026-11")).Income == 4000, "Joint budget missing for partner.");
    await Reject(() => plans.SaveAsync(actor, false, "2026-11", new(2500, int.MaxValue, 30)));
});
await Test("Exact analytics, scopes, partial periods, grouping and CSV", AnalyticsRegression.Run);
await Test("Flexible projections: median, zero rate, timing, inflation and horizons", () =>
{
    Check(InvestmentProjection.Median(new decimal[] { 20, -10, 5, 10 }) == 7.5m, "Even median incorrect.");
    Check(InvestmentProjection.Median(new decimal[] { -20, 8, 12 }) == 8m, "Odd median incorrect.");
    foreach (var years in new[] { 1, 2, 5, 10, 17, 20, 30, 41, 60 }) { var row = InvestmentProjection.Calculate(750, 150, years, 0, 0, 0)[^1]; Check(row.Nominal == 750 + 150 * 12 * years && row.Contributed == row.Nominal && row.PurchasingPower == row.Nominal, "Zero-rate projection incorrect."); }
    var annual = InvestmentProjection.Calculate(1000, 0, 1, 10, 0, 0)[^1]; Check(annual.Nominal == 1100, "Effective annual compounding incorrect.");
    var negative = InvestmentProjection.Calculate(1000, 0, 1, -10, 0, 0)[^1]; Check(negative.Nominal == 900, "Loss scenario incorrect.");
    var real = InvestmentProjection.Calculate(1000, 0, 1, 10, 0, 10)[^1]; Check(real.PurchasingPower == 1000, "Inflation adjustment incorrect.");
    try { InvestmentProjection.Calculate(0, 150, 61, 5, 0, 0); throw new Exception("Invalid horizon accepted."); } catch (RuleException) { }
    return Task.CompletedTask;
});
await Test("Separate personal/couple spaces, expenses, exports and recurrence booking", SpaceRegression.Run);
await Test("Monthly reminders: consent, encryption, Paris time, retry and persistent deduplication", () => ReminderRegression.Run(factory, provider.GetRequiredService<IDataProtectionProvider>(), runDirectory));
await finance.AddAccountAsync(actor, "Private Current", "Private Institution", AccountKind.Current, 1000m, today.AddMonths(-3));
await finance.AddAccountAsync(actor, "Savings", "Bank", AccountKind.Savings, 200m, today.AddMonths(-3));
var state = await finance.ReadAsync(actor); var a = state.Accounts[0].Id; var b = state.Accounts[1].Id;
await Test("Exact money, ledger balances, and historical boundaries", async () =>
{
    await finance.AddTransactionAsync(actor, a, "Coffee", "Loisirs", 0.10m, today, EntryKind.Expense);
    await finance.AddTransactionAsync(actor, a, "Coffee", "Loisirs", 0.20m, today, EntryKind.Expense);
    var w = await finance.ReadAsync(actor); Check(w.Balance(a) == 99970, "Money lost precision.");
    Check(w.Balance(a, today.AddYears(-2)) == 0, "Opening balance leaked before opening date.");
    await Reject(() => finance.AddTransactionAsync(actor, a, "Bad", "Autres", 0.001m, today, EntryKind.Expense));
    await Reject(() => finance.AddTransactionAsync(actor, a, "Bad", "Autres", -1m, today, EntryKind.Expense));
    await Reject(() => finance.AddTransactionAsync(actor, a, "Bad", "Autres", 1m, today.AddDays(1), EntryKind.Expense));
    await Reject(() => finance.AddTransactionAsync(actor, a, "Bad", "Autres", 1m, today.AddYears(-1), EntryKind.Expense));
    await Reject(() => finance.AddTransactionAsync(actor, Guid.NewGuid(), "Bad", "Autres", 1m, today, EntryKind.Expense));
});
await Test("Atomic transfers and paired deletion preserve net worth", async () =>
{
    var before = await finance.ReadAsync(actor); var total = before.Accounts.Sum(x => before.Balance(x.Id));
    await finance.AddTransactionAsync(actor, a, "Transfer", "ignored", 150m, today, EntryKind.Transfer, b);
    var w = await finance.ReadAsync(actor); Check(w.Accounts.Sum(x => w.Balance(x.Id)) == total, "Transfer changed net worth.");
    Check(w.Balance(b) == 35000, "Destination not credited.");
    var t = w.Transactions.Single(x => x.AccountId == a && x.Kind == EntryKind.Transfer);
    await finance.DeleteTransactionAsync(actor, t.Id);
    w = await finance.ReadAsync(actor); Check(!w.Transactions.Any(x => x.TransferId == t.TransferId), "Orphan transfer leg.");
    Check(w.Balance(b) == 20000, "Transfer removal did not reverse balances.");
    await Reject(() => finance.AddTransactionAsync(actor, a, "Bad", "Autres", 1m, today, EntryKind.Transfer, a));
    await Reject(() => finance.DeleteAccountAsync(actor, a));
});
await Test("Month-end recurrences are idempotent across short months", async () =>
{
    await finance.AddRecurrenceAsync(actor, a, "Monthly", "Logement", -10m, new DateOnly(2026, 8, 31), true);
    await finance.BookDueAsync(actor); await finance.BookDueAsync(actor);
    var w = await finance.ReadAsync(actor); var r = w.Recurrences.Single();
    Check(r.NextDate == new DateOnly(2026, 11, 30), "Month-end recurrence drifted.");
    var dates = w.Transactions.Where(t => t.RecurrenceId == r.Id).Select(t => t.Date).ToArray();
    Check(dates.SequenceEqual(new[] { new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 31) }), "Recurrence duplicated or skipped.");
});
await Test("Concurrent writes do not lose data and unknown actors cannot write", async () =>
{
    var before = (await finance.ReadAsync(actor)).Transactions.Count;
    await Task.WhenAll(Enumerable.Range(0, 12).Select(i => finance.AddTransactionAsync(actor, a, "Concurrent " + i, "Autres", 1m, today, EntryKind.Income)));
    Check((await finance.ReadAsync(actor)).Transactions.Count == before + 12, "Lost concurrent write.");
    try { await finance.AddAccountAsync(Guid.NewGuid(), "Intruder", "Bank", AccountKind.Current, 0m, today); throw new Exception("Unknown actor accepted."); } catch (UnauthorizedAccessException) { }
});
await Test("Financial content is encrypted and CSV neutralizes formulas", async () =>
{
    await finance.AddTransactionAsync(actor, a, "=HYPERLINK(\"bad\")", "Autres", 1m, today, EntryKind.Expense);
    await using var db = await factory.CreateDbContextAsync(); var row = await db.Vaults.SingleAsync();
    Check(!row.Payload.Contains("Private Current") && !row.Payload.Contains("HYPERLINK"), "Plaintext financial data.");
    var csv = FinanceService.ExportCsv(await finance.ReadAsync(actor)); Check(csv.Contains("'\u003dHYPERLINK"), "CSV injection not neutralized.");
    Check(!await db.Users.AnyAsync(u => u.ProtectedSecret == secret), "Plaintext MFA key.");
});
await Test("Invalid goals and invalid updates do not partially persist", async () =>
{
    await Reject(() => finance.SaveGoalAsync(actor, null, "Bad", 100m, 101m, null));
    await Reject(() => finance.SavePolicyAsync(actor, "Bad", 0, 1m));
    var w = await finance.ReadAsync(actor); Check(w.Goals.Count == 0 && w.Name == "Notre foyer", "Invalid update partially persisted.");
});
await RecurrenceRegression.RunAsync(Test, scope.ServiceProvider, finance, actor, clock);
await LoanRegression.RunAsync(Test, scope.ServiceProvider, finance, actor, clock);
await AccessRegression.RunAsync(Test, scope.ServiceProvider, finance, actor, clock);
await Test("HTTPS host: authentication, CSRF, protected routes, export, MFA and logout", async () =>
{
    var httpDirectory = Path.Combine(runDirectory, "http"); Directory.CreateDirectory(httpDirectory);
    var certPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest("CN=127.0.0.1", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); req.CertificateExtensions.Add(san.Build());
    using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(7));
    var certPath = Path.Combine(httpDirectory, "test.pfx"); var passPath = Path.Combine(httpDirectory, "password");
    await File.WriteAllBytesAsync(certPath, cert.Export(X509ContentType.Pfx, certPassword)); await File.WriteAllTextAsync(passPath, certPassword);
    var httpServices = new ServiceCollection(); httpServices.AddLogging();
    var httpClock = new TestClock(DateTimeOffset.UtcNow.AddMinutes(-2)); httpServices.AddSingleton<TimeProvider>(httpClock); httpServices.AddSingleton<StorageGate>();
    httpServices.AddDataProtection().SetApplicationName("CashHarbor.v1").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(httpDirectory, "keys"))).ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword, X509KeyStorageFlags.EphemeralKeySet));
    httpServices.AddDbContextFactory<VaultDb>(o => o.UseSqlite($"Data Source={Path.Combine(httpDirectory, "cashharbor.db")}")); httpServices.AddScoped<IIdentityService, IdentityService>();
    httpServices.AddScoped<IWorkspaceStore, EncryptedWorkspaceStore>(); httpServices.AddScoped<FinanceService>(); httpServices.AddScoped<LoanService>();
    await using var httpProvider = httpServices.BuildServiceProvider();
    var httpFactory = httpProvider.GetRequiredService<IDbContextFactory<VaultDb>>(); await using (var db = await httpFactory.CreateDbContextAsync()) await db.Database.EnsureCreatedAsync();
    var httpSecret = Totp.NewSecret(); await httpProvider.GetRequiredService<IIdentityService>().ProvisionAsync("web@example.test", password, httpSecret, Totp.Code(httpSecret, httpClock.Now.ToUnixTimeSeconds() / 30), false);
    // Synthetic household for representative chart and responsive checks. Never production seed data.
    var sample = httpProvider.GetRequiredService<FinanceService>(); var sampleDate = sample.Today;
    Guid sampleActor; await using (var db = await httpFactory.CreateDbContextAsync()) sampleActor = (await db.Users.SingleAsync()).Id;
    await sample.AddAccountAsync(sampleActor, "Mon compte personnel", "Banque du foyer", AccountKind.Current, 1700m, sampleDate.AddMonths(-6));
    await sample.AddAccountAsync(sampleActor, "Épargne commune", "Banque du foyer", AccountKind.Savings, 7300m, sampleDate.AddMonths(-6), AccountVisibility.Joint, "Livret bancaire");
    var sampleAccount = (await sample.ReadAsync(sampleActor)).Accounts[0].Id;
    var sharedAccount = (await sample.ReadAsync(sampleActor)).Accounts.Single(a => a.Visibility == AccountVisibility.Joint).Id;
    for (var i = 5; i >= 0; i--)
    {
        var date = new DateOnly(sampleDate.Year, sampleDate.Month, Math.Min(10, sampleDate.Day)).AddMonths(-i);
        await sample.AddTransactionAsync(sampleActor, sampleAccount, "Salaire", "Salaire", 2450m + i * 20, date, EntryKind.Income);
        await sample.AddTransactionAsync(sampleActor, sampleAccount, "Loyer", "Logement", 850m, date, EntryKind.Expense);
        await sample.AddTransactionAsync(sampleActor, sampleAccount, "Courses de la semaine", "Courses", 245.35m + i * 12, date, EntryKind.Expense);
        await sample.AddTransactionAsync(sampleActor, sampleAccount, "Sorties et loisirs", "Loisirs", 120m + i * 17, date, EntryKind.Expense);
        await sample.RecordBalanceAsync(sampleActor, sharedAccount, 7300m + (5 - i) * 180m, date);
    }
    await sample.SaveGoalAsync(sampleActor, null, "Notre prochain voyage", 2500m, 870m, sampleDate.AddMonths(8), AccountVisibility.Joint);
    await sample.SaveGoalAsync(sampleActor, null, "Mon projet personnel", 1500m, 450m, sampleDate.AddMonths(8));
    await sample.AddRecurrenceAsync(sampleActor, sampleAccount, "Loyer", "Logement", -850m, sampleDate.AddDays(5), true);
    var partnerSecret = Totp.NewSecret();
    await httpProvider.GetRequiredService<IIdentityService>().ProvisionAsync("partner@example.test", password, partnerSecret, Totp.Code(partnerSecret, httpClock.Now.ToUnixTimeSeconds() / 30), false);
    Guid partnerActor; await using (var db = await httpFactory.CreateDbContextAsync()) partnerActor = await db.Users.Where(u => u.Email == "partner@example.test").Select(u => u.Id).SingleAsync();
    await sample.AddAccountAsync(partnerActor, "B_PRIVATE_ACCOUNT", "B_PRIVATE_BANK", AccountKind.Savings, 98765m, sampleDate.AddMonths(-6));
    var partnerAccount = (await sample.ReadAsync(partnerActor)).Accounts.Single(a => a.OwnerId == partnerActor).Id;
    await sample.AddTransactionAsync(partnerActor, partnerAccount, "B_PRIVATE_OPERATION", "Autres", 12345m, sampleDate, EntryKind.Income);
    await sample.AddRecurrenceAsync(partnerActor, partnerAccount, "B_PRIVATE_RECURRENCE", "Autres", -77m, sampleDate, true);
    await sample.SaveGoalAsync(partnerActor, null, "B_PRIVATE_GOAL", 100m, 20m, null);
    var sampleLoans = httpProvider.GetRequiredService<LoanService>();
    await sample.AddAccountAsync(sampleActor, "Compte joint", "Banque du foyer", AccountKind.Current, 2400m, sampleDate.AddMonths(-6), AccountVisibility.Joint);
    var jointPaymentAccount = (await sample.ReadAsync(sampleActor)).Accounts.Single(a => a.Name == "Compte joint").Id;
    await sampleLoans.SaveAsync(sampleActor, null, new("Prêt auto personnel", "Banque du foyer", LoanKind.Vehicle, 9800m, 4.5m, 48, sampleDate.AddMonths(-12), LoanInsuranceKind.FixedMonthly, 8m, 0, 80m, 4.9m, 10, AccountVisibility.Personal, sampleAccount));
    await sampleLoans.SaveAsync(sampleActor, null, new("Notre prêt immobilier", "Banque du foyer", LoanKind.Mortgage, 185000m, 3.45m, 240, sampleDate.AddMonths(-18), LoanInsuranceKind.InitialCapitalRate, 0, .3m, 1200m, 3.92m, 18, AccountVisibility.Joint, jointPaymentAccount));
    await sampleLoans.SaveAsync(partnerActor, null, new("B_PRIVATE_LOAN", "B_PRIVATE_LENDER", LoanKind.Consumer, 5000m, 5m, 36, sampleDate.AddMonths(-2), LoanInsuranceKind.FixedMonthly, 5m, 0, 0, null, 0, AccountVisibility.Personal, partnerAccount));
    var samplePrivateLoan = (await sample.ReadAsync(sampleActor)).Loans.Single(l => l.Visibility == AccountVisibility.Personal);
    var sampleSharedLoan = (await sample.ReadAsync(sampleActor)).Loans.Single(l => l.Visibility == AccountVisibility.Joint);
    var partnerLoan = (await sample.ReadAsync(partnerActor)).Loans.Single(l => l.OwnerId == partnerActor);
    await sampleLoans.ConfirmAsync(sampleActor, samplePrivateLoan.Id, 11, sampleDate, false);
    await sampleLoans.ConfirmAsync(partnerActor, partnerLoan.Id, 1, sampleDate, true);
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var origin = $"https://127.0.0.1:{port}";
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "CashHarbor.client"), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(Path.Combine(root, "CashHarbor.client/bin/Debug/net10.0/CashHarbor.client.dll"));
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; start.Environment["ASPNETCORE_URLS"] = origin;
    start.Environment["Storage__Directory"] = httpDirectory; start.Environment["Storage__KeyDirectory"] = Path.Combine(httpDirectory, "keys");
    start.Environment["Security__KeyCertificatePath"] = certPath; start.Environment["Security__KeyCertificatePasswordFile"] = passPath; start.Environment["Security__PublicOrigin"] = origin;
    start.Environment["Security__TlsCertificatePath"] = certPath; start.Environment["Security__TlsCertificatePasswordFile"] = passPath;
    start.Environment["Logging__LogLevel__Default"] = "Information";
    using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
    var cookies = new CookieContainer();
    // Only this loopback test accepts the temporary self-signed certificate.
    using var handler = new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
    using var client = new HttpClient(handler) { BaseAddress = new Uri(origin), Timeout = TimeSpan.FromSeconds(15) };
    try
    {
        var ready = false; string lastHealth = "no response";
        for (var i = 0; i < 24 && !process.HasExited; i++) { try { var health = await client.GetAsync("/health"); lastHealth = health.StatusCode + " " + await health.Content.ReadAsStringAsync(); if (health.IsSuccessStatusCode) { ready = true; break; } } catch (HttpRequestException e) { lastHealth = e.ToString(); } await Task.Delay(250); }
        if (!ready) Console.Error.WriteLine("Health failure: " + lastHealth);
        if (!ready) throw new Exception("Host did not start: " + (process.HasExited ? await errors : "timeout"));
        foreach (var route in new[] { "/", "/comptes", "/operations", "/objectifs", "/echeances", "/parametres", "/budget", "/projection", "/marches", "/prets", "/prets/" + samplePrivateLoan.Id, "/export/loan/" + samplePrivateLoan.Id + "/csv", "/export/csv", "/export/json" })
            Check((await client.GetAsync(route)).StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, "Anonymous access to " + route);
        var login = await client.GetAsync("/connexion"); var html = await login.Content.ReadAsStringAsync(); Check(login.IsSuccessStatusCode && html.Contains("Votre espace vous attend"), "Login page did not render.");
        var cssPath = WebUtility.HtmlDecode(Regex.Match(html, "rel=\"stylesheet\" href=\"([^\"]+)\"").Groups[1].Value);
        var cssResponse = await client.GetAsync(cssPath);
        Check(cssResponse.IsSuccessStatusCode && cssResponse.Content.Headers.ContentType?.MediaType == "text/css" && (await cssResponse.Content.ReadAsStringAsync()).Contains("--cream"), "Stylesheet unavailable: " + cssPath + " / " + cssResponse.StatusCode);
        Check((await client.GetAsync("/harbor.svg")).IsSuccessStatusCode, "Brand icon unavailable.");
        Check(login.Headers.Contains("Content-Security-Policy") && login.Headers.CacheControl?.NoStore == true, "Missing security headers.");
        var token = Token(html);
        Check((await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "web@example.test" }))).StatusCode == HttpStatusCode.BadRequest, "Login CSRF accepted.");
        var values = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["email"] = "web@example.test", ["password"] = password, ["code"] = Totp.Code(httpSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) };
        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(values)); Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/", "Login failed.");
        var authCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-CashHarbor="));
        Check(authCookie.Contains("secure", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && authCookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "Cookie is missing protections.");
        foreach (var route in new[] { "/", "/comptes", "/operations", "/objectifs", "/echeances", "/parametres", "/budget", "/projection", "/marches", "/prets", "/prets/" + samplePrivateLoan.Id }) Check((await client.GetAsync(route)).IsSuccessStatusCode, "Authenticated route failed: " + route);
        Check((await client.PostAsync("/actions/account-add", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode == HttpStatusCode.BadRequest, "Write CSRF accepted.");
        html = await client.GetStringAsync("/comptes"); token = Token(html);
        response = await client.PostAsync("/actions/account-add", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["name"] = "Web Account", ["institution"] = "Bank", ["kind"] = "Current", ["visibility"] = "Personal", ["opening"] = "123.45", ["date"] = DateTime.Today.ToString("yyyy-MM-dd") }));
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("succes=1"), "Account creation HTTP failed.");
        Check((await client.GetStringAsync("/comptes")).Contains("Web Account"), "Created account not rendered.");
        Check((await client.GetAsync("/export/json")).IsSuccessStatusCode, "Export failed.");
        await HttpAccessRegression.RunAsync(client, origin, password, partnerSecret, sampleActor, sampleAccount, sharedAccount, partnerActor);
        if (Environment.GetEnvironmentVariable("CASHHARBOR_BROWSER") is { Length: > 0 })
        {
            var fixturePath = Path.Combine(httpDirectory, "ui-fixture.json");
            var cookieValues = cookies.GetCookies(new Uri(origin)).Cast<Cookie>().Select(c => new { name = c.Name, value = c.Value, url = origin, secure = c.Secure, httpOnly = c.HttpOnly, sameSite = "Strict" }).ToArray();
            await File.WriteAllTextAsync(fixturePath, System.Text.Json.JsonSerializer.Serialize(new { origin, cookies = cookieValues, loanRoutes = new[] { "/prets/" + samplePrivateLoan.Id + "?espace=personnel", "/prets/" + sampleSharedLoan.Id + "?espace=couple" } }));
            try
            {
                var browserStart = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                browserStart.ArgumentList.Add(Path.Combine(root, "scripts/verify-ui.mjs")); browserStart.ArgumentList.Add(fixturePath);
                using var browser = Process.Start(browserStart)!; var browserOutput = browser.StandardOutput.ReadToEndAsync(); var browserErrors = browser.StandardError.ReadToEndAsync();
                await browser.WaitForExitAsync(); Console.WriteLine(await browserOutput); if (browser.ExitCode != 0) Console.Error.WriteLine(await browserErrors); Check(browser.ExitCode == 0, "Browser verification failed.");
            }
            finally { File.Delete(fixturePath); }
        }
        using var badHost = new HttpRequestMessage(HttpMethod.Get, "/connexion"); badHost.Headers.Host = "attacker.example"; Check((await client.SendAsync(badHost)).StatusCode == HttpStatusCode.BadRequest, "Host validation bypass.");
        var oldCookieHeader = cookies.GetCookieHeader(new Uri(origin));
        html = await client.GetStringAsync("/"); token = Token(html);
        Check((await client.PostAsync("/auth/logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }))).StatusCode == HttpStatusCode.Redirect, "Logout failed.");
        using var stolenHandler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var stolenClient = new HttpClient(stolenHandler) { BaseAddress = new Uri(origin) }; stolenClient.DefaultRequestHeaders.Add("Cookie", oldCookieHeader);
        Check((await stolenClient.GetAsync("/comptes")).StatusCode == HttpStatusCode.Redirect, "Logged-out cookie was not revoked server-side.");
    }
    catch
    {
        if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); Console.Error.WriteLine(await output + await errors); throw;
    }
    finally { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); }
});
Console.WriteLine($"{passed}/{executed} suites passed. Temporary fixtures: {runDirectory}");
return Environment.ExitCode;

static string Token(string html)
{
    var match = Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"");
    if (!match.Success) throw new Exception("Missing antiforgery token."); return WebUtility.HtmlDecode(match.Groups[1].Value);
}
sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
