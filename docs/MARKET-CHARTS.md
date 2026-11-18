# Courbes des marchés

La page **Courbes des marchés** propose trois onglets exclusifs : ETF, Actions et Cryptos. Choisir jusqu’à trois identifiants d’un même type, une période passée de dix ans maximum, puis afficher les courbes. Les suggestions proviennent des positions de ce type dans l’espace Personnel ou Couple sélectionné.

Pour les cryptos, utiliser l’identifiant CoinGecko, par exemple `ethereum` ou `bitcoin`. Les cours sont en euros, avec un dernier point par jour UTC. Le plan gratuit peut refuser les périodes dépassant un an. Pour les actions et ETF, utiliser le symbole exact Alpha Vantage et configurer `ALPHAVANTAGE_API_KEY` dans `.env`. La recherche du fournisseur vérifie le type et la devise du titre avant de charger les clôtures mensuelles.

En mode **prix**, les titres doivent avoir la même devise. Aucune conversion de change implicite n’est faite. En mode **base 100**, chaque série démarre à 100 à la première date commune : les variations sont calculées dans la devise de chaque titre, sans correction des changes. Les clôtures d’actions et ETF sont non ajustées : dividendes et divisions de titres peuvent affecter la comparaison. Ce graphique n’est pas un calcul de rendement total.

Seules les dates réellement présentes pour toutes les séries retenues sont affichées. Aucun remplissage ni interpolation d’un cours absent n’est effectué ; les segments relient les observations. Un titre indisponible est signalé et exclu du graphique, sans empêcher les autres d’apparaître. Une devise incompatible en mode prix ou l’absence de dates communes empêche la comparaison et affiche une explication.

Les courbes permettent le survol, le toucher, la navigation au clavier, le masquage de séries, l’affichage des données exactes et l’export SVG. Les historiques sont mis en cache une heure ; les erreurs cinq minutes. Ces cours ne sont pas présentés comme des cotations en direct.

Sources techniques : [CoinGecko, historique par période](https://www.coingecko.com/learn/how-to-fetch-historical-crypto-data-with-python), [Alpha Vantage, historique mensuel et recherche de symboles](https://www.alphavantage.co/documentation/).
