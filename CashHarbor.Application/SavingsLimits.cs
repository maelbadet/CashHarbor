using System.Globalization;
using System.Text;
using CashHarbor.Domain;
namespace CashHarbor.Application;

public sealed record SavingsLimit(string Product, long CeilingCents, string Source)
{
    public long RemainingCents(long balance)=>Math.Max(0,CeilingCents-Math.Max(0,balance));
}
public static class SavingsLimits
{
    public static SavingsLimit? For(FinancialAccount account)
    {
        if(account.Kind!=AccountKind.Savings||string.IsNullOrWhiteSpace(account.Product))return null;
        var key=string.Concat(account.Product.Normalize(NormalizationForm.FormD).Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark&&char.IsLetterOrDigit(c))).ToUpperInvariant();
        return key switch {
            "LIVRETA"=>new("Livret A",2295000,"https://www.service-public.gouv.fr/particuliers/vosdroits/F2365"),
            "LEP" or "LIVRETDEPARGNEPOPULAIRE"=>new("LEP",1000000,"https://www.service-public.gouv.fr/particuliers/vosdroits/F2367"),
            "LIVRETJEUNE"=>new("Livret jeune",160000,"https://www.service-public.gouv.fr/particuliers/vosdroits/F2904"),
            _=>null };
    }
}
