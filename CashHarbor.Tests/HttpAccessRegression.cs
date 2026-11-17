using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CashHarbor.Infra;

internal static class HttpAccessRegression
{
    public static async Task RunAsync(HttpClient first, string origin, string password, string secondSecret, Guid firstActor, Guid firstAccount, Guid sharedAccount, Guid secondActor)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new(), ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var second = new HttpClient(handler) { BaseAddress = new Uri(origin) };
        var login = await second.GetStringAsync("/connexion");
        var response = await second.PostAsync("/auth/login", Form(new() { ["__RequestVerificationToken"] = Token(login), ["email"] = "partner@example.test", ["password"] = password, ["code"] = Totp.Code(secondSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) }));
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.OriginalString == "/", "Second login failed.");
        foreach (var route in new[] { "/", "/comptes", "/comptes?type=epargne", "/operations", "/operations?compte=" + firstAccount, "/objectifs", "/echeances", "/parametres", "/export/json", "/export/csv" })
        {
            var a = await first.GetStringAsync(route); var b = await second.GetStringAsync(route);
            Check(!a.Contains("B_PRIVATE"), "Partner data leaked to first user on " + route);
            Check(!b.Contains("Mon compte personnel") && !b.Contains("Mon projet personnel"), "First user data leaked to partner on " + route);
        }
        using (var json = JsonDocument.Parse(await second.GetStringAsync("/export/json")))
        {
            Check(!json.RootElement.GetProperty("Accounts").EnumerateArray().Any(a => a.GetProperty("Id").GetGuid() == firstAccount), "JSON export exposes private account ID.");
            Check(json.RootElement.GetProperty("Accounts").EnumerateArray().Any(a => a.GetProperty("Id").GetGuid() == sharedAccount), "JSON export omits joint account.");
        }
        var token = Token(await second.GetStringAsync("/comptes"));
        foreach (var action in new[] { "account-edit", "account-share", "account-balance", "account-delete" })
        {
            response = await second.PostAsync("/actions/" + action, Form(new() { ["__RequestVerificationToken"] = token, ["id"] = firstAccount.ToString(), ["name"] = "FORGED", ["institution"] = "FORGED", ["confirm"] = "on", ["balance"] = "0", ["date"] = DateTime.Today.ToString("yyyy-MM-dd") }));
            Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString.Contains("erreur="), "HTTP private account mutation accepted: " + action);
        }
        response = await second.PostAsync("/actions/transaction-add", Form(new() { ["__RequestVerificationToken"] = token, ["account"] = firstAccount.ToString(), ["label"] = "FORGED", ["category"] = "Autres", ["amount"] = "10", ["kind"] = "Expense", ["date"] = DateTime.Today.ToString("yyyy-MM-dd") }));
        Check(response.Headers.Location?.OriginalString.Contains("erreur=") == true, "HTTP write to private account accepted.");
        response = await second.PostAsync("/actions/account-add", Form(new() { ["__RequestVerificationToken"] = token, ["name"] = "B_PRIVATE_CREATED_HTTP", ["institution"] = "Bank", ["kind"] = "Savings", ["visibility"] = "Personal", ["opening"] = "99", ["date"] = DateTime.Today.ToString("yyyy-MM-dd"), ["ownerId"] = firstActor.ToString() }));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Second user could not create own account.");
        using (var json = JsonDocument.Parse(await second.GetStringAsync("/export/json")))
        {
            var account = json.RootElement.GetProperty("Accounts").EnumerateArray().Single(a => a.GetProperty("Name").GetString() == "B_PRIVATE_CREATED_HTTP");
            Check(account.GetProperty("OwnerId").GetGuid() == secondActor, "Form supplied owner ID was trusted.");
        }
        response = await second.PostAsync("/actions/account-edit", Form(new() { ["__RequestVerificationToken"] = token, ["id"] = sharedAccount.ToString(), ["name"] = "Notre épargne commune", ["institution"] = "Banque du foyer" }));
        Check(response.Headers.Location?.OriginalString.Contains("succes=1") == true, "Joint account could not be edited by partner.");
        Check(!(await first.GetStringAsync("/export/json")).Contains("B_PRIVATE"), "New partner account leaked after joint update.");
        Console.WriteLine("HTTP PASS two identities: pages, filters, charts, exports, forged IDs, owner assignment and common access");
    }
    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
