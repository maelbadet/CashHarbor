# Vérifications de livraison

Vérifications effectuées le 17 novembre 2026, avec des données exclusivement fictives.

| Vérification | Résultat |
| --- | --- |
| Construction de la solution .NET 10 | Réussite, zéro avertissement et zéro erreur |
| Suites exécutables métier / stockage / HTTP | 17 sur 17 |
| Vecteurs officiels RFC 6238 | Six vecteurs SHA-1 vérifiés |
| Précision des montants et dates limites | Validées |
| Virements atomiques et suppression des deux écritures | Validés |
| Récurrences en fin de mois et relance sans doublon | Validées |
| Écritures concurrentes et rejet d’un acteur inconnu | Validés |
| Enveloppe financière chiffrée et CSV protégé des formules | Validés |
| TOTP obligatoire, refus des replays, verrouillage persistant, plafond de deux accès | Validés |
| Serveur HTTPS réel : pages et exports protégés, CSRF, cookie, hôte, révocation à la déconnexion | Validés |
| CSS et icône accessibles sur l’hôte réel | Validés |
| Chrome isolé : pages et vues personnelles, communes et épargne, 390 et 1 440 px | 20 parcours validés, thème rouge et noir appliqué, aucun débordement horizontal global, relevés de solde dépliés dans la vue épargne |
| Captures du tableau de bord mobile et ordinateur | Inspectées visuellement |
| NuGet : dépendances directes et transitives vulnérables connues | Aucune signalée par les sources consultées |
| Configuration Docker Compose | Valide avec origine HTTPS de test |
| Image Linux : compilation Release | Réussite |
| Conteneur Linux de production | Démarrage réussi, UID 1654, racine en lecture seule, volumes inscriptibles, certificat de protection des clés, proxy HTTPS, connexion et CSS |
| Deux sessions HTTPS distinctes | Pages, filtres, graphiques et exports sans données personnelles du partenaire ; falsification d’identifiants et de titulaire rejetée |
| Écritures privées et communes | Fusion du périmètre autorisé sans perte des comptes cachés, échéances privées non comptabilisées par le partenaire, suppression de virement vers un compte privé refusée |
| Relevés et valorisations | Centimes exacts, remplacement à date identique, réconciliation après ajout rétroactif, aucun revenu artificiel |
| Reprise d’une enveloppe JSON au format v1 | Masquage avant classement, propriétaires explicites, sauvegarde préalable, soldes et opérations conservés, relance sans effet |
| Marqueur SQLite v2 | Anciennes images v1 bloquées pour empêcher le contournement des nouveaux droits |

Le conteneur de contrôle et ses volumes anonymes ont été supprimés après vérification. Aucun compte, secret ou serveur de production n’a été créé pour vous. Aucun accès au VPS n’a été effectué.

Restent à vérifier sur le VPS : règles Tailscale pour vos deux identités, nom HTTPS, adresse du proxy, pare-feu, permissions réelles des secrets, sauvegarde hors site et restauration. Le script de sauvegarde et la procédure de restauration ne constituent pas une restauration déjà prouvée sur votre infrastructure. Aucun pentest externe ni audit indépendant n’a été réalisé.
