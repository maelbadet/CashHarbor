# Prêts et crédits à taux fixe

Le menu **Prêts et crédits** permet de suivre des prêts immobiliers, auto, à la consommation ou étudiants amortissables à taux fixe, avec échéances mensuelles. Le prêt peut être personnel ou commun. Le classement et les filtres côté serveur s’appliquent au contrat, à ses règlements et à ses exports.

## Ajouter ou reprendre un prêt en cours

Saisir le capital initial, le taux nominal annuel fixe, la durée initiale en mois et la date de la première échéance. Pour reprendre un prêt déjà commencé, renseigner le nombre de mensualités réellement réglées : leur capital amorti sera repris sans créer d’anciennes dépenses. Le capital restant n’est jamais diminué simplement parce qu’une date est passée.

L’assurance propose trois modes : montant mensuel fixe, taux annuel sur capital initial, ou taux annuel sur capital restant au début de l’échéance. Saisir le montant ou le taux total correspondant au contrat et à la couverture des emprunteurs ; les quotités et garanties ne sont pas déduites automatiquement.

Les frais connus sont ajoutés au coût estimé, sans être débités automatiquement. Le **TAEG du contrat** est une valeur informative, distincte du taux nominal : il n’est ni recalculé, ni utilisé à la place du taux nominal pour produire le tableau. Le coût affiché additionne uniquement les intérêts, assurances et frais saisis ; il ne constitue pas un calcul réglementaire de TAEG. Références : [Service Public — TAEG](https://www.service-public.gouv.fr/particuliers/vosdroits/F2456), [Ministère de l’Économie — informations du crédit immobilier](https://www.economie.gouv.fr/particuliers/gerer-mon-argent/emprunter-et-sassurer/credit-immobilier-quelles-informations-la-banque-doit-elle-vous-fournir).

L’amortissement utilise un taux mensuel proportionnel au taux nominal annuel (taux / 12), une mensualité estimée puis des intérêts arrondis au centime à chaque échéance. Le dernier règlement est ajusté pour solder exactement le capital. Pour de très petits montants ou certaines combinaisons de durée et d’arrondis, le prêt peut se solder un peu avant la durée initialement saisie ; le nombre d’échéances calculé est affiché. Les paramètres qui ne permettent pas un amortissement positif au centime sont refusés. Le jour d’échéance est conservé, avec le dernier jour du mois pour les mois plus courts.

L’échéancier de la banque peut différer selon ses conventions, la date de déblocage et les arrondis. Cette version ne traite pas les taux variables, différés, prêts in fine, remboursements anticipés, renégociations, impayés avec pénalités ou assurances à barème personnalisé. Comparer les résultats au contrat avant de les utiliser pour le suivi.

## Confirmer un règlement

Ouvrir le prêt et confirmer sa prochaine échéance arrivée à terme. Saisir la date réelle, entre la date de début de suivi ou le dernier règlement et aujourd’hui.

- **Suivi du prêt uniquement**, valeur par défaut : le capital restant diminue et aucune opération bancaire n’est créée. Choisir ce mode si la mensualité figure déjà dans les opérations, dans une récurrence ou doit être saisie séparément.
- **Créer aussi la dépense** : une opération unique débite le compte associé du total capital + intérêts + assurance. Elle est enregistrée dans la même transaction que le règlement. Une nouvelle soumission de la même échéance est rejetée. Si une dépense identique existe sur ce compte à la même date, la création est refusée afin de demander une vérification ; ce contrôle ne remplace pas la vérification des imports ou des dates différentes.

Le compte associé doit avoir la même visibilité et le même titulaire que le prêt. Un prêt personnel ne peut pas débiter automatiquement un compte commun : son libellé et son lien au prêt risqueraient de révéler des informations privées. Un prêt peut aussi être suivi sans compte associé.

La prochaine mensualité de chaque prêt apparaît dans **Échéances**, mais le bouton de comptabilisation des récurrences ne confirme pas les prêts. Il faut choisir une seule source de dépense : une récurrence existante, une saisie manuelle ou le débit généré depuis le prêt.

## Corriger ou annuler

Le dernier règlement enregistré peut être annulé : le capital est rétabli et seule l’éventuelle dépense générée par ce règlement est supprimée. Une opération saisie indépendamment reste inchangée. L’opération liée ne peut pas être supprimée directement depuis Opérations, pour ne pas désynchroniser le prêt.

Les conditions du contrat restent modifiables tant qu’aucun règlement n’a été enregistré depuis son ajout. Pour corriger un contrat ensuite, annuler les règlements du plus récent au plus ancien avant la modification. La visibilité reste fixée à la création. Le compte associé peut être changé pour les prochains règlements ; les précédents conservent leurs liens d’origine.

Un compte personnel lié à un prêt personnel ou contenant ses remboursements générés ne peut pas être rendu commun, afin de ne pas exposer indirectement le prêt. Un prêt avec des règlements enregistrés ne peut pas être supprimé avant leur annulation.

## Solde net et exports

Le tableau de bord affiche le **solde net suivi** : soldes des comptes accessibles moins le capital restant des prêts accessibles. Les intérêts futurs et la valeur des biens immobiliers ne sont pas inclus. Il ne s’agit donc pas d’une évaluation du patrimoine immobilier complet.

L’export JSON contient les prêts et règlements accessibles à la personne connectée. Chaque échéancier peut être exporté en CSV, sans rendre un prêt personnel accessible au partenaire. Les graphiques historiques des comptes continuent à montrer les soldes des comptes, sans reconstruire un historique de dette qui n’a pas été saisi.

## Mise à jour Docker

Sauvegarder les volumes data et keys avec .secrets, puis mettre les nouveaux fichiers sur le serveur en conservant .env et les secrets actuels. Depuis le dossier du projet :

```bash
sudo docker compose stop app
sudo docker compose build
sudo docker compose up -d
sudo docker compose logs --tail=50 app
```

Le marqueur de base passe automatiquement de **v2 à v3**, avec une sauvegarde SQLite préalable dans `/data/backups/before-loans-v3-*.db`. Les tables, données de comptes, accès, clés et paramètres de visibilité existants sont conservés. Les anciennes images v2 sont bloquées : elles ignoreraient les nouveaux champs de prêts et pourraient les supprimer lors d’une écriture.

Si vous passez directement de la toute première version v1, appliquer aussi [le classement des comptes personnels et communs](UPGRADE-PRIVATE-ACCOUNTS.md) avant de reprendre les saisies. Aucune clé ni aucun certificat ne doit être régénéré.
