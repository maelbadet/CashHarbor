# Modèle et dépendances

CashHarbor.Domain ne dépend d’aucun framework de stockage ou d’interface. Il définit Workspace, FinancialAccount, Transaction, FinancialGoal, RecurringOperation, EmergencyFundPolicy et BalanceSnapshot. Les valeurs sont en centimes d’euro signés ; le calcul du solde additionne les écritures à un solde initial daté.

CashHarbor.Application dépend uniquement de Domain. FinanceService porte les validations et cas d’usage. IWorkspaceStore et IIdentityService définissent les ports. Aucune requête EF, aucun cookie et aucun appel HTTP n’existe dans cette couche. TimeProvider rend les dates testables.

CashHarbor.Infra implémente les ports. SQLite stocke trois types de lignes : utilisateurs locaux, enveloppe chiffrée du foyer et journal technique. L’enveloppe est adaptée à deux utilisateurs et à une instance : modification sous verrou, contrôle optimiste de révision, enregistrement atomique de l’enveloppe et du journal. Les données ne sont jamais déchiffrées dans des logs.

CashHarbor.client est l’hôte et la couche de présentation. Le point de composition référence Infra pour brancher les adaptateurs. Les pages utilisent Application / Domain. Les formulaires sont de vrais POST puis redirection, avec antiforgery, et les graphiques sont des SVG serveur accessibles avec tableau de valeurs. Pas de SignalR, de circuit Blazor permanent ni de stockage navigateur.

CashHarbor.api reste un adaptateur réservé et n’est pas inclus dans l’image de production. Ajouter une API publique à l’avenir nécessiterait de définir son authentification et ses politiques d’autorisation, plutôt que de dupliquer directement les accès aux données.

## Choix par rapport au diagramme

- Une enveloppe Workspace chiffrée pour le foyer, deux identités locales. Chaque compte et objectif porte une visibilité Personal / Joint et un OwnerId pour les éléments personnels. WorkspaceAccess filtre les lectures puis fusionne uniquement les écritures autorisées, sans exposer les données cachées à Application. Une évolution multi-foyers exigerait de modifier tous les ports pour imposer un périmètre supplémentaire.
- Établissement libre sur chaque compte, trois types de comptes, EUR uniquement. Le catalogue FinancialProduct, les taux InterestRate et règles fiscales ne sont pas encore implémentés.
- Catégories prédéfinies dans l’interface, sans hiérarchie. Les ports métier acceptent un libellé de catégorie validé.
- BalanceSnapshot enregistre les soldes constatés datés. Des écritures Adjustment, exclues des revenus et dépenses, réconcilient ces soldes. Les graphiques montrent les flux et les soldes historiques personnels / communs visibles. Pas de cours de marché ni de valorisation bancaire automatique.
- Les objectifs enregistrent un montant déclaré, sans déplacer ni réserver de fonds. GoalAccount et les allocations entre objectifs ne sont pas implémentés.
- Les récurrences sont mensuelles ou annuelles et comptabilisées explicitement. La fréquence par défaut reste mensuelle pour relire les anciennes données. Les jours de fin de mois et le 29 février conservent leur ancrage. Les dépenses annuelles sont divisées par douze uniquement pour l’indicateur mensuel de réserve. Pas d’ordre bancaire ni d’arrière-plan effectuant un débit.
- La réserve de précaution est un indicateur explicable, pas un moteur de recommandation de placement ni un conseil financier personnalisé.

## Évolution du stockage

Le module de prêts ajoute `Loan`, `LoanPayment` et `LoanInstallment` dans Domain et un calculateur d’amortissement indépendant du stockage. `LoanService` porte la reprise des prêts, les règlements séquentiels, la création facultative de débits et leur annulation atomique. `WorkspaceAccess` filtre et fusionne également les prêts et leurs règlements, en vérifiant la visibilité du compte débité. Le marqueur courant est désormais **v3** : le passage depuis v2 crée une sauvegarde et conserve les tables. Il empêche une ancienne version d’effacer les champs financiers qu’elle ne connaît pas.

Les tables sont initialisées avec EnsureCreated et marquées PRAGMA user_version=4. La transition depuis v1, v2 ou v3 crée une sauvegarde et interdit aux anciennes images de rouvrir des données privées, d’effacer des prêts inconnus de leur modèle ou de traiter les paiements annuels comme mensuels. Le classement administratif reste nécessaire pour affecter explicitement les comptes et objectifs historiques v1 à leur titulaire ou au foyer. Les prochaines évolutions de tables devront fournir des migrations explicites ; EnsureCreated ne fait pas de migration. Le format chiffré conserve le purpose CashHarbor.Workspace.v1 pour relire les enveloppes existantes et leurs anciennes clés.

Une seule instance à la fois. La sérialisation en mémoire n’est pas un verrou distribué ; le jeton de concurrence réduit les pertes silencieuses sans permettre un déploiement multi-instance. Les commandes de provisionnement et récupération se lancent service arrêté.

## Rapports financiers

`FinancialAnalytics` construit des rapports en lecture seule depuis la tranche de `Workspace` déjà autorisée. Les calculs restent en centimes dans Application : reconstitution quotidienne des soldes, flux regroupés et comparaison sur une durée identique. Les composants Razor produisent les SVG et tableaux ; `charts.js`, servi localement sous la CSP existante, ajoute le curseur, les légendes et l’export SVG. Aucun changement du schéma v4 ni dépendance externe n’est nécessaire. Voir [le guide](DASHBOARD.md).
