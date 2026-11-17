using CashHarbor.Application;
using CashHarbor.Domain;
using CashHarbor.Infra;

namespace CashHarbor.client;

public static class AccessClassification
{
    public static async Task RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("Interactive terminal required.");
        using var scope = services.CreateScope(); var migrator = scope.ServiceProvider.GetRequiredService<LegacyAccessMigrator>();
        var plan = await migrator.PlanAsync();
        if (plan.Items.Count == 0) { Console.WriteLine("Aucun compte ni objectif ancien à classer. Aucun changement."); return; }
        Console.WriteLine("CLASSEMENT DES DONNÉES EXISTANTES — application web arrêtée.");
        Console.WriteLine("0 : Commun, accessible aux deux personnes");
        for (var i = 0; i < plan.Users.Count; i++) Console.WriteLine($"{i + 1} : Personnel, visible uniquement par {plan.Users[i].Email}");
        var assignments = new List<AccessAssignment>();
        foreach (var item in plan.Items)
        {
            int selection;
            do { Console.Write($"{(item.IsGoal ? "Objectif" : "Compte")} « {item.Name} » — choix : "); }
            while (!int.TryParse(Console.ReadLine(), out selection) || selection < 0 || selection > plan.Users.Count);
            assignments.Add(new(item.Id, item.IsGoal, selection == 0 ? AccountVisibility.Joint : AccountVisibility.Personal, selection == 0 ? null : plan.Users[selection - 1].Id));
        }
        Console.Write("Le partage commun rend tout l’historique du compte visible aux deux personnes. Taper CLASSER pour appliquer : ");
        if (Console.ReadLine() != "CLASSER") { Console.WriteLine("Annulé. Aucune donnée modifiée."); return; }
        try
        {
            var backup = await migrator.ApplyAsync(assignments);
            Console.WriteLine($"Classement terminé. Sauvegarde SQLite préalable : {backup}");
            Console.WriteLine("Conserver cette sauvegarde avec les clés et les secrets. Aucune opération ni aucun solde supprimé.");
        }
        catch (RuleException e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
    }
}
