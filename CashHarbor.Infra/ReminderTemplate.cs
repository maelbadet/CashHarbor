using System.Net;
namespace CashHarbor.Infra;

public static class ReminderTemplate
{
    public static string Html(bool test)
    {
        var heading = test ? "Votre rappel est prêt." : "Votre point mensuel.";
        var text = test ? "Votre configuration SMTP fonctionne. Vous pouvez activer votre rappel mensuel dans les paramètres." : "Prenez quelques minutes pour actualiser vos soldes, vérifier vos opérations et faire le point sur vos objectifs.";
        return $$"""
        <!doctype html><html lang="fr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="margin:0;background:#101113;color:#f4f4f5;font-family:Arial,sans-serif">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0"><tr><td align="center" style="padding:32px 16px">
        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:560px;background:#191b20;border:1px solid #30333b;border-radius:16px">
        <tr><td style="padding:32px;border-bottom:3px solid #ef4444"><strong style="font-size:26px;color:#ffffff">CashHarbor<span style="color:#ef4444">.</span></strong><p style="font-size:11px;letter-spacing:2px;color:#aeb4bf">CODE AND WEB CONSULTING</p></td></tr>
        <tr><td style="padding:32px"><p style="color:#ef4444;font-size:12px;letter-spacing:2px">VOTRE RENDEZ-VOUS BUDGET</p><h1 style="font-size:28px;color:#ffffff">{{WebUtility.HtmlEncode(heading)}}</h1><p style="line-height:1.8;color:#c9cdd5">{{WebUtility.HtmlEncode(text)}}</p><p style="padding:18px;background:#111216;border-left:3px solid #ef4444;line-height:1.7;color:#e3e5e9">Ouvrez votre application privée via Tailscale pour retrouver vos espaces Personnel et Couple.</p><p style="font-size:13px;color:#aeb4bf;line-height:1.7">Vous pouvez modifier ou désactiver ce rappel dans Paramètres → Rappels par e-mail.</p></td></tr>
        <tr><td style="padding:24px 32px;border-top:1px solid #30333b;color:#9199a7;font-size:12px;line-height:1.7">Aucun solde ni détail bancaire dans cet e-mail.<br>CashHarbor · Un budget plus clair, chaque mois.</td></tr>
        </table></td></tr></table></body></html>
        """;
    }
}
