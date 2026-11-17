using CashHarbor.Application;
using CashHarbor.Domain;
using Microsoft.AspNetCore.Components;
using System.Globalization;
namespace CashHarbor.client.Components.Shared;
public abstract class FinancePage : ComponentBase
{
    [Inject] protected FinanceService Finance { get; set; } = default!;
    protected Workspace? W;
    protected DateOnly Today => Finance.Today;
    protected override async Task OnInitializedAsync() => W = await Finance.ReadAsync();
    public static string Euro(long cents) => (cents / 100m).ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " €";
    public static string Amount(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Kind(AccountKind kind) => kind switch { AccountKind.Current => "Compte courant", AccountKind.Savings => "Épargne", _ => "Investissement" };
}
