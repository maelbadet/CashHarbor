# Sécurité de cette version

Périmètre : application privée à une instance, deux utilisateurs d’un même foyer disposant de données personnelles et communes, accès réseau privé HTTPS. Pas de données bancaires d’authentification, pas de connexion à une banque, pas de paiement et pas de service public multi-foyers.

## Contrôles implémentés

- Provisionnement terminal uniquement, plafond de deux utilisateurs vérifié en stockage. Pas d’inscription ni de mot de passe initial.
- Hachage de mot de passe ASP.NET Core PasswordHasher (PBKDF2, 210 000 itérations), mots de passe de 14 à 128 caractères. Pas de chiffrement réversible des mots de passe.
- TOTP obligatoire, fenêtre +/- un pas de 30 secondes, comparaison en temps constant, refus d’un pas déjà consommé. Secret TOTP protégé par Data Protection.
- Verrouillage persistant 15 minutes après cinq échecs, limitation des requêtes de connexion par IP et des écritures par utilisateur. Les réinitialisations terminal doivent être réalisées sans autre processus d’application actif.
- Cookie préfixé __Host-, Secure, HttpOnly, SameSite Strict, durée absolue de 30 minutes. Version de session vérifiée en base à chaque requête ; déconnexion, changement et réinitialisation de mot de passe invalident les sessions de l’utilisateur.
- Validation antiforgery explicite sur connexion, déconnexion et toutes les mutations. Formulaires rendus côté serveur. Aucune donnée financière dans localStorage.
- Authentification requise sur toutes les pages financières, toutes les mutations et tous les exports. L’identifiant utilisateur provient du cookie signé, jamais du formulaire.
- HTTPS requis, contrôle de l’origine publique en production, prise en compte des forwarded headers depuis un seul proxy connu, CSP sans script tiers, anti-framing, no-sniff, no-store, referrer désactivé.
- Libellés et contenu financier dans une enveloppe JSON chiffrée et authentifiée par Data Protection. Base SQLite locale, requêtes EF paramétrées. Clés Data Protection persistantes dans un volume distinct et protégées par certificat en production.
- Validation des montants en centimes exacts, des dates et des références de comptes ; transaction de stockage atomique et jeton de concurrence sur l’enveloppe. Les transferts internes ne créent pas de revenu.
- Journal des actions sans montants, libellés, mots de passe, codes MFA ou jetons de session. Noms d’utilisateurs normalisés ; e-mails et métadonnées du journal restent en clair dans SQLite.
- Exports CSV avec guillemets et neutralisation des préfixes de formule. Les exports téléchargés sont volontairement en clair.
- Conteneur sans root, capabilities supprimées, filesystem en lecture seule, limites de ressources, secrets hors image et hors dépôt.

## Limites

Le chiffrement protège une base copiée sans les clés. Le certificat et sa phrase secrète résident sur l’hôte pour permettre un redémarrage automatique ; il ne protège pas d’un administrateur malveillant ou d’un serveur compromis. Le mode Development n’impose pas la protection des clés par certificat : ne pas y saisir vos vraies données.

Les comptes personnels sont accessibles uniquement à leur titulaire ; les comptes communs sont accessibles aux deux utilisateurs. Le stockage renvoie un périmètre filtré et ne fusionne que ce périmètre lors des écritures, en conservant les données cachées. Les opérations, échéances, relevés, objectifs, graphiques et exports suivent ces droits. Le journal web est limité aux événements de l’utilisateur connecté. Les comptes communs ne peuvent pas être rendus privés depuis l’interface ; partager un compte privé nécessite la confirmation du titulaire. Les objectifs sont déclaratifs et ne réservent pas de solde réel. Les calculs dépendent de saisies exactes et complètes.

La limite des mots de passe et le verrouillage persistent en base. Les limites de débit par IP sont en mémoire et se réinitialisent au redémarrage. Une déconnexion sur un appareil déconnecte toutes les sessions du même utilisateur. Pas de récupération publique ni de codes de secours à usage unique ; la récupération passe par l’administrateur du VPS.

L’application n’a pas fait l’objet d’un audit externe ni d’un pentest indépendant. Les tests automatisés ne prouvent pas l’absence de vulnérabilité. Il reste à vérifier le réseau, le TLS et la restauration sur votre infrastructure avant d’y importer des données réelles. Les scans de dépendances, sauvegardes planifiées et supervision ne s’exécutent pas automatiquement.

## Vérifications utiles

La version courante **v4** conserve la fréquence annuelle dans les enveloppes chiffrées et empêche les images v3, limitées au mensuel, de les rouvrir. Les anciennes récurrences restent mensuelles par défaut. La création, la modification et la comptabilisation des récurrences restent limitées aux comptes accessibles à l’utilisateur, avec validation de fréquence côté serveur et protection antiforgery.

Le marqueur SQLite v2 empêche le démarrage d’une ancienne image v1 qui ignorerait les droits. Les comptes et objectifs v1 sans visibilité sont masqués jusqu’au classement administratif avec `--classify-accounts`. Le changement de version et le classement créent des sauvegardes préalables dans `/data/backups`. Ces commandes nécessitent un accès au terminal du serveur, application web arrêtée. L’administrateur du serveur reste capable de déchiffrer les données : il ne s’agit pas d’un chiffrement de bout en bout entre utilisateurs.

Le marqueur actuel **v3** ajoute la protection contre le retour à une image v2 qui ignorerait puis supprimerait les prêts lors d’une écriture. Les prêts, règlements et exports d’amortissement sont filtrés comme les comptes personnels et communs. Les comptes de remboursement doivent avoir la même visibilité et le même titulaire que le prêt ; les comptes personnels contenant un prêt lié ou ses débits ne peuvent pas être partagés. Confirmation et annulation des règlements sont atomiques avec les opérations générées ; une même échéance ne peut pas être confirmée deux fois par double soumission. Aucun taux courant n’est téléchargé et aucun paiement bancaire réel n’est exécuté.

```text
dotnet build CashHarbor.client
dotnet run --project CashHarbor.Tests
dotnet list CashHarbor.client package --vulnerable --include-transitive
```

Références : [cookies](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [CSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [clés au repos](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0).
