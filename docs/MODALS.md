# Formulaires en fenêtres modales

Les formulaires de création de compte, prêt, opération, objectif, récurrence et achat de placement sont présentés dans des fenêtres modales natives. Le plan budgétaire mensuel utilise la même présentation. Les boutons restent proches du haut de la page ; un bouton d’ouverture redondant n’est pas affiché lorsqu’un bouton de création existe déjà dans l’en-tête.

Les fiches de compte conservent nom, établissement, solde et plafond éventuel. Historique, relevé de solde, modification, partage et suppression s’ouvrent séparément. Les confirmations de partage et suppression restent dans leur formulaire respectif.

Les fenêtres sont nommées pour les lecteurs d’écran. Le focus reste dans la modal ouverte ; Échap, la croix et le clic sur le fond permettent de fermer. À la fermeture, le focus revient à l’élément d’ouverture. Sur mobile, le défilement s’effectue dans la fenêtre et l’arrière-plan reste bloqué.

Les formulaires, leurs actions POST et leurs jetons antifalsification restent rendus sur le serveur. Le module local `modals.js` améliore leur présentation ; sans JavaScript, les formulaires restent accessibles dans les pages. Les liens d’ajout indiquent explicitement la page et l’espace avant l’ancre, pour éviter qu’une ancre seule soit résolue depuis la racine Blazor.

Vérifications : ouvertures de création, détails de compte, focus, largeur mobile et fermeture par Échap contrôlés dans Chrome à 390 et 1 440 px. Le bouton de création de compte de l’en-tête est vérifié pour ouvrir la fenêtre sur place. Les parcours HTTPS continuent de contrôler les écritures et les droits d’accès.
