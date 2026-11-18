using CashHarbor.Domain;
namespace CashHarbor.Application;
public sealed record SafetyReserveReport(long CommonMonthlyCents, decimal MonthlyShareCents, long MonthlyMarginCents, int Months, long TargetCents, long SavedCents)
{
    public long MissingCents => Math.Max(0,TargetCents-SavedCents);
}
public static class SafetyReserve
{
    public static SafetyReserveReport Calculate(Workspace authorized,bool couple)
    {
        var p=authorized.EmergencyPolicy;
        var common=p.SharedMonthlyExpenseCents??FinanceService.EssentialMonthlyCents(WorkspaceSpaces.Select(authorized,AccountVisibility.Joint));
        var monthlyShare=couple?common:common/2m;
        var margin=checked(p.MonthlySafetyMarginCents*(couple?2:1));
        var target=checked((long)decimal.Ceiling((monthlyShare+margin)*p.SafetyMonths));
        var scope=WorkspaceSpaces.Select(authorized,couple?AccountVisibility.Joint:AccountVisibility.Personal);
        var saved=scope.Accounts.Where(a=>a.Kind==AccountKind.Savings).Sum(a=>scope.Balance(a.Id));
        return new(common,monthlyShare,margin,p.SafetyMonths,target,saved);
    }
}
