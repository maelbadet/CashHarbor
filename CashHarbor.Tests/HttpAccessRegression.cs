using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CashHarbor.Infra;
using CashHarbor.Domain;

internal static class HttpAccessRegression
{
    public static async Task RunAsync(HttpClient first, string origin, string password, string secondSecret, Guid firstActor, Guid firstAccount, Guid sharedAccount, Guid secondActor)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new(), ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var second = new HttpClient(handler) { BaseAddress = new Uri(origin) };
        var login = await second.GetStringAsync("/connexion");
        var response = await second.PostAsync("/auth/login", Form(new() { ["__RequestVerificationToken"] = Token(login), ["email"] = "partner@example.test", ["password"] = password, ["code"] = Totp.Code(secondSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) }));
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/", "Second login failed.");
        foreach (var route in new[] { "/", "/comptes", "/comptes?type=epargne", "/operations", "/operations?compte=" + firstAccount, "/objectifs", "/echeances", "/prets", "/parametres", "/export/json", "/export/csv" })
        {
            var a = await first.GetStringAsync(route); var b = await second.GetStringAsync(route);
            Check(!a.Contains("B_PRIVATE"), "Partner data leaked to first user on " + route);
            Check(!b.Contains("Mon compte personnel") && !b.Contains("Mon projet personnel"), "First user data leaked to partner on " + route);
            Check(!WebUtility.HtmlDecode(b).Contains("Prêt auto personnel"), "Private loan leaked to partner on " + route);
        }
        using (var json = JsonDocument.Parse(await second.GetStringAsync("/export/json?espace=couple")))
        {
            Check(!json.RootElement.GetProperty("Accounts").EnumerateArray().Any(a => a.GetProperty("Id").GetGuid() == firstAccount), "JSON export exposes private account ID.");
            Check(json.RootElement.GetProperty("Accounts").EnumerateArray().Any(a => a.GetProperty("Id").GetGuid() == sharedAccount), "JSON export omits joint account.");
        }
        var token = Token(await second.GetStringAsync("/comptes"));
        var reminderPage = await second.GetStringAsync("/parametres");
        Check(reminderPage.Contains("partner@example.test") && !reminderPage.Contains("web@example.test"), "Reminder recipient leaked or is not the current user.");
        response = await second.PostAsync("/actions/reminder-save", Form(new() { ["enabled"] = "on", ["day"] = "1", ["hour"] = "9" }));
        Check(response.StatusCode == HttpStatusCode.BadRequest, "Reminder update accepted without CSRF.");
        response = await second.PostAsync("/actions/reminder-save", Form(new() { ["__RequestVerificationToken"] = token, ["day"] = "16", ["hour"] = "11", ["actor"] = firstActor.ToString(), ["email"] = "forged@example.test" }));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Own disabled reminder preferences could not be saved.");
        reminderPage = await second.GetStringAsync("/parametres");
        Check(reminderPage.Contains("partner@example.test") && !reminderPage.Contains("forged@example.test"), "Reminder recipient was trusted from form input.");
        response = await second.PostAsync("/actions/reminder-test", Form(new() { ["__RequestVerificationToken"] = token }));
        Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "Unconfigured SMTP test should be rejected.");
        foreach (var route in new[] { "/export/analytics.csv?compte=" + firstAccount, "/export/analytics.csv?periode=custom&debut=invalid&fin=invalid" })
            Check((await second.GetAsync(route)).StatusCode == HttpStatusCode.BadRequest, "Analytics accepted hidden account or invalid dates.");
        var analyticsState = JsonSerializer.Deserialize<Workspace>(await second.GetStringAsync("/export/json?espace=couple"))!;
        var date = DateOnly.FromDateTime(DateTime.Today);
        var commonReport = CashHarbor.Application.FinancialAnalytics.Build(analyticsState, new(date, date, CashHarbor.Application.AnalyticsScope.Joint), date);
        var csv = await second.GetStringAsync($"/export/analytics.csv?periode=custom&debut={date:yyyy-MM-dd}&fin={date:yyyy-MM-dd}&vue=commun");
        Check(csv == CashHarbor.Application.FinancialAnalytics.ExportCsv(commonReport), "Analytics HTTP export differs from authorized common ledger.");
        foreach (var action in new[] { "account-edit", "account-share", "account-balance", "account-delete" })
        {
            response = await second.PostAsync("/actions/" + action, Form(new() { ["__RequestVerificationToken"] = token, ["id"] = firstAccount.ToString(), ["name"] = "FORGED", ["institution"] = "FORGED", ["confirm"] = "on", ["balance"] = "0", ["date"] = DateTime.Today.ToString("yyyy-MM-dd") }));
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("erreur="), "HTTP private account mutation accepted: " + action);
        }
        response = await second.PostAsync("/actions/transaction-add", Form(new() { ["__RequestVerificationToken"] = token, ["account"] = firstAccount.ToString(), ["label"] = "FORGED", ["category"] = "Autres", ["amount"] = "10", ["kind"] = "Expense", ["date"] = DateTime.Today.ToString("yyyy-MM-dd") }));
        Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "HTTP write to private account accepted.");
        response = await second.PostAsync("/actions/account-add", Form(new() { ["__RequestVerificationToken"] = token, ["name"] = "B_PRIVATE_CREATED_HTTP", ["institution"] = "Bank", ["kind"] = "Savings", ["visibility"] = "Personal", ["opening"] = "99", ["date"] = DateTime.Today.ToString("yyyy-MM-dd"), ["ownerId"] = firstActor.ToString() }));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Second user could not create own account.");
        using (var json = JsonDocument.Parse(await second.GetStringAsync("/export/json?espace=personnel")))
        {
            var account = json.RootElement.GetProperty("Accounts").EnumerateArray().Single(a => a.GetProperty("Name").GetString() == "B_PRIVATE_CREATED_HTTP");
            Check(account.GetProperty("OwnerId").GetGuid() == secondActor, "Form supplied owner ID was trusted.");
        }
        response = await second.PostAsync("/actions/account-edit", Form(new() { ["__RequestVerificationToken"] = token, ["id"] = sharedAccount.ToString(), ["name"] = "Notre épargne commune", ["institution"] = "Banque du foyer" }));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Joint account could not be edited by partner.");
        Check(!(await first.GetStringAsync("/export/json")).Contains("B_PRIVATE"), "New partner account leaked after joint update.");
        await CheckLoansAsync(first, second, token, firstActor, secondActor);
        await CheckAnnualRecurrencesAsync(first, second, token, firstActor, sharedAccount);
        var scheduleToken=Token(await first.GetStringAsync("/echeances?espace=personnel"));
        response=await first.PostAsync("/actions/scheduled-transfer",Form(new(){["__RequestVerificationToken"]=scheduleToken,["espace"]="personnel",["account"]=firstAccount.ToString(),["destination"]=sharedAccount.ToString(),["label"]="PRIVATE_MONTHLY_TRANSFER",["amount"]="600",["next"]=DateTime.Today.AddMonths(1).ToString("yyyy-MM-dd"),["frequency"]="Monthly"}));
        Check(response.Headers.Location?.OriginalString.StartsWith("/echeances?succes=1")==true,"Scheduled transfer form did not return to recurrences.");
        var scheduleState=JsonSerializer.Deserialize<Workspace>(await first.GetStringAsync("/export/json?espace=personnel"))!;
        Check(scheduleState.Recurrences.Single(r=>r.Label=="PRIVATE_MONTHLY_TRANSFER").TargetAccountId==sharedAccount,"Scheduled transfer destination not persisted.");
        Check(!(await second.GetStringAsync("/echeances?espace=couple")).Contains("PRIVATE_MONTHLY_TRANSFER"),"Partner sees private transfer schedule.");
        response=await second.PostAsync("/actions/goal-save",Form(new(){["__RequestVerificationToken"]=token,["espace"]="couple",["visibility"]="Joint",["name"]="Linked common saving goal",["target"]="6000",["saved"]="0",["account"]=sharedAccount.ToString(),["deadline"]=DateTime.Today.AddYears(1).ToString("yyyy-MM-dd")}));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1")==true,"Account-linked goal form failed.");
        var goalsState=JsonSerializer.Deserialize<Workspace>(await first.GetStringAsync("/export/json?espace=couple"))!;
        Check(goalsState.Goals.Single(g=>g.Name=="Linked common saving goal").AccountId==sharedAccount,"Goal account not persisted or visible to partner.");
        response=await second.PostAsync("/actions/goal-save",Form(new(){["__RequestVerificationToken"]=token,["espace"]="couple",["visibility"]="Joint",["name"]="Forged goal",["target"]="6000",["saved"]="0",["account"]=firstAccount.ToString()}));
        Check(response.Headers.Location?.OriginalString.Contains("erreur=")==true,"Shared goal accepted private account ID.");
        response = await second.PostAsync("/budget/save", Form(new() { ["espace"] = "couple" }));
        Check(response.StatusCode == HttpStatusCode.BadRequest, "Budget update accepted without CSRF.");
        var budgetMonth = DateTime.Today.ToString("yyyy-MM");
        response = await second.PostAsync("/budget/save", Form(new() { ["__RequestVerificationToken"] = token, ["espace"] = "couple", ["month"] = budgetMonth, ["income"] = "4000", ["needs"] = "50", ["wants"] = "30", ["saving"] = "800", ["annual"] = "1200", ["buffer"] = "100", ["cap_Courses"] = "500" }));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true && response.Headers.Location.OriginalString.Contains("espace=couple"), "Budget form redirect or persistence failed.");
        Check((await first.GetStringAsync("/budget?espace=couple&mois=" + budgetMonth)).Contains("value=\"4000\""), "Partner cannot read saved joint plan.");
        foreach (var client in new[] { first, second })
        {
            foreach (var key in new[] { "personnel", "couple" })
            {
                var json = JsonSerializer.Deserialize<Workspace>(await client.GetStringAsync("/export/json?espace=" + key))!;
                var expected = key == "couple" ? AccountVisibility.Joint : AccountVisibility.Personal;
                Check(json.Accounts.All(a => a.Visibility == expected) && json.Goals.All(g => g.Visibility == expected) && json.Loans.All(l => l.Visibility == expected), "Mixed export in " + key);
                var page = WebUtility.HtmlDecode(await client.GetStringAsync("/?espace=" + key));
                Check(!page.Contains(key == "couple" ? "Mon compte personnel" : "Notre épargne commune"), "Dashboard mixes spaces.");
                var navigation = await client.GetStringAsync("/comptes");
                Check(!WebUtility.HtmlDecode(navigation).Contains(key == "couple" ? "Mon compte personnel" : "Notre épargne commune"), "Space not retained across navigation.");
            }
        }
        response = await first.PostAsync("/actions/account-edit", Form(new() { ["__RequestVerificationToken"] = Token(await first.GetStringAsync("/comptes?espace=personnel")), ["espace"] = "personnel", ["id"] = sharedAccount.ToString(), ["name"] = "WRONG_SPACE", ["institution"] = "Bank" }));
        Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "Form edited an account in another space.");
        Check((await first.GetAsync("/?espace=unknown")).StatusCode == HttpStatusCode.BadRequest, "Invalid space accepted.");
        await first.GetAsync("/?espace=personnel");
        Console.WriteLine("HTTP PASS two identities: pages, filters, charts, exports, forged IDs, owner assignment and common access");
    }
    private static async Task CheckLoansAsync(HttpClient first, HttpClient second, string token, Guid firstActor, Guid secondActor)
    {
        var a = JsonSerializer.Deserialize<Workspace>(await first.GetStringAsync("/export/json?espace=personnel"))!;
        var common = JsonSerializer.Deserialize<Workspace>(await first.GetStringAsync("/export/json?espace=couple"))!;
        var own = a.Loans.Single(l => l.OwnerId == firstActor); var joint = common.Loans.Single(l => l.Visibility == AccountVisibility.Joint);
        foreach (var route in new[] { "/prets/" + own.Id, "/export/loan/" + own.Id + "/csv" })
        {
            var response = await second.GetAsync(route + "?espace=personnel"); Check(response.StatusCode == HttpStatusCode.NotFound, "Private loan details or export accessible by guessed ID.");
            Check((await first.GetAsync(route + "?espace=personnel")).IsSuccessStatusCode, "Owner cannot access loan details or export.");
        }
        Check((await second.GetAsync("/prets/" + joint.Id + "?espace=couple")).IsSuccessStatusCode && (await second.GetAsync("/export/loan/" + joint.Id + "/csv?espace=couple")).IsSuccessStatusCode, "Common loan not accessible to partner.");
        var noCsrf = await first.PostAsync("/actions/loan-confirm", Form(new() { ["id"] = own.Id.ToString(), ["number"] = "12", ["paidOn"] = DateTime.Today.ToString("yyyy-MM-dd") }));
        Check(noCsrf.StatusCode == HttpStatusCode.BadRequest, "Loan mutation accepted without CSRF.");
        foreach (var action in new[] { "loan-confirm", "loan-account", "loan-delete", "loan-undo" })
        {
            var response = await second.PostAsync("/actions/" + action, Form(new() { ["__RequestVerificationToken"] = token, ["id"] = own.Id.ToString(), ["number"] = "12", ["paidOn"] = DateTime.Today.ToString("yyyy-MM-dd"), ["mode"] = "tracking" }));
            Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "Forged private loan mutation accepted: " + action);
        }
        var draft = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["id"] = own.Id.ToString(), ["name"] = "B_PRIVATE_LOAN_HTTP", ["lender"] = "B_PRIVATE_LENDER_HTTP", ["loanKind"] = "Consumer", ["principal"] = "1200", ["rate"] = "3.5", ["months"] = "12", ["firstDue"] = DateTime.Today.AddMonths(-1).ToString("yyyy-MM-dd"), ["insuranceKind"] = "FixedMonthly", ["insurance"] = "1", ["insuranceRate"] = "0", ["fees"] = "0", ["apr"] = "4.0", ["initialPaid"] = "0", ["visibility"] = "Personal", ["ownerId"] = firstActor.ToString() };
        var result = await second.PostAsync("/actions/loan-save", Form(draft)); Check(result.Headers.Location?.OriginalString.Contains("erreur=") == true, "Private loan contract overwritten by partner.");
        draft.Remove("id"); result = await second.PostAsync("/actions/loan-save", Form(draft)); Check(result.Headers.Location?.OriginalString.Contains("succes=1") == true, "HTTP loan creation failed.");
        var b = JsonSerializer.Deserialize<Workspace>(await second.GetStringAsync("/export/json?espace=personnel"))!;
        Check(b.Loans.Single(l => l.Name == "B_PRIVATE_LOAN_HTTP").OwnerId == secondActor, "Loan owner supplied in form was trusted.");
        Check(!(await first.GetStringAsync("/export/json")).Contains("B_PRIVATE"), "New private partner loan leaked.");
        var number = LoanCalculator.PaidCount(joint, common.LoanPayments) + 1;
        var payment = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["id"] = joint.Id.ToString(), ["number"] = number.ToString(), ["paidOn"] = DateTime.Today.ToString("yyyy-MM-dd"), ["mode"] = "tracking" };
        result = await second.PostAsync("/actions/loan-confirm", Form(payment)); Check(result.Headers.Location?.OriginalString.Contains("succes=1") == true, "Partner could not confirm common loan payment.");
        result = await second.PostAsync("/actions/loan-confirm", Form(payment)); Check(result.Headers.Location?.OriginalString.Contains("erreur=") == true, "Double POST confirmed installment twice.");
        result = await second.PostAsync("/actions/loan-undo", Form(payment)); Check(result.Headers.Location?.OriginalString.Contains("succes=1") == true, "Partner could not undo common loan payment.");
        Console.WriteLine("HTTP PASS loans: private details/CSV blocked, common access, CSRF, forged owner/IDs and idempotent repayments");
    }
    private static async Task CheckAnnualRecurrencesAsync(HttpClient first, HttpClient second, string secondToken, Guid actor, Guid sharedAccount)
    {
        var w = JsonSerializer.Deserialize<Workspace>(await first.GetStringAsync("/export/json?espace=personnel"))!;
        var account = w.Accounts.First(a => a.OwnerId == actor).Id;
        var token = Token(await first.GetStringAsync("/echeances"));
        var draft = new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["account"] = account.ToString(), ["label"] = "PRIVATE_ANNUAL_WATER", ["category"] = "Logement", ["amount"] = "-600", ["next"] = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd"), ["essential"] = "on", ["frequency"] = "Yearly" };
        var response = await first.PostAsync("/actions/recurrence-add", Form(draft)); Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "HTTP annual creation failed.");
        w = JsonSerializer.Deserialize<Workspace>(await first.GetStringAsync("/export/json"))!;
        var recurrence = w.Recurrences.Single(r => r.Label == "PRIVATE_ANNUAL_WATER");
        Check(recurrence.Frequency == RecurrenceFrequency.Yearly && recurrence.Cents == -60000, "Annual frequency or full amount not stored.");
        Check(!(await second.GetStringAsync("/echeances")).Contains("PRIVATE_ANNUAL_WATER"), "Private annual bill leaked.");
        draft["id"] = recurrence.Id.ToString(); draft["__RequestVerificationToken"] = secondToken;
        response = await second.PostAsync("/actions/recurrence-edit", Form(draft)); Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "Partner edited a hidden annual bill.");
        draft["__RequestVerificationToken"] = token; draft["amount"] = "-612";
        response = await first.PostAsync("/actions/recurrence-edit", Form(draft)); Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Owner could not edit annual bill.");
        draft.Remove("id"); draft["frequency"] = "Weekly";
        response = await first.PostAsync("/actions/recurrence-add", Form(draft)); Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "Invalid recurrence frequency accepted over HTTP.");
        draft["frequency"] = "Yearly"; draft["label"] = "Assurance annuelle commune"; draft["account"] = sharedAccount.ToString(); draft["__RequestVerificationToken"] = secondToken;
        response = await second.PostAsync("/actions/recurrence-add", Form(draft)); Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Shared annual bill creation failed.");
        Check(WebUtility.HtmlDecode(await first.GetStringAsync("/echeances?espace=couple")).Contains("Assurance annuelle commune"), "Shared annual bill absent from partner's view.");
        Console.WriteLine("HTTP PASS annual recurrences: create/edit, full amount, shared access, private isolation and invalid frequencies");
    }
    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
