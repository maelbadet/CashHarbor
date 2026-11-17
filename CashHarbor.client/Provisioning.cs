using CashHarbor.Application;
using CashHarbor.Infra;
namespace CashHarbor.client;
public static class Provisioning
{
    public static async Task RunAsync(IServiceProvider services, bool reset)
    {
        if (Console.IsInputRedirected) throw new InvalidOperationException("Interactive terminal required. Do not pass secrets in command arguments.");
        using var scope = services.CreateScope(); var identity = scope.ServiceProvider.GetRequiredService<IIdentityService>();
        Console.WriteLine(reset ? "Réinitialisation d’un accès (sessions révoquées)." : "Création d’un accès privé (maximum deux).");
        Console.Write("Adresse e-mail : "); var email = Console.ReadLine() ?? "";
        Console.Write("Mot de passe unique (14 caractères minimum) : "); var password = ReadSecret();
        Console.Write("Confirmer : "); if (password != ReadSecret()) { Console.WriteLine("Mots de passe différents. Aucun accès créé."); Environment.ExitCode = 1; return; }
        var secret = Totp.NewSecret();
        Console.WriteLine("Ajoutez cette clé TOTP à votre application d’authentification (SHA-1, 6 chiffres, 30 secondes).");
        Console.WriteLine($"Clé privée à conserver dans votre gestionnaire de mots de passe : {secret}");
        Console.Write("Code actuel de l’application : "); var code = ReadSecret();
        try { await identity.ProvisionAsync(email, password, secret, code, reset); Console.WriteLine("Accès prêt. Attendez le prochain code pour vous connecter. Effacez l’affichage du terminal."); }
        catch (RuleException e) { Console.WriteLine(e.Message); Environment.ExitCode = 1; }
    }
    private static string ReadSecret()
    {
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true); if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return result.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (result.Length > 0) result.Length--; }
            else if (!char.IsControl(key.KeyChar) && result.Length < 128) result.Append(key.KeyChar);
        }
    }
}
