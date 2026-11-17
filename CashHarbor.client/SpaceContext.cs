using CashHarbor.Domain;
using CashHarbor.Application;
namespace CashHarbor.client;

public sealed class SpaceContext(IHttpContextAccessor accessor)
{
    public const string CookieName = "__Host-CashHarbor-Space";
    public string Key => Resolve(accessor.HttpContext!.Request);
    public bool IsCouple => Key == "couple";
    public AccountVisibility Visibility => IsCouple ? AccountVisibility.Joint : AccountVisibility.Personal;
    public AnalyticsScope Scope => IsCouple ? AnalyticsScope.Joint : AnalyticsScope.Personal;
    public string Label => IsCouple ? "Couple" : "Personnel";
    public string ScopeKey => IsCouple ? "commun" : "personnel";
    public Workspace Select(Workspace authorized) => WorkspaceSpaces.Select(authorized, Visibility);
    public string Url(string path) => path + (path.Contains('?') ? "&" : "?") + "espace=" + Key;
    public static string Resolve(HttpRequest request)
    {
        var explicitSpace = request.Query["espace"].ToString();
        if (explicitSpace is "personnel" or "couple") return explicitSpace;
        // Old bookmarks still select the corresponding separate space.
        var legacy = request.Query["vue"].ToString();
        if (legacy is "commun" or "personnel") return legacy == "commun" ? "couple" : "personnel";
        return request.Cookies[CookieName] == "couple" ? "couple" : "personnel";
    }
}
