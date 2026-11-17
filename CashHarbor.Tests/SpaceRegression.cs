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
    }
    private sealed class MemoryStore(Workspace full) : IWorkspaceStore
    {
        public Task<Workspace> ReadAsync(Guid actor, CancellationToken ct = default) => Task.FromResult(WorkspaceAccess.VisibleTo(full, actor));
        public Task UpdateAsync(Guid actor, string action, Action<Workspace> change, CancellationToken ct = default) { var slice = WorkspaceAccess.VisibleTo(full, actor); change(slice); WorkspaceAccess.Merge(full, slice, actor); return Task.CompletedTask; }
    }
}
