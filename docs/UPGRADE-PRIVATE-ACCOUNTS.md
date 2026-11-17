# Mise à jour : comptes personnels et communs

Cette version conserve SQLite dans le conteneur, la base `/data/cashharbor.db`, les volumes `cashharbor_data` / `cashharbor_keys`, vos deux accès et vos secrets actuels. Ne pas régénérer le certificat ni les clés. Ne pas supprimer les volumes.

## Avant la mise à jour

Faire une sauvegarde complète et protégée des volumes data et keys avec le dossier .secrets, selon [la procédure de sauvegarde](DEPLOYMENT.md#5-sauvegarder-et-restaurer). Une copie du JSON exporté n’est pas une sauvegarde complète des accès et des clés.

Mettre les nouveaux fichiers du dépôt sur le serveur sans écraser .env, .secrets ou les données persistantes. Depuis votre session SSH :

```bash
cd /srv/docker/www/CashHarbor
sudo docker compose stop app
sudo docker compose build
sudo docker compose run --rm --no-deps app --classify-accounts
```

La commande affiche chaque compte et objectif existant, avec les choix suivants :

- **0 : commun**, visible par vous deux.
- **1 ou 2 : personnel**, visible uniquement par l’adresse e-mail affichée pour ce choix. Lire les adresses : l’ordre est alphabétique, pas l’ordre de création.

Vérifier tous les choix, puis saisir **CLASSER** pour confirmer. Un compte commun rend visibles son nom, tous ses soldes, ses opérations passées et futures, ses relevés et ses échéances. Un compte personnel ne sera présent que dans la vue de son titulaire.

Les comptes et objectifs anciens restent masqués pour les deux personnes tant qu’ils ne sont pas classés. Rien n’est effacé et aucun propriétaire n’est deviné. La commande peut être relancée si elle est annulée ; après un classement réussi, elle n’effectue plus de modification.

Le passage du marqueur SQLite v1 à v2 crée automatiquement une sauvegarde préalable dans `/data/backups/before-privacy-v2-*.db`. Le classement en crée une autre dans `/data/backups/before-access-classification-*.db`. Ces copies financières restent chiffrées avec vos clés actuelles. Elles ne remplacent pas votre sauvegarde complète hors serveur.

## Redémarrer

```bash
sudo docker compose up -d
sudo docker compose logs --tail=50 app
```

L’adresse Tailscale et la valeur CASHHARBOR_ORIGIN restent identiques. Se connecter séparément avec chacun des deux accès et vérifier :

1. Chacun voit ses propres comptes et objectifs personnels, sans ceux de son partenaire.
2. Les comptes et objectifs communs apparaissent chez les deux utilisateurs.
3. Les graphiques, opérations, échéances et exports ne contiennent que les comptes accessibles à la personne connectée.
4. Les soldes et opérations précédemment saisis sont conservés.

Ne pas redémarrer une ancienne image v1 sur cette base : le marqueur v2 interdit ce retour pour empêcher une exposition des comptes privés par l’ancien code. Pour un retour complet à l’ancienne version, il faudrait restaurer une sauvegarde complète d’avant la mise à jour, et retrouver son ancien modèle où toutes les données étaient communes. Cette restauration ferait perdre les saisies postérieures : conserver l’instance v2 et diagnostiquer toute erreur avant de revenir en arrière.

## Utilisation

Dans **Comptes**, choisir **Personnel** ou **Commun** à la création. Personnel est la valeur par défaut et le titulaire est toujours l’utilisateur connecté ; le formulaire ne permet pas de créer un compte privé au nom de l’autre personne.

La vue **Épargne et placements** regroupe livrets et investissements accessibles, avec une indication de leur visibilité. Le champ produit est un libellé de suivi, sans calcul automatique d’intérêts ni validation des règles bancaires de détention.

Dans **Évolution et relevés de solde**, enregistrer le solde réellement constaté à une date. Le système génère un ajustement, distinct des revenus et dépenses. Si une opération plus ancienne est ajoutée ensuite, les ajustements sont recalculés pour conserver les soldes constatés. Un relevé à la même date remplace le précédent, sans doublon.

Le titulaire peut rendre un compte personnel commun après une confirmation explicite du partage de tout son historique. Un compte commun ne peut pas redevenir personnel depuis l’interface. Les virements ne peuvent utiliser que des comptes accessibles ; une personne ne peut pas supprimer une écriture commune si cela modifierait le compte privé de son partenaire. Le titulaire ayant accès aux deux comptes peut supprimer les deux écritures ensemble.

Les objectifs ont eux aussi une visibilité personnelle ou commune, fixée à la création. Le journal affiché dans Paramètres contient uniquement les événements de l’utilisateur connecté. Le nom du foyer et les paramètres de réserve de précaution restent communs.
