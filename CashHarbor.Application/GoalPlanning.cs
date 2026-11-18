using CashHarbor.Domain;
namespace CashHarbor.Application;
public static class GoalPlanning
{
    public static long Current(Workspace workspace, FinancialGoal goal) => goal.AccountId is { } id ? workspace.Balance(id) : goal.SavedCents;
    public static int Months(DateOnly today, DateOnly deadline)
    {
        if(deadline<=today)return 0;
        var months=(deadline.Year-today.Year)*12+deadline.Month-today.Month;
        if(today.AddMonths(months)>deadline)months--;
        return Math.Max(0,months);
    }
    public static long Monthly(long current,long target,int months)=>months>0?checked((long)decimal.Ceiling(Math.Max(0m,(decimal)target-current)/months)):0;
}
