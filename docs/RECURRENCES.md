# Récurrences mensuelles et annuelles

Dans **Échéances**, ajouter une récurrence puis choisir **Mensuelle** ou **Annuelle**. La fréquence est également affichée dans la liste.

Exemple pour une facture d’eau payée une seule fois par an :

- Libellé : Eau.
- Fréquence : Annuelle.
- Montant : **-600** pour une facture de 600 €.
- Première date : la date réelle de la prochaine facture.
- Compte : le compte personnel ou commun qui paie la facture.

Le montant saisi est celui de chaque paiement, sans division par douze. Quand l’échéance est arrivée, **Comptabiliser les échéances** crée une dépense de 600 €, puis avance la prochaine date d’un an. Aucune dépense mensuelle de 50 € n’est créée. Les échéances en retard sont rattrapées à leurs dates annuelles ; relancer la comptabilisation ne les duplique pas. Le bouton ne déclenche aucun paiement auprès d’une banque.

## Modifier une récurrence existante

Dans la colonne Action, ouvrir **Modifier**, changer la fréquence, le montant ou la prochaine date puis enregistrer. Le compte associé reste identique. Les opérations déjà comptabilisées conservent leurs montants et leurs dates.

La prochaine date détermine le nouveau calendrier si elle est modifiée, ou si la fréquence change. Une simple modification du libellé ou du montant conserve l’ancrage de date existant : le 31 reste un jour de fin de mois et le 29 février reste un ancrage bissextile.

Pour une récurrence annuelle du 29 février, les années non bissextiles utilisent le 28 février, puis le 29 février revient lors de la prochaine année bissextile. Pour une récurrence mensuelle du 31, les mois plus courts utilisent leur dernier jour.

Les droits restent ceux du compte : une facture sur compte personnel est privée ; une facture sur compte commun est accessible aux deux utilisateurs. Un utilisateur ne peut pas modifier une récurrence privée du partenaire en devinant son identifiant.

## Réserve de précaution

Les dépenses marquées essentielles sont exprimées en moyenne mensuelle pour cet indicateur uniquement. Une dépense annuelle de 600 € contribue à hauteur de 50 €/mois. Le total est arrondi au centime après addition des équivalents mensuels. La comptabilisation réelle reste annuelle et conserve le montant complet.

## Mise à jour Docker

Sauvegarder les volumes data et keys avec .secrets, puis copier les nouveaux fichiers sans remplacer .env ni les secrets. Depuis le dossier du projet :

```bash
sudo docker compose stop app
sudo docker compose build
sudo docker compose up -d
```

La version de compatibilité de la base passe automatiquement de v3 à **v4**, avec sauvegarde SQLite dans `/data/backups/before-annual-recurrences-v4-*.db`. Les comptes, prêts et opérations sont conservés. Les récurrences existantes dépourvues de fréquence restent **mensuelles**. Les anciennes images v3 sont bloquées pour éviter qu’elles traitent une facture annuelle comme mensuelle. Aucun nouveau secret ni certificat n’est nécessaire.
