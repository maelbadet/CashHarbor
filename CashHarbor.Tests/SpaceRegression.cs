using CashHarbor.Application;
using CashHarbor.Domain;

internal static class SpaceRegression
{
    public static async Task Run()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        var actor = Guid.NewGuid(); var partner = Guid.NewGuid(); var personalId = Guid.NewGuid(); var jointId = Guid.NewGuid(); var hiddenId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var w = new Workspace { Accounts = [new(personalId, "VPS perso", "Banque", AccountKind.Current, 10000, today, AccountVisibility.Personal, actor), new(jointId, "Foyer", "Banque", AccountKind.Current, 20000, today, AccountVisibility.Joint), new(hiddenId, "Partenaire privé", "Banque", AccountKind.Current, 99999, today, AccountVisibility.Personal, partner)],
            Recurrences = [new(Guid.NewGuid(), personalId, "VPS", "Tech", -1500, today, today.Day, true), new(Guid.NewGuid(), jointId, "Eau commune", "Logement", -5000, today, today.Day, true)],
            Goals = [new(Guid.NewGuid(), "PC perso", 100000, 0, null, AccountVisibility.Personal, actor), new(Guid.NewGuid(), "Voyage", 100000, 0, null, AccountVisibility.Joint)] };
        var authorized = WorkspaceAccess.VisibleTo(w, actor);
        var personal = WorkspaceSpaces.Select(authorized, AccountVisibility.Personal); var joint = WorkspaceSpaces.Select(authorized, AccountVisibility.Joint);
        Check(personal.Accounts.Single().Id == personalId && joint.Accounts.Single().Id == jointId, "Spaces contain foreign accounts.");
        Check(personal.Goals.Single().Name == "PC perso" && joint.Goals.Single().Name == "Voyage", "Goals mixed across spaces.");
        var store = new MemoryStore(w); var finance = new FinanceService(store, TimeProvider.System);
        await finance.BookDueAsync(actor, AccountVisibility.Personal);
        Check(w.Transactions.Count == 1 && w.Transactions.Single().AccountId == personalId && w.Recurrences.Single(r => r.AccountId == jointId).NextDate == today, "Personal booking modified joint ledger.");
        await finance.BookDueAsync(actor, AccountVisibility.Joint); await finance.BookDueAsync(actor, AccountVisibility.Joint);
        Check(w.Transactions.Count == 2 && w.Balance(personalId) == 8500 && w.Balance(jointId) == 15000 && w.Balance(hiddenId) == 99999, "Scoped booking duplicated or corrupted balances.");
        authorized = WorkspaceAccess.VisibleTo(w, actor);
        var soloReport = FinancialAnalytics.Build(WorkspaceSpaces.Select(authorized, AccountVisibility.Personal), new(today, today), today);
        var coupleReport = FinancialAnalytics.Build(WorkspaceSpaces.Select(authorized, AccountVisibility.Joint), new(today, today), today);
        Check(soloReport.ExpenseCents == 1500 && coupleReport.ExpenseCents == 5000, "VPS cost polluted couple chart.");
        Check(!FinanceService.ExportCsv(WorkspaceSpaces.Select(authorized, AccountVisibility.Joint)).Contains("VPS"), "Personal transaction leaked into couple CSV.");
        await finance.SaveGoalAsync(actor,null,"Linked goal",6000,0,today.AddMonths(12),AccountVisibility.Personal,personalId);
        var linked=(await finance.ReadAsync(actor)).Goals.Single(g=>g.Name=="Linked goal");
        Check(GoalPlanning.Current(await finance.ReadAsync(actor),linked)==8500,"Linked goal ignored account balance.");
        await finance.AddTransactionAsync(actor,personalId,"Deposit","Salaire",15,today,EntryKind.Income);
        Check(GoalPlanning.Current(await finance.ReadAsync(actor),linked)==10000,"Goal did not follow new account transaction.");
        Check(!(await finance.ReadAsync(partner)).Goals.Any(g=>g.Id==linked.Id),"Linked private goal exposed to partner.");
        try{await finance.SaveGoalAsync(actor,null,"Bad shared goal",6000,0,today.AddMonths(12),AccountVisibility.Joint,personalId);throw new Exception("Shared goal linked to private account.");}catch(RuleException){}
        try{await finance.DeleteAccountAsync(actor,personalId);throw new Exception("Linked account deletion accepted.");}catch(RuleException){}
        Check(GoalPlanning.Months(new(2026,11,18),new(2027,11,18))==12 && GoalPlanning.Monthly(105000,600000,12)==41250,"1050 to 6000 goal calculation incorrect.");
        Check(GoalPlanning.Monthly(700000,600000,12)==0,"Reached goal still requires contributions.");
        await finance.SavePolicyAsync(actor,"Home",8,100,1000);
        await finance.RenameAccountAsync(actor,personalId,"LEP account","Bank","LEP");
        await finance.RenameAccountAsync(actor,personalId,"Renamed account","Bank");
        Check((await finance.ReadAsync(actor)).Accounts.Single(a=>a.Id==personalId).Product=="LEP","Legacy account rename dropped savings product.");
        Check(SafetyReserve.Calculate(await finance.ReadAsync(actor),false).TargetCents==480000&&SafetyReserve.Calculate(await finance.ReadAsync(partner),true).TargetCents==960000,"Saved safety policy not shared or incorrectly scaled.");
        var before=w.Accounts.Sum(a=>w.Balance(a.Id));
        await finance.AddRecurringTransferAsync(actor,personalId,jointId,"Common contribution",50,today,RecurrenceFrequency.Monthly);
        var programmed=(await finance.ReadAsync(actor)).Recurrences.Single(r=>r.Label=="Common contribution");
        Check(!(await finance.ReadAsync(partner)).Recurrences.Any(r=>r.Id==programmed.Id),"Private transfer schedule exposed to partner.");
        await finance.BookDueAsync(actor,AccountVisibility.Personal);await finance.BookDueAsync(actor,AccountVisibility.Personal);
        var legs=w.Transactions.Where(t=>t.RecurrenceId==programmed.Id).ToArray();
        Check(legs.Length==2&&legs.All(t=>t.Kind==EntryKind.Transfer)&&legs.Sum(t=>t.Cents)==0&&legs[0].TransferId==legs[1].TransferId,"Scheduled transfer duplicated or created an expense.");
        Check(w.Accounts.Sum(a=>w.Balance(a.Id))==before,"Scheduled transfer changed total balances.");
        await finance.AddRecurringTransferAsync(actor,jointId,personalId,"Private return schedule",5,today,RecurrenceFrequency.Monthly);
        var hidden=(await finance.ReadAsync(actor)).Recurrences.Single(r=>r.Label=="Private return schedule");
        await finance.BookDueAsync(partner,AccountVisibility.Joint);
        Check(w.Recurrences.Any(r=>r.Id==hidden.Id)&&!w.Transactions.Any(t=>t.RecurrenceId==hidden.Id),"Partner deleted or booked hidden common-to-private schedule.");
        await finance.BookDueAsync(actor,AccountVisibility.Joint);
        Check(w.Transactions.Count(t=>t.RecurrenceId==hidden.Id)==2,"Owner could not book common-to-private schedule.");
        try{await finance.AddRecurringTransferAsync(actor,personalId,personalId,"Invalid",10,today,RecurrenceFrequency.Monthly);throw new Exception("Self transfer accepted.");}catch(RuleException){}
        await finance.RenameAccountAsync(actor,personalId,"Crypto account","Bank",kind:AccountKind.Crypto);
        Check((await finance.ReadAsync(actor)).Accounts.Single(a=>a.Id==personalId).Kind==AccountKind.Crypto,"Crypto classification not saved.");
    }
    private sealed class MemoryStore(Workspace full) : IWorkspaceStore
    {
        public Task<Workspace> ReadAsync(Guid actor, CancellationToken ct = default) => Task.FromResult(WorkspaceAccess.VisibleTo(full, actor));
        public Task UpdateAsync(Guid actor, string action, Action<Workspace> change, CancellationToken ct = default) { var slice = WorkspaceAccess.VisibleTo(full, actor); change(slice); WorkspaceAccess.Merge(full, slice, actor); return Task.CompletedTask; }
    }
}
