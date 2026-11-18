# Objectifs liés à un compte

Dans Objectifs, choisir un **compte à suivre**, un montant cible et une date. Le solde courant enregistré dans CashHarbor est utilisé pour la progression, aussi bien dans la page Objectifs que sur le tableau de bord. Il évolue avec les opérations et relevés saisis ; aucune connexion bancaire automatique n’est ajoutée.

Exemple : LEP à **1 050 €**, cible **6 000 €**, échéance dans **12 mois**. Il reste **4 950 €**, soit **412,50 € par mois**, sans intérêts futurs. Les versements mensuels sont comptés à partir du mois prochain, au même jour, ajusté aux fins de mois ; seuls ceux arrivant au plus tard à l’échéance sont retenus. Le montant est arrondi au centime supérieur. Une échéance dépassée ou de moins d’un mois affiche le montant restant sans inventer une mensualité.

Le calcul n’ajoute pas les intérêts du LEP, les frais ou des retraits futurs. Les intérêts déjà enregistrés dans le compte font naturellement partie du solde. Si le solde dépasse la cible, l’objectif est atteint. Plusieurs objectifs rattachés au même compte suivent le même solde : ils ne constituent pas des réservations indépendantes et ne doivent pas être additionnés comme des fonds séparés.

Sans compte associé, le fonctionnement déclaratif reste disponible. Le champ de montant manuel est ignoré lorsqu’un compte est lié. Le rattachement est modifiable ; il ne transfère aucun argent.

Un objectif personnel utilise un compte du même titulaire ; un objectif Couple utilise un compte commun. La suppression d’un compte lié est bloquée tant que les objectifs ne sont pas détachés. Un partage de compte qui rendrait ses objectifs personnels incompatibles est également refusé.

Cette évolution ajoute un lien dans les données financières chiffrées. Au démarrage, le passage du format v4 à v5 crée une sauvegarde du coffre avant de marquer la nouvelle compatibilité. Les anciennes images v4 refusent ensuite le coffre v5, pour éviter d’effacer les liens qu’elles ne connaissent pas. Ne pas réinstaller une ancienne image sur ce volume ; restaurer la sauvegarde correspondant à cette image si un retour arrière est nécessaire.
