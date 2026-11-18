using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CashHarbor.Application;
using CashHarbor.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using CashHarbor.client.Components.Shared;
namespace CashHarbor.client;
public static class Endpoints
{
    public static void MapCashHarborEndpoints(this WebApplication app)
    {
        app.MapPost("/portfolio/{action}", async (string action, HttpContext http, IAntiforgery csrf, IPortfolio portfolio) =>
        {
            try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var f = await http.Request.ReadFormAsync(); var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (f["espace"].ToString() is not ("personnel" or "couple")) return Results.BadRequest();
            var couple = f["espace"] == "couple"; var back = "/placements?espace=" + (couple ? "couple" : "personnel");
            try
            {
                decimal D(string key) => decimal.Parse(f[key].ToString(), CultureInfo.InvariantCulture);
                if (action == "add") await portfolio.SaveAsync(actor, couple, new(Guid.Empty, f["name"].ToString().Trim(), f["kind"].ToString(), f["symbol"].ToString().Trim(), D("quantity"), D("invested"), DateOnly.ParseExact(f["bought"].ToString(), "yyyy-MM-dd"), string.IsNullOrEmpty(f["manual"]) ? null : D("manual")));
                else if (action == "delete") await portfolio.DeleteAsync(actor, couple, Guid.Parse(f["id"]!)); else return Results.NotFound();
                return Results.LocalRedirect(back + "&succes=1");
            }
            catch (Exception e) when (e is RuleException or FormatException or OverflowException) { return Results.LocalRedirect(back + "&erreur=" + Uri.EscapeDataString("Vérifiez les champs de la position.")); }
        }).RequireAuthorization().RequireRateLimiting("write");
        app.MapPost("/auth/login", async (HttpContext http, IAntiforgery csrf, IIdentityService identity) =>
        {
            try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest("Formulaire expiré. Rechargez la page."); }
            var form = await http.Request.ReadFormAsync();
            var session = await identity.LoginAsync(form["email"].ToString(), form["password"].ToString(), form["code"].ToString());
            if (session == null) return Results.LocalRedirect("/connexion?echec=1");
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, session.Id.ToString()), new Claim(ClaimTypes.Name, session.Email), new Claim("version", session.Version)], CookieAuthenticationDefaults.AuthenticationScheme));
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
            return Results.LocalRedirect("/");
        }).RequireRateLimiting("login").AllowAnonymous();
        app.MapPost("/auth/logout", async (HttpContext http, IAntiforgery csrf, IIdentityService identity) =>
        {
            try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await identity.RevokeSessionsAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!));
            await http.SignOutAsync(); return Results.LocalRedirect("/connexion");
        }).RequireAuthorization();
        app.MapPost("/actions/{action}", WriteAsync).RequireAuthorization().RequireRateLimiting("write");
        app.MapGet("/export/analytics.csv", async (HttpContext http, FinanceService finance, SpaceContext space) =>
        {
            try
            {
                var workspace = space.Select(await finance.ReadAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!)));
                var report = FinancialAnalytics.Build(workspace, AnalyticsFilters.Parse(http.Request.Query, finance.Today) with { Scope = space.Scope }, finance.Today);
                return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(FinancialAnalytics.ExportCsv(report))).ToArray(), "text/csv; charset=utf-8", "cashharbor-analyse.csv");
            }
            catch (RuleException e) { return Results.BadRequest(e.Message); }
        }).RequireAuthorization().RequireRateLimiting("write");
        app.MapGet("/export/loan/{id:guid}/csv", async (Guid id, HttpContext http, FinanceService finance, SpaceContext space) =>
        {
            var w = space.Select(await finance.ReadAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!)));
            var loan = w.Loans.SingleOrDefault(l => l.Id == id);
            return loan == null ? Results.NotFound() : Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(LoanService.ExportSchedule(loan))).ToArray(), "text/csv; charset=utf-8", "cashharbor-amortissement.csv");
        }).RequireAuthorization().RequireRateLimiting("write");
        app.MapGet("/export/{format}", async (string format, HttpContext http, FinanceService finance, SpaceContext space) =>
        {
            var w = space.Select(await finance.ReadAsync(Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!)));
            return format switch
            {
                "csv" => Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(FinanceService.ExportCsv(w))).ToArray(), "text/csv; charset=utf-8", "cashharbor-operations.csv"),
                "json" => Results.File(JsonSerializer.SerializeToUtf8Bytes(w, new JsonSerializerOptions { WriteIndented = true }), "application/json", "cashharbor-foyer.json"),
                _ => Results.NotFound()
            };
        }).RequireAuthorization().RequireRateLimiting("write");
    }
    private static async Task<IResult> WriteAsync(string action, HttpContext http, IAntiforgery csrf, FinanceService finance, IIdentityService identity, LoanService loans, IReminderService reminders, SpaceContext space)
    {
        try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.BadRequest("Formulaire expiré. Rechargez la page."); }
        var f = await http.Request.ReadFormAsync(); var actor = Guid.Parse(http.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string S(string name) => f[name].ToString(); Guid G(string name) => Guid.Parse(S(name));
        Guid? O(string name) => string.IsNullOrEmpty(S(name)) ? null : G(name);
        decimal M(string name) => decimal.Parse(S(name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        DateOnly D(string name) => DateOnly.ParseExact(S(name), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var back = action.Split('-')[0] switch { "account" => "/comptes", "transaction" => "/operations", "goal" => "/objectifs", "recurrence" => "/echeances", "loan" => "/prets", _ => "/parametres" };
        var spaceKey = S("espace") is "personnel" or "couple" ? S("espace") : space.Key;
        var visibility = spaceKey == "couple" ? AccountVisibility.Joint : AccountVisibility.Personal;
        try
        {
            if (S("espace").Length > 0)
            {
                if (S("espace") is not ("personnel" or "couple")) throw new RuleException("Espace inconnu.");
                var current = WorkspaceSpaces.Select(await finance.ReadAsync(actor), visibility);
                void Require(bool ok) { if (!ok) throw new RuleException("Élément introuvable dans cet espace. Ouvrez l’espace correspondant."); }
                if (action.StartsWith("account-") && action != "account-add") Require(current.Accounts.Any(a => a.Id == G("id")));
                if (action == "transaction-delete") Require(current.Transactions.Any(t => t.Id == G("id")));
                if (action.StartsWith("goal-") && S("id").Length > 0) Require(current.Goals.Any(g => g.Id == G("id")));
                if (action.StartsWith("loan-") && S("id").Length > 0) Require(current.Loans.Any(l => l.Id == G("id")));
                if (action is "recurrence-edit" or "recurrence-delete") Require(current.Recurrences.Any(r => r.Id == G("id")));
                if (S("account").Length > 0) Require(current.Accounts.Any(a => a.Id == G("account")));
                if (S("target").Length > 0 && action == "transaction-add") Require(current.Accounts.Any(a => a.Id == G("target")));
                if (S("visibility").Length > 0) Require(Enum.Parse<AccountVisibility>(S("visibility")) == visibility);
            }
            switch (action)
            {
                case "smtp-save": await reminders.SaveSmtpAsync(actor, new(S("host"), int.Parse(S("port"), CultureInfo.InvariantCulture), S("username"), S("from"), Enum.Parse<SmtpEncryption>(S("encryption"))), S("smtpPassword")); break;
                case "reminder-save": await reminders.SavePreferencesAsync(actor, S("enabled") == "on", int.Parse(S("day"), CultureInfo.InvariantCulture), int.Parse(S("hour"), CultureInfo.InvariantCulture)); break;
                case "reminder-test": await reminders.SendTestAsync(actor); break;
                case "loan-save":
                    await loans.SaveAsync(actor, O("id"), new(S("name"), S("lender"), Enum.Parse<LoanKind>(S("loanKind")), M("principal"), M("rate"), int.Parse(S("months"), CultureInfo.InvariantCulture), D("firstDue"), Enum.Parse<LoanInsuranceKind>(S("insuranceKind")), M("insurance"), M("insuranceRate"), M("fees"), string.IsNullOrEmpty(S("apr")) ? null : M("apr"), int.Parse(S("initialPaid"), CultureInfo.InvariantCulture), Enum.Parse<AccountVisibility>(S("visibility")), O("account"))); break;
                case "loan-account": await loans.SetPaymentAccountAsync(actor, G("id"), O("account")); back += "/" + G("id"); break;
                case "loan-confirm": await loans.ConfirmAsync(actor, G("id"), int.Parse(S("number"), CultureInfo.InvariantCulture), D("paidOn"), S("mode") == "expense"); back += "/" + G("id"); break;
                case "loan-undo": await loans.UndoLastAsync(actor, G("id"), int.Parse(S("number"), CultureInfo.InvariantCulture)); back += "/" + G("id"); break;
                case "loan-delete": await loans.DeleteAsync(actor, G("id")); break;
                case "account-add": await finance.AddAccountAsync(actor, S("name"), S("institution"), Enum.Parse<AccountKind>(S("kind")), M("opening"), D("date"), Enum.Parse<AccountVisibility>(S("visibility")), S("product")); break;
                case "account-share": await finance.ShareAccountAsync(actor, G("id"), S("confirm") == "on"); spaceKey = "couple"; break;
                case "account-balance": await finance.RecordBalanceAsync(actor, G("id"), M("balance"), D("date")); break;
                case "account-edit": await finance.RenameAccountAsync(actor, G("id"), S("name"), S("institution")); break;
                case "account-delete": await finance.DeleteAccountAsync(actor, G("id")); break;
                case "transaction-add": await finance.AddTransactionAsync(actor, G("account"), S("label"), S("category"), M("amount"), D("date"), Enum.Parse<EntryKind>(S("kind")), O("target")); break;
                case "transaction-delete": await finance.DeleteTransactionAsync(actor, G("id")); break;
                case "goal-save": await finance.SaveGoalAsync(actor, O("id"), S("name"), M("target"), M("saved"), string.IsNullOrEmpty(S("deadline")) ? null : D("deadline"), string.IsNullOrEmpty(S("visibility")) ? AccountVisibility.Personal : Enum.Parse<AccountVisibility>(S("visibility"))); break;
                case "goal-delete": await finance.DeleteGoalAsync(actor, G("id")); break;
                case "recurrence-add": await finance.AddRecurrenceAsync(actor, G("account"), S("label"), S("category"), M("amount"), D("next"), S("essential") == "on", string.IsNullOrEmpty(S("frequency")) ? RecurrenceFrequency.Monthly : Enum.Parse<RecurrenceFrequency>(S("frequency"))); break;
                case "recurrence-edit": await finance.UpdateRecurrenceAsync(actor, G("id"), S("label"), S("category"), M("amount"), D("next"), S("essential") == "on", Enum.Parse<RecurrenceFrequency>(S("frequency"))); break;
                case "recurrence-delete": await finance.DeleteRecurrenceAsync(actor, G("id")); break;
                case "recurrence-book": await finance.BookDueAsync(actor, visibility); break;
                case "settings-save": await finance.SavePolicyAsync(actor, S("name"), int.Parse(S("months"), CultureInfo.InvariantCulture), M("buffer")); break;
                case "password-change": await identity.ChangePasswordAsync(actor, S("current"), S("password"), S("code")); await http.SignOutAsync(); return Results.LocalRedirect("/connexion?modifie=1");
                default: return Results.NotFound();
            }
            return Results.LocalRedirect(back + "?succes=1&espace=" + spaceKey);
        }
        catch (RuleException e) { return Results.LocalRedirect(back + "?espace=" + spaceKey + "&erreur=" + Uri.EscapeDataString(e.Message)); }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException) { return Results.LocalRedirect(back + "?erreur=" + Uri.EscapeDataString("Vérifiez les champs du formulaire : montant, date et sélection.")); }
    }
}
