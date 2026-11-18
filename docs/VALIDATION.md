# Vérifications de livraison

Vérifications effectuées le 17 novembre 2026, avec des données exclusivement fictives.

| Vérification | Résultat |
| --- | --- |
| Construction de la solution .NET 10 | Réussite, zéro avertissement et zéro erreur |
| Suites exécutables métier / stockage / HTTP | 32 sur 32 |
| Vecteurs officiels RFC 6238 | Six vecteurs SHA-1 vérifiés |
| Précision des montants et dates limites | Validées |
| Virements atomiques et suppression des deux écritures | Validés |
| Récurrences en fin de mois et relance sans doublon | Validées |
| Écritures concurrentes et rejet d’un acteur inconnu | Validés |
| Enveloppe financière chiffrée et CSV protégé des formules | Validés |
| TOTP obligatoire, refus des replays, verrouillage persistant, plafond de deux accès | Validés |
| Serveur HTTPS réel : pages et exports protégés, CSRF, cookie, hôte, révocation à la déconnexion | Validés |
| CSS et icône accessibles sur l’hôte réel | Validés |
| Chrome isolé : comptes, épargne, prêts personnels et communs, détails et autres pages, 390 et 1 440 px | 26 parcours validés, thème rouge et noir appliqué, aucun débordement horizontal global, relevés de solde dépliés dans la vue épargne |
| Captures du tableau de bord mobile et ordinateur | Inspectées visuellement |
| Analyse financière | Soldes quotidiens comparés au journal, centimes exacts, couverture partielle, soldes négatifs, regroupements sans perte, comparaison de périodes et CSV vérifiés |
| Interactions des graphiques | Clavier, toucher, légende, dates personnalisées et export SVG vérifiés à 390 et 1 440 px, sans erreur JavaScript ni violation CSP |
| Export analytique HTTP | Compte privé du partenaire et dates invalides rejetés ; CSV commun identique au rapport autorisé |
| Rappels mensuels | Activation individuelle, fin de mois, année bissextile, heure de Paris et passage à l’heure d’été, reprise des échecs après six heures, déduplication après redémarrage et concurrence vérifiées avec un transport fictif |
| SMTP et confidentialité | Configuration chiffrée sur disque, secret jamais renvoyé, changement de serveur sans ressaisie du secret refusé, destinataire lié à l’identité ; CSRF et falsification de destinataire vérifiées via HTTP |
| Interface SMTP | Formulaires dépliés inspectés à 390 et 1 440 px, aucun débordement global et champ mot de passe vide |
| Transport Hostinger | Bibliothèques SMTP incluses dans l’image Linux ; aucun identifiant réel utilisé et aucun envoi réel effectué. La connexion fournisseur reste à tester après configuration sur le VPS |
| NuGet : dépendances directes et transitives vulnérables connues | Aucune signalée par les sources consultées |
| Configuration Docker Compose | Valide avec origine HTTPS de test |
| Image Linux : compilation Release | Réussite |
| Conteneur Linux de production | Démarrage réussi, UID 1654, racine en lecture seule, volumes inscriptibles, certificat de protection des clés, proxy HTTPS, connexion et CSS |
| Deux sessions HTTPS distinctes | Pages, filtres, graphiques et exports sans données personnelles du partenaire ; falsification d’identifiants et de titulaire rejetée |
| Écritures privées et communes | Fusion du périmètre autorisé sans perte des comptes cachés, échéances privées non comptabilisées par le partenaire, suppression de virement vers un compte privé refusée |
| Relevés et valorisations | Centimes exacts, remplacement à date identique, réconciliation après ajout rétroactif, aucun revenu artificiel |
| Reprise d’une enveloppe JSON au format v1 | Masquage avant classement, propriétaires explicites, sauvegarde préalable, soldes et opérations conservés, relance sans effet |
| Marqueur SQLite v2 | Anciennes images v1 bloquées pour empêcher le contournement des nouveaux droits |
| Prêts à taux fixe | Formule de mensualité comparée à une référence indépendante, capital conservé au centime, dernier solde nul, cas à taux zéro et jours de fin de mois |
| Assurance et TAEG | Trois modes testés, base de capital correcte, TAEG informatif exclu du taux nominal d’amortissement |
| Règlements et reprise de prêts | Débit facultatif atomique, annulation rétablie, double soumission et doublon de dépense refusés, mensualités déjà réglées sans création d’anciens débits ; capital restant dû présenté séparément du solde des comptes |
| Confidentialité des prêts | Contrats, règlements, détails et CSV personnels cachés au partenaire ; comptes de visibilité incompatible et identifiants forgés refusés |
| Marqueur SQLite v3 | Sauvegarde et conservation exacte de l’enveloppe lors du passage v2 vers v3 ; anciennes images v2 bloquées |
| Récurrences annuelles | Paiement complet une fois par an, rattrapage sans doublon, retour au 29 février après les années non bissextiles |
| Édition des récurrences | Fréquence et montant modifiables, historique préservé, jour d’ancrage inchangé lors d’une simple correction de montant, identifiants privés et fréquences invalides refusés |
| Compatibilité mensuelle et réserve | Anciennes enveloppes JSON lues en mensuel ; montant annuel divisé par douze uniquement pour le calcul indicatif de réserve |
| Marqueur SQLite v4 | Sauvegarde et conservation exacte de l’enveloppe v3 ; anciennes images limitées au mensuel bloquées |

Le conteneur de contrôle et ses volumes anonymes ont été supprimés après vérification. Aucun compte, secret ou serveur de production n’a été créé pour vous. Aucun accès au VPS n’a été effectué.

Restent à vérifier sur le VPS : règles Tailscale pour vos deux identités, nom HTTPS, adresse du proxy, pare-feu, permissions réelles des secrets, sauvegarde hors site et restauration. Le script de sauvegarde et la procédure de restauration ne constituent pas une restauration déjà prouvée sur votre infrastructure. Aucun pentest externe ni audit indépendant n’a été réalisé.

Mise à jour du 18 novembre 2026 : 35 suites validées. Plans mensuels chiffrés et isolés entre utilisateurs, espaces et mois ; validation des pourcentages ; formulaire budgétaire partagé vérifié via HTTPS avec CSRF et redirection conservant l’espace. Projections testées à 1, 2, 5, 10, 17, 20, 30, 41 et 60 ans, médianes paires et impaires, taux nul, pertes et inflation. Chrome : 42 rendus ordinateur/mobile, ancre de création conservant l’espace et aperçu interactif à 41 ans vérifiés. L’import historique Alpha Vantage dépend de la clé, du quota et de la couverture de l’abonnement ; il n’a pas été testé avec une clé réelle. La reconstruction Docker de cette mise à jour n’a pas pu être exécutée : moteur Docker Desktop local arrêté.
Courbes des marchés : 36 suites validées, dont réponses fournisseurs fictives pour devises, type ETF/action, regroupement UTC, cache et indisponibilité. Les trois modes de sélection sont vérifiés dans Chrome sur ordinateur et mobile. Aucun test avec clé fournisseur réelle ni garantie de couverture des symboles.
