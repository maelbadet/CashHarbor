# CashHarbor

Application privée .NET 10 pour un foyer de deux personnes. Interface française responsive reprenant la palette CWC : crème #f7f3ee, noir #111111, orange #f97316 et Segoe UI / Inter / sans-serif.

## Fonctionnalités

- Deux utilisateurs provisionnés en terminal, sans inscription publique.
- Mot de passe + TOTP obligatoire, verrouillage après 5 échecs, session de 30 minutes, révocation serveur à la déconnexion.
- Comptes courants, épargne et investissements, soldes initiaux et opérations en euros.
- Dépenses, revenus, virements internes atomiques ; recherche, filtres et pagination.
- Tableau de bord, graphique sur six mois, répartition des dépenses et objectifs déclaratifs.
- Échéances mensuelles comptabilisées sur confirmation, sans doublon à la relance.
- Réserve de précaution paramétrable, exports CSV et JSON, journal d’activité.
- Données financières chiffrées par ASP.NET Core Data Protection dans SQLite. En production, clés protégées par certificat obligatoire.

Aucune connexion bancaire, aucun identifiant bancaire, aucune collecte de télémétrie, aucune ressource externe dans l’interface. Les deux utilisateurs partagent toutes les données du foyer. Les produits réglementés, taux d’intérêt historiques, catégories hiérarchiques et recommandations du diagramme complet ne sont pas encore implémentés.

## Architecture

Le stockage reste local au conteneur de l’application : SQLite écrit dans `/data/cashharbor.db`, sans serveur de base de données séparé. Vos deux accès partagent cette même base. Le volume Docker `cashharbor_data` conserve les données lorsque le conteneur est recréé ; `cashharbor_keys` conserve les clés de chiffrement. Sauvegarder ces deux volumes avec les secrets. La commande `docker compose down -v` supprime les volumes et leurs données : utiliser `docker compose down` pour un arrêt sans suppression.

- **Domain** : modèle métier et valeurs monétaires en centimes.
- **Application** : cas d’usage, validations et interfaces des adaptateurs.
- **Infra** : EF Core / SQLite, chiffrement, accès utilisateurs et TOTP.
- **client** : hôte ASP.NET Core et pages Blazor rendues sur le serveur, sans circuit interactif ni dépendance JavaScript.
- **Tests** : suites exécutables métier, stockage et parcours HTTP sur serveur HTTPS temporaire.
- **api** : adaptateur réservé, sans API financière. Ne pas le démarrer pour utiliser l’application.

Les dépendances pointent vers le métier : Domain ← Application ← Infra. L’hôte assemble les adaptateurs ; les pages appellent Application. Un seul processus, une seule instance et un espace partagé : pas de microservices.

## Démarrage local Windows

Installer le SDK .NET 10. Depuis ce dossier :

```powershell
dotnet dev-certs https --trust
dotnet restore CashHarbor.slnx
dotnet build CashHarbor.slnx
dotnet run --project CashHarbor.client --launch-profile https -- --setup
```

Créer un accès, puis relancer la commande de provisionnement pour le deuxième. Enregistrer la clé TOTP dans une application d’authentification ; valider avec son code. Les mots de passe sont saisis sans affichage. Aucun compte par défaut.

```powershell
dotnet run --project CashHarbor.client --launch-profile https
```

Ouvrir **https://localhost:7105**. Le mode Development sert uniquement au développement : ses clés ne sont pas protégées par certificat et il ne doit pas recevoir vos vraies données financières. La session expire après 30 minutes ; un formulaire expiré doit être rechargé.

## Vérifications

```powershell
dotnet build CashHarbor.client
dotnet run --project CashHarbor.Tests
```

La dernière suite lance l’hôte Debug déjà compilé et teste un véritable serveur HTTPS local avec un certificat temporaire. Les fixtures synthétiques restent sous .test-artifacts (ignoré par Git) pour diagnostic. Ne jamais y placer des données réelles.

Contrôle visuel optionnel sous Windows (Node.js 22+ et Chrome requis) :

```powershell
$env:CASHHARBOR_BROWSER='C:/Program Files/Google/Chrome/Application/chrome.exe'
dotnet run --project CashHarbor.Tests
```

Un profil Chrome temporaire isolé vérifie les sept pages à 390 et 1 440 px, l’application réelle des styles et l’absence de débordement horizontal global. Les captures sont rangées avec les fixtures. Voir [les vérifications de livraison](docs/VALIDATION.md).

## VPS Linux

Suivre [le guide de déploiement privé](docs/DEPLOYMENT.md) : Docker, Tailscale Serve, deux accès, certificat de protection des clés, sauvegarde et restauration. L’application ne doit pas être exposée directement sur Internet.

Lire [les propriétés et limites de sécurité](docs/SECURITY.md) et [les choix du modèle](docs/ARCHITECTURE.md). Cette livraison est une première version fonctionnelle, pas une certification de sécurité ni une garantie de disponibilité.

Sources techniques : [authentification par cookie](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [protection des clés](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0), [Tailscale HTTPS](https://tailscale.com/docs/how-to/set-up-https-certificates).
