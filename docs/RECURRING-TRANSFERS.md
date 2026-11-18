# Classement des comptes et virements programmés

La page Comptes affiche, dans cet ordre : **Comptes courants et livrets**, **Placements et actions**, puis **Cryptos**. Le type Crypto est distinct du type Placement. Pour reclasser un ancien compte, ouvrir Modifier et choisir son type ; aucune classification n’est devinée à partir du nom.

Dans **Échéances → Programmer un virement entre comptes**, choisir un compte source de l’espace courant, un destinataire accessible, un montant positif, une première date et une fréquence mensuelle ou annuelle. Les comptes de placement peuvent recevoir un versement, par exemple vers une assurance-vie. Un virement vers le compte commun depuis un compte personnel est possible sans mélanger les budgets.

Au moment de confirmer les échéances arrivées à terme, CashHarbor ajoute atomiquement deux écritures : débit du compte source et crédit du destinataire. Elles partagent un identifiant de transfert et de récurrence. Une relance ne crée pas de doublon. Les virements sont exclus des revenus, dépenses et charges essentielles utilisées pour la réserve de sûreté. Le jour d’ancrage est conservé lorsque les mois sont plus courts.

Ce suivi ne passe aucun ordre bancaire. Les échéances nécessitent confirmation dans l’application après vérification de la banque. Aucun transfert futur n’est appliqué à l’avance et aucun service bancaire n’est connecté.

Une programmation reliant un compte personnel reste visible et modifiable uniquement par le titulaire qui accède aux deux comptes. Le partenaire voit le mouvement sur le compte commun une fois comptabilisé, comme pour un virement interne manuel, sans accès au compte personnel. Les mises à jour du partenaire conservent les programmations qu’il ne peut pas voir. Un compte utilisé comme source ou destinataire ne peut pas être supprimé avant l’arrêt de la récurrence.

Pour modifier le montant ou la date, ouvrir Modifier dans le tableau des échéances. Le montant d’un virement programmé y reste négatif, car il décrit le débit source ; la création demande un montant positif. Pour changer les comptes, arrêter la programmation puis en créer une autre. Arrêter une programmation conserve les mouvements déjà enregistrés.

Le format de compatibilité passe à v7, avec sauvegarde automatique d’un coffre v6 avant ouverture. Les anciennes images refusent ensuite ce nouveau format, afin de ne pas perdre les destinataires des virements ni interpréter les comptes Crypto comme de l’épargne disponible.
