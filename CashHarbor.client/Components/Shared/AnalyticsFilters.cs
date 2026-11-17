using System.Globalization;
using CashHarbor.Application;

namespace CashHarbor.client.Components.Shared;

public static class AnalyticsFilters
{
    public static AnalyticsRequest Parse(IQueryCollection query, DateOnly today)
    {
        var period = query["periode"].ToString(); if (string.IsNullOrEmpty(period)) period = "6m";
        var month = new DateOnly(today.Year, today.Month, 1);
        var from = period switch
        {
            "1m" => month, "3m" => month.AddMonths(-2), "6m" => month.AddMonths(-5), "12m" => month.AddMonths(-11), "annee" => new(today.Year, 1, 1),
            "custom" => Date(query["debut"].ToString()), _ => throw new RuleException("Période inconnue. Choisissez une période proposée ou des dates personnalisées.")
        };
        var to = period == "custom" ? Date(query["fin"].ToString()) : today;
        var scope = query["vue"].ToString() switch { "" or "tout" => AnalyticsScope.All, "personnel" => AnalyticsScope.Personal, "commun" => AnalyticsScope.Joint, _ => throw new RuleException("Vue inconnue.") };
        var grain = query["precision"].ToString() switch { "" or "auto" => AnalyticsGrain.Auto, "jour" => AnalyticsGrain.Daily, "semaine" => AnalyticsGrain.Weekly, "mois" => AnalyticsGrain.Monthly, _ => throw new RuleException("Regroupement inconnu.") };
        Guid? account = null;
        if (!string.IsNullOrEmpty(query["compte"])) account = Guid.TryParse(query["compte"], out var id) ? id : throw new RuleException("Compte invalide.");
        return new(from, to, scope, account, grain);
    }
    private static DateOnly Date(string text) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : throw new RuleException("Renseignez les deux dates de la période personnalisée.");
    public static string Scope(AnalyticsScope scope) => scope switch { AnalyticsScope.Personal => "personnel", AnalyticsScope.Joint => "commun", _ => "tout" };
    public static string Grain(AnalyticsGrain grain) => grain switch { AnalyticsGrain.Daily => "jour", AnalyticsGrain.Weekly => "semaine", AnalyticsGrain.Monthly => "mois", _ => "auto" };
    public static string Query(AnalyticsRequest request) => $"periode=custom&debut={request.From:yyyy-MM-dd}&fin={request.To:yyyy-MM-dd}&vue={Scope(request.Scope)}&precision={Grain(request.Grain)}&compte={request.AccountId}";
}
