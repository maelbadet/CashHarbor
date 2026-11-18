using System.Text.Json;
using CashHarbor.Application;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
namespace CashHarbor.Infra;

public sealed class BudgetPlans(IWorkspaceStore workspace, IDataProtectionProvider protection, IConfiguration config, PortfolioGate gate) : IBudgetPlans
{
    private readonly IDataProtector protector = protection.CreateProtector("CashHarbor.BudgetPlans.v1");
    private string PathFor(Guid actor, bool couple, string month)
    {
        if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out var date) || date.Year is < 1900 or > 2200) throw new RuleException("Mois invalide.");
        return Path.Combine(config["Storage:Directory"]!, "budget-" + (couple ? "couple" : actor.ToString("N")) + "-" + month + ".enc");
    }
    public async Task<BudgetPlan> ReadAsync(Guid actor, bool couple, string month)
    {
        await workspace.ReadAsync(actor); var path = PathFor(actor, couple, month); await gate.Lock.WaitAsync();
        try { return File.Exists(path) ? JsonSerializer.Deserialize<BudgetPlan>(protector.Unprotect(await File.ReadAllTextAsync(path))) ?? new() : new(); } finally { gate.Lock.Release(); }
    }
    public async Task SaveAsync(Guid actor, bool couple, string month, BudgetPlan plan)
    {
        await workspace.ReadAsync(actor); var path = PathFor(actor, couple, month);
        if (plan.Income is < 0 or > 100000000 || plan.NeedsPercent is < 0 or > 100 || plan.WantsPercent is < 0 or > 100 || plan.NeedsPercent + plan.WantsPercent > 100 || new[] { plan.SavingsGoal, plan.AnnualBills, plan.SafetyBuffer }.Any(v => v is < 0 or > 100000000) || plan.Limits?.Any(p => !FinanceService.Categories.Contains(p.Key) || p.Value is < 0 or > 100000000) == true) throw new RuleException("Montants ou répartition invalides.");
        await gate.Lock.WaitAsync(); try { await File.WriteAllTextAsync(path + ".tmp", protector.Protect(JsonSerializer.Serialize(plan))); File.Move(path + ".tmp", path, true); } finally { gate.Lock.Release(); }
    }
}
