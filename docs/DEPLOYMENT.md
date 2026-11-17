# Installation privée sur VPS Linux

Cette procédure cible votre VPS **Ubuntu 24.04 LTS**, avec Docker Engine, Compose v2, OpenSSL, Tailscale et, pour les sauvegardes, age. Installer ces outils depuis leurs sources officielles : [Docker pour Ubuntu](https://docs.docker.com/engine/install/ubuntu/) et [Tailscale pour Linux](https://tailscale.com/download/linux). Un VPS héberge l’application 24 h/24 tant que le système et ses services restent disponibles. Ce montage à une instance ne garantit pas la haute disponibilité. Exécuter les commandes dans votre propre session SSH ; ne pas transmettre le mot de passe SSH.

## 1. Réseau privé et HTTPS

Installer Tailscale sur le VPS et sur vos appareils. Ajouter uniquement vos deux identités au réseau privé et restreindre les règles d’accès au VPS / port 443 pour ces identités. Activer MagicDNS et HTTPS dans la console Tailscale. Le nom du certificat HTTPS apparaît dans les journaux publics Certificate Transparency : choisir un nom de machine sans information privée.

Le service Docker publie exclusivement **127.0.0.1:8080**. Ne pas ouvrir ce port dans le pare-feu, ne pas modifier l’adresse en 0.0.0.0, ne pas utiliser Tailscale Funnel et ne pas ouvrir de port sur une box. Le port de base de données n’existe pas sur le réseau : SQLite reste dans le volume local.

Sur le VPS :

```bash
sudo tailscale serve --bg http://127.0.0.1:8080
sudo tailscale serve status
```

Noter l’origine HTTPS affichée. Tailscale Serve termine TLS et transmet X-Forwarded-Proto. L’application accepte ce header uniquement depuis le proxy configuré (passerelle Docker 172.30.88.1, un seul saut). Le nom d’hôte reçu doit correspondre exactement à CASHHARBOR_ORIGIN. Une erreur 400 indique généralement une origine ou un proxy mal configururé : ne pas désactiver le contrôle HTTPS pour contourner l’erreur.

Documentation officielle : [Serve](https://tailscale.com/docs/features/tailscale-serve), [HTTPS](https://tailscale.com/docs/how-to/set-up-https-certificates).

## 2. Préparer le dépôt et les secrets

Copier le code sur le VPS, sans App_Data, .test-artifacts, .env, clés locales ni exports. Dans le dossier du projet :

```bash
cp .env.example .env
nano .env
sudo bash scripts/initialize-secrets.sh
docker compose config --quiet
docker compose build --pull
```

Dans .env, renseigner l’origine exacte, par exemple CASHHARBOR_ORIGIN=https://mon-vps.mon-reseau.ts.net. Ce fichier ne contient pas de mot de passe. Le certificat de chiffrement des clés et son mot de passe sont dans .secrets, ignoré par Git et exclu du contexte Docker. Ils ne passent jamais dans les arguments d’une commande ni dans l’image.

Le conteneur fonctionne avec l’UID 1654, sans privilèges, sans capabilities, avec système de fichiers en lecture seule et volumes dédiés aux données et aux clés. Le script attribue la lecture des deux secrets à cet UID. Ne pas régénérer les secrets après avoir saisi des données : conserver tous les anciens certificats nécessaires au déchiffrement en cas de rotation.

Le réseau Docker 172.30.88.0/24 doit être libre. En cas de conflit, choisir un sous-réseau libre et modifier ensemble ipam et Security__ProxyAddress. Ne jamais autoriser tous les proxies.

## 3. Créer vos deux accès

Avant de démarrer le service permanent, exécuter deux fois, chacun avec son propre e-mail, son mot de passe et son application d’authentification :

```bash
docker compose run --rm --no-deps app --setup
```

Le terminal doit être interactif. Les mots de passe sont masqués. Utiliser un mot de passe unique de 14 caractères minimum, conservé dans un gestionnaire. Enregistrer la clé privée TOTP affichée dans une application d’authentification, en mode SHA-1 / 6 chiffres / 30 secondes. Entrer son code pour terminer. La clé n’est affichée qu’au provisionnement ; effacer ensuite l’affichage du terminal. Garder une copie de récupération protégée, indépendante du téléphone.

Deux utilisateurs maximum. Aucune inscription ni récupération par e-mail n’est exposée. Vous partagez tous les comptes, opérations et objectifs. Attendre le prochain code après le provisionnement avant de vous connecter : un code consommé ne peut pas être réutilisé.

En cas de perte du téléphone ou du mot de passe, l’administrateur du VPS peut réinitialiser un accès **existant** :

```bash
docker compose stop app
docker compose run --rm --no-deps app --reset-user
docker compose start app
```

Ce parcours remplace mot de passe et clé TOTP, et révoque toutes les sessions de cet utilisateur. L’accès SSH au VPS est donc une autorité de récupération à protéger.

## 4. Démarrer et vérifier

```bash
docker compose up -d
docker compose ps
curl --fail http://127.0.0.1:8080/health
docker compose logs --tail=50 app
```

Depuis un appareil connecté au réseau Tailscale, ouvrir l’URL HTTPS. Vérifier la connexion des deux utilisateurs, la création d’un compte fictif, une dépense fictive et la déconnexion. Vérifier que l’URL est inaccessible depuis un appareil hors de votre réseau privé. Vérifier que les ports 8080 et les ports de base de données ne sont pas accessibles sur l’IP publique du VPS.

Activer la synchronisation de l’heure (NTP) sur le VPS et vos téléphones : TOTP dépend d’une heure correcte. Le fuseau du budget est Europe/Paris dans le conteneur. Les événements de sécurité s’affichent en UTC.

## 5. Sauvegarder et restaurer

Une sauvegarde exploitable contient ensemble **cashharbor_data**, **cashharbor_keys** et **.secrets**. Une base seule ne permet pas de récupérer les données chiffrées. Une sauvegarde volée avec ses clés doit elle-même rester chiffrée. Le JSON téléchargé par l’application n’est pas une sauvegarde du serveur.

Créer une paire de clés age **sur votre ordinateur de confiance**, conserver la clé privée hors du VPS, puis utiliser uniquement la clé publique sur le serveur :

```bash
sudo bash scripts/backup.sh age1VOTRE_CLE_PUBLIQUE
```

Le script arrête brièvement l’application pour obtenir une copie SQLite cohérente, produit une archive chiffrée sans fichier intermédiaire en clair et redémarre le service même en cas d’échec. Transférer ensuite le fichier .tar.age hors du VPS. Automatiser ce script quotidiennement avec votre outil de planification et surveiller ses échecs ; le dépôt ne configure pas de tâche distante à votre place.

Pour restaurer, utiliser un serveur de test isolé, le même code / schéma v1, sans données réelles déjà présentes :

1. Déchiffrer l’archive avec age sur une machine de confiance, dans un emplacement chiffré et protégé ; extraire les dossiers data, keys et secrets. Ne pas laisser d’archive en clair sur le VPS.
2. Arrêter l’application cible. Créer les volumes nommés par la configuration Compose, puis copier le contenu de data et keys dans leurs volumes respectifs et secrets dans .secrets. Préserver l’UID 1654 sur les volumes et les deux fichiers secrets ; garder les fichiers secrets en mode 400.
3. Reconfigurer l’origine HTTPS de la machine cible. Démarrer **une seule instance**. Tester les deux connexions et comparer les soldes ainsi que les opérations avec l’instance d’origine.
4. Ne supprimer aucune sauvegarde avant d’avoir prouvé la restauration. Conserver la clé privée age et une copie de récupération du certificat hors du VPS.

## 6. Mises à jour

Faire une sauvegarde vérifiée avant une mise à jour. Reconstruire régulièrement avec les correctifs .NET et les images à jour :

```bash
docker compose build --pull
docker compose up -d --no-deps app
```

Les tags d’image suivent la branche .NET 10 ; épingler les digests après validation si vous avez besoin de reproductibilité stricte. Appliquer aussi les mises à jour de l’hôte, Docker et Tailscale. Une future modification du schéma devra fournir une migration explicite : EnsureCreated initialise uniquement la base v1 et ne constitue pas un mécanisme de migration. Pas de replicas ni de partage du fichier SQLite entre plusieurs serveurs.

Le VPS doit avoir des sauvegardes hors site, un pare-feu, SSH par clé et des comptes d’administration maîtrisés. Chiffrer le disque / les sauvegardes protège les copies hors ligne ; un administrateur ou un processus ayant compromis le serveur peut lire les données quand l’application tourne.
