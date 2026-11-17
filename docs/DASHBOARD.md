# Tableau de bord et graphiques

Le tableau de bord propose des périodes de 1, 3, 6 ou 12 mois, l’année en cours et des dates personnalisées inclusives. La vue peut regrouper les comptes personnels et communs, ou sélectionner une seule visibilité et un seul compte. Chaque utilisateur reçoit uniquement ses comptes personnels et les comptes communs.

Les soldes sont calculés en centimes, à la fin de chaque journée, depuis les soldes initiaux et les opérations enregistrées. La courbe suit des paliers sans lissage. Une date antérieure au début du suivi d’un compte ne constitue pas un solde connu : la couverture partielle est signalée. Les valorisations manuelles influencent les soldes ; cette courbe ne mesure pas un rendement de placement.

Les revenus et dépenses excluent les virements internes et les ajustements. Une facture annuelle apparaît intégralement le jour de sa comptabilisation. Le regroupement quotidien, hebdomadaire ou mensuel conserve les mêmes totaux, y compris pour les semaines et mois partiels. La comparaison utilise la période précédente de même nombre de jours ; aucun pourcentage n’est inventé si la base est nulle.

Au survol, au toucher ou au clavier (flèches, Début, Fin), les graphiques affichent les montants exacts. Les séries peuvent être masquées avec la légende. Les tableaux dépliables présentent toutes les valeurs sans dépendre de JavaScript. Les exports SVG reprennent les séries affichées ; le CSV reprend les valeurs quotidiennes filtrées et le nombre de comptes renseignés.

Les comptes et engagements, échéances et projets portent sur la situation actuelle et sont explicitement distingués de la période historique. Le graphique de prêt est une projection contractuelle du capital restant dû, sans prédiction de règlements futurs.

Les calculs et contrôles d’accès restent dans le serveur. Le module local `charts.js` apporte uniquement les interactions et l’export SVG, sans service tiers, stockage navigateur ni calcul financier. Les fichiers exportés contiennent des données financières lisibles : conservez-les dans votre espace privé.

## Installation de cette mise à jour

Copier les sources actualisées sur le VPS en conservant `.env`, `.secrets` et les volumes Docker. Depuis `/srv/docker/www/CashHarbor` :

```sh
docker compose build
docker compose up -d
docker compose logs --tail=50 app
```

Cette évolution ne modifie pas le schéma de stockage v4. Les données existantes sont conservées dans les volumes. Recharger complètement la page après le redémarrage pour récupérer la feuille de style et le module des graphiques.
