# CashHarbor

Le [guide du tableau de bord](docs/DASHBOARD.md) décrit les graphiques, les filtres et la mise à jour Docker.

Application privée .NET 10 pour un foyer de deux personnes. Interface française responsive dans le thème demandé : noir #101010, panneaux #1a1a1a, rouge #ef4444 et Segoe UI / Inter / sans-serif.

## Fonctionnalités

- Deux utilisateurs provisionnés en terminal, sans inscription publique.
- Mot de passe + TOTP obligatoire, verrouillage après 5 échecs, session de 30 minutes, révocation serveur à la déconnexion.
- Comptes courants, épargne et investissements personnels ou communs, soldes initiaux et opérations en euros.
- Dépenses, revenus, virements internes atomiques ; recherche, filtres et pagination.
- Tableau de bord, évolution des soldes sur six mois, relevés de valorisation, flux mensuels, répartition des dépenses et objectifs personnels ou communs.
- Échéances mensuelles ou annuelles, modifiables et comptabilisées sur confirmation, sans doublon à la relance. Les dépenses annuelles essentielles sont mensualisées uniquement pour l’indicateur de réserve.
- Réserve de précaution paramétrable, exports CSV et JSON, journal d’activité.
- Prêts personnels ou communs à taux fixe : taux nominal, TAEG informatif, trois modes d’assurance, frais, échéancier exportable, reprise des mensualités déjà payées et confirmation sans double débit. Le solde net déduit le capital restant dû des comptes suivis.
- Données financières chiffrées par ASP.NET Core Data Protection dans SQLite. En production, clés protégées par certificat obligatoire.

Aucune connexion bancaire, aucun identifiant bancaire, aucune collecte de télémétrie, aucune ressource externe dans l’interface. Chacun voit ses comptes et objectifs personnels ainsi que les éléments communs. Les opérations, graphiques et exports suivent ces droits ; les données privées du partenaire sont exclues côté serveur. Les produits peuvent être nommés librement, mais les règles réglementaires, taux d’intérêt historiques, catégories hiérarchiques et recommandations du diagramme complet ne sont pas encore implémentés.

## Architecture

Le stockage reste local au conteneur de l’application : SQLite écrit dans `/data/cashharbor.db`, sans serveur de base de données séparé. Vos deux accès partagent cette même base. Le volume Docker `cashharbor_data` conserve les données lorsque le conteneur est recréé ; `cashharbor_keys` conserve les clés de chiffrement. Sauvegarder ces deux volumes avec les secrets. La commande `docker compose down -v` supprime les volumes et leurs données : utiliser `docker compose down` pour un arrêt sans suppression.

- **Domain** : modèle métier et valeurs monétaires en centimes.
- **Application** : cas d’usage, validations et interfaces des adaptateurs.
- **Infra** : EF Core / SQLite, chiffrement, accès utilisateurs et TOTP.
- **client** : hôte ASP.NET Core et pages Blazor rendues sur le serveur, sans circuit Blazor interactif. Un petit module JavaScript local enrichit les graphiques ; les calculs et les droits restent côté serveur.
- **Tests** : suites exécutables métier, stockage et parcours HTTP sur serveur HTTPS temporaire.
- **api** : adaptateur réservé, sans API financière. Ne pas le démarrer pour utiliser l’application.

Les dépendances pointent vers le métier : Domain ← Application ← Infra. L’hôte assemble les adaptateurs ; les pages appellent Application. Un seul processus, une seule instance et un espace partagé : pas de microservices.

## Mise à jour d’une installation existante

Suivre [la reprise des comptes personnels et communs](docs/UPGRADE-PRIVATE-ACCOUNTS.md). Les données anciennes sans titulaire restent masquées jusqu’au classement explicite en terminal. La mise à jour conserve les données, les accès, les clés et les volumes.

Pour les prêts, suivre [le guide du suivi des crédits](docs/LOANS.md). Le passage automatique v2 vers v3 sauvegarde la base avant modification du marqueur de compatibilité ; les prêts existants restent vides jusqu’à leur saisie.

La version courante **v4** ajoute les [récurrences annuelles](docs/RECURRENCES.md) et conserve les récurrences existantes en mensuel. Le passage depuis v3 sauvegarde automatiquement la base avant de bloquer les anciennes images qui ignorent la fréquence annuelle.

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

Un profil Chrome temporaire isolé vérifie les pages et les vues personnelles, communes et épargne à 390 et 1 440 px, l’application réelle du thème rouge et noir et l’absence de débordement horizontal global. Les captures sont rangées avec les fixtures. Voir [les vérifications de livraison](docs/VALIDATION.md).

## VPS Linux

Suivre [le guide de déploiement privé](docs/DEPLOYMENT.md) : Docker, Tailscale Serve, deux accès, certificat de protection des clés, sauvegarde et restauration. L’application ne doit pas être exposée directement sur Internet.

Lire [les propriétés et limites de sécurité](docs/SECURITY.md) et [les choix du modèle](docs/ARCHITECTURE.md). Cette livraison est une première version fonctionnelle, pas une certification de sécurité ni une garantie de disponibilité.

Sources techniques : [authentification par cookie](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [protection des clés](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-encryption-at-rest?view=aspnetcore-10.0), [Tailscale HTTPS](https://tailscale.com/docs/how-to/set-up-https-certificates).
