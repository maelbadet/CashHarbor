# Rappels mensuels et SMTP Hostinger

Dans **Paramètres → Rappels par e-mail → Configurer le serveur SMTP commun**, renseigner :

| Champ | Hostinger Email |
| --- | --- |
| Serveur | `smtp.hostinger.com` |
| Port | `465` |
| Chiffrement | TLS dès la connexion |
| Identifiant | Adresse complète de la boîte mail |
| Adresse d’envoi | La même adresse, ou une adresse autorisée par cette boîte |
| Mot de passe | Mot de passe de la boîte mail, pas du compte Hostinger |

Ces valeurs proviennent de la [documentation Hostinger Email](https://www.hostinger.com/support/1575756-how-to-get-email-account-configuration-details-for-hostinger-email/). Si hPanel indique un autre service, par exemple Titan, reprendre les paramètres de cette boîte. Le port 587 est aussi disponible, avec **STARTTLS obligatoire** : [documentation Hostinger](https://www.hostinger.com/tutorials/how-to-send-emails-using-phpmailer/). Aucun mode SMTP en clair n’est proposé, et la validation du certificat serveur reste active.

Enregistrer puis utiliser **M’envoyer un e-mail de test**. Le destinataire est toujours l’adresse de connexion de l’utilisateur courant ; on ne peut pas saisir une adresse arbitraire. Le test est limité à un envoi toutes les cinq minutes par utilisateur. Il ne compte pas comme rappel mensuel. L’acceptation par le serveur SMTP ne garantit pas le classement en boîte de réception : vérifier aussi les indésirables et la configuration du domaine chez Hostinger.

Chacun se connecte ensuite avec son propre accès, active son rappel et choisit le jour et l’heure. Le fuseau est **Europe/Paris**, heure d’été comprise. Le jour 31 est ramené au dernier jour des mois plus courts. Le service vérifie au démarrage puis toutes les quinze minutes. Si l’application était arrêtée, il rattrape le mois courant après l’heure prévue ; il n’envoie pas les mois passés. Activer un rappel après sa date prévue peut donc déclencher le rappel du mois courant.

Le message demande simplement de mettre à jour les comptes. Aucun nom de compte, solde, opération ou pièce jointe bancaire n’est envoyé. Chaque destinataire reçoit son propre message, sans exposition de l’adresse du partenaire.

Après succès, le mois envoyé est conservé sur disque pour éviter un nouvel envoi au redémarrage. En cas d’échec SMTP, une tentative peut reprendre six heures plus tard. Une coupure brutale entre l’acceptation SMTP et l’enregistrement local peut exceptionnellement produire un doublon : SMTP et le stockage local ne partagent pas de transaction.

## Stockage et déploiement

La configuration, le mot de passe et l’état des rappels sont chiffrés avec ASP.NET Data Protection dans `/data/reminders.enc`, dans le volume Docker existant `cashharbor_data`. Le mot de passe n’est jamais renvoyé au navigateur. Laisser le champ vide conserve le secret existant ; changer le serveur ou l’identifiant exige de le ressaisir. Les deux utilisateurs peuvent gérer le SMTP commun ; les préférences et statuts de rappel restent propres à chacun.

Aucune nouvelle variable `.env`, ouverture entrante de port, tâche cron ni modification du schéma SQLite n’est nécessaire. Le VPS doit autoriser les connexions SMTP sortantes vers le serveur choisi. Le script de sauvegarde existant archive tout le volume `data`, donc aussi ce fichier chiffré ; les clés et leur certificat doivent rester disponibles pour le restaurer. Une seule instance de l’application doit fonctionner à la fois.

Après transfert des sources :

```sh
docker compose build
docker compose up -d
```

Les rappels sont désactivés tant que chacun ne les a pas activés. Aucun e-mail réel n’a été envoyé lors des tests de développement.
