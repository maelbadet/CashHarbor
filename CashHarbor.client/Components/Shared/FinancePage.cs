using CashHarbor.Application;
using CashHarbor.Domain;
using Microsoft.AspNetCore.Components;
using System.Globalization;
using System.Security.Claims;
namespace CashHarbor.client.Components.Shared;
public abstract class FinancePage : ComponentBase
{
    [Inject] protected FinanceService Finance { get; set; } = default!;
    [Inject] protected IHttpContextAccessor HttpContext { get; set; } = default!;
    [Inject] protected SpaceContext Space { get; set; } = default!;
    protected Guid Actor => Guid.Parse(HttpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException());
    protected Workspace? W;
    protected DateOnly Today => Finance.Today;
    protected string FormAnchor => Space.Url(HttpContext.HttpContext!.Request.Path + "#ajouter");
    protected override async Task OnInitializedAsync() => W = Space.Select(await Finance.ReadAsync(Actor));
    public static string Visibility(AccountVisibility visibility) => visibility == AccountVisibility.Joint ? "Commun · vous deux" : "Personnel · vous uniquement";
    public static string Euro(long cents) => (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " €";
    public static string Amount(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Kind(AccountKind kind) => kind switch { AccountKind.Current => "Compte courant", AccountKind.Savings => "Épargne", _ => "Investissement" };
}
