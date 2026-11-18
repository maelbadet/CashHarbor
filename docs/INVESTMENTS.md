# Portefeuille et comparaison

La page Placements et simulation conserve des lots d’achat (quantité, coût total frais inclus, date). Les positions personnelles sont privées ; les positions Couple sont communes. Les fichiers `portfolio-*.enc` du volume `/data` sont chiffrés avec les mêmes clés de protection que le reste de l’application et inclus dans les sauvegardes du volume. Ils ne sont pas ajoutés aux soldes bancaires.

Pour Ethereum, choisir Crypto et l’identifiant `ethereum`, puis une quantité telle que `0.5`. La comparaison à deux mois, deux ans ou une date choisie évalue cette même quantité aux deux dates. Ce calcul n’est pas une performance intégrant achats successifs, ventes, staking, dividendes et fiscalité.

CoinGecko fournit les prix en EUR, avec un cache de trente minutes. L’offre gratuite limite l’historique à 365 jours : une valeur de deux ans peut rester indisponible. Une panne ou un quota ne produit jamais un faux cours. Voir [les limites du fournisseur](https://support.coingecko.com/articles/5723073761-what-granularity-do-you-support-for-historical-data).

Pour actions et ETF, configurer `ALPHAVANTAGE_API_KEY` dans `.env`. Le symbole doit correspondre à la cotation exacte du titre : le nom « Amundi 500 » ne suffit pas à identifier une part, sa devise et sa place. Le connecteur fournit les clôtures mensuelles **non ajustées**, datées, dans la devise de cotation. Elles ne sont pas présentées comme un prix EUR ou un cours en direct. Aucun taux de change n’est deviné. Voir [Alpha Vantage](https://www.alphavantage.co/documentation/).

`COINGECKO_API_KEY` est facultative et correspond au plan Demo. Ne jamais saisir une clé de compte de courtage ou d’exchange : aucune connexion à vos comptes de trading n’est nécessaire. Les fournisseurs ne reçoivent que les identifiants de marché et les dates, pas les quantités, prix d’achat ou identité.

La simulation accepte votre capital et votre valeur finale **en euros**, sur les mêmes dates, sans versements intermédiaires. Elle affiche gains et écarts avec deux scénarios de taux constants modifiables. Les valeurs proposées 1,7 % et 2,5 % ne reconstituent pas les taux historiques : ce sont des hypothèses de simulation. Le calcul réglementaire par quinzaines, les variations de taux, l’éligibilité LEP et la fiscalité du placement ne sont pas modélisés. Il ne s’agit donc pas d’un décompte bancaire de Livret A ou LEP.

Les prix unitaires manuels permettent de préremplir la valeur finale lorsque les cours EUR ne sont pas fournis. L’intégration n’effectue aucun ordre, vente ou mouvement bancaire.
