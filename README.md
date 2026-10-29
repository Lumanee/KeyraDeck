# KeyraDeck

Transforme ton téléphone Android en Stream Deck / Touch Portal pour ton PC Windows :
des gros boutons pour lancer tes applis, des raccourcis clavier, et un mélangeur de volume par appli.

- **KeyraDeck PC** (`pc/`) : petite appli Windows (C# .NET 8) qui reçoit les appuis et agit sur le PC.
- **KeyraDeck** (`android/`) : l'appli Android (Kotlin, Jetpack Compose) avec les boutons.

Les deux communiquent en Wi-Fi local (WebSocket). L'appairage se fait en scannant un QR code.

## Ce que contient le prototype

Page d'accueil :

| Bouton | Ce qu'il fait |
|---|---|
| YouTube | Ouvre YouTube dans Chrome |
| Claude | Ouvre l'appli Claude si elle est installée, sinon claude.ai |
| Chrome | Ouvre Chrome |
| Discord | Dossier : Ouvrir Discord, Micro (muet), Sourdine |
| Volume | Un curseur par appli qui joue du son (+ volume général), avec bouton muet |
| Reaper | Ouvre Reaper |
| Launchers | Dossier : Xbox, Steam, Battle.net, Epic Games |

- Les icônes sont **extraites automatiquement** des applis installées.
- **Appui long** sur un bouton : choisir une image de la galerie du téléphone (elle est envoyée et gardée sur le PC), ou revenir à l'icône automatique.
- Le dossier **Launchers** reprend automatiquement le contenu du dossier « Launcher » de ton menu Démarrer s'il existe. Ajoute un raccourci dans ce dossier, il apparaît sur le téléphone.
- Les dossiers s'ouvrent par-dessus la page, comme sur Android. Ils se referment après avoir lancé une appli, mais restent ouverts pour Micro / Sourdine.

> Anciennement « PhoneDeck » : les fichiers s'appellent maintenant `KeyraDeck.exe` / `KeyraDeck.apk` et les réglages sont dans `%APPDATA%\KeyraDeck` (recopiés automatiquement depuis l'ancien dossier). Les anciens noms (`PhoneDeckPC-Windows.zip`, `PhoneDeck.apk`) restent publiés quelque temps pour que les anciennes installations se mettent à jour toutes seules.

## Installer et essayer

### 1. Télécharger

Va dans **Releases → « KeyraDeck : dernière version »** sur la page GitHub du projet (`github.com/Lumanee/KeyraDeck`), et télécharge :

- `KeyraDeck-Windows.zip` pour le PC
- `KeyraDeck.apk` pour le téléphone

Ces fichiers sont fabriqués automatiquement par GitHub à chaque modification du code (onglet **Actions**).

### 2. Sur le PC

1. Dézippe `KeyraDeck-Windows.zip` où tu veux (par exemple `Documents\KeyraDeck`).
2. Lance `KeyraDeck.exe`.
   - Windows peut afficher « Windows a protégé votre ordinateur » (l'appli n'est pas signée) : clique sur **Informations complémentaires → Exécuter quand même**.
   - Le pare-feu Windows demande l'autorisation : coche **Réseaux privés** et valide. Sans ça, le téléphone ne pourra pas se connecter.
3. Une fenêtre affiche un **QR code**, l'adresse IP et un **code** à 6 caractères.

Fermer la fenêtre la range près de l'horloge (l'appli reste active). Pour quitter : clic droit sur l'icône près de l'horloge → **Quitter**.

### 3. Sur le téléphone

1. Copie `KeyraDeck.apk` sur le téléphone (ou télécharge-le directement depuis le téléphone) et ouvre-le.
2. Android demande d'autoriser l'installation d'applis inconnues pour ton navigateur / gestionnaire de fichiers : accepte.
3. Ouvre **KeyraDeck**, touche **Scanner le QR code** et vise l'écran du PC.

Le téléphone et le PC doivent être sur le **même réseau Wi-Fi / box**.
Si le scan ne marche pas, saisis l'adresse IP, le port (8765) et le code affichés sur le PC.

### 4. Relier OBS, Discord et la maison (facultatif)

Bouton **Connexions** en haut du configurateur. Chaque logiciel a ses étapes écrites dans la fenêtre ; une fois relié, les boutons montrent leur **vrai état**.

- **OBS Studio** (version 28 ou plus) : dans OBS, *Outils > Paramètres du serveur WebSocket*, coche « Activer », puis *Afficher les informations de connexion* et copie le mot de passe dans KeyraDeck. Boutons : Live, Enregistrer, Pause, Replay, Caméra virtuelle, Scène (au choix dans la liste de tes scènes), Couper une source (micro, son du bureau…). Le préréglage de page « Streaming (OBS) » reprend tes vraies scènes.
- **Discord** : Discord n'accepte que les « applications » déclarées. Sur [discord.com/developers/applications](https://discord.com/developers/applications) : *New Application*, puis dans *OAuth2* copie le **Client ID** et le **Client Secret**, et ajoute la redirection `http://localhost`. Colle les deux dans KeyraDeck, clique sur **Connecter Discord** et accepte dans Discord (une seule fois). Boutons : Micro, Sourdine, Quitter le vocal. Widget **Salon vocal Discord** : qui est là, qui parle (contour vert), avec ton micro et ta sourdine touchables.
- **Maison (Home Assistant)** : adresse de Home Assistant (ex. `http://homeassistant.local:8123`) et un **jeton d'accès longue durée** (profil > Sécurité). Bouton **Appareil Home Assistant** : choisis une lampe, une prise, une scène… Le bouton s'allume quand l'appareil est allumé.

Les mots de passe et jetons sont rangés à part dans `integrations.json` (dossier de config) : ils ne partent jamais avec une page exportée.

Sans Discord relié, les raccourcis globaux restent possibles : Discord → Paramètres → **Raccourcis clavier**, « Activer/désactiver le micro » = **Ctrl + Maj + F9**, « Activer/désactiver le son » = **Ctrl + Maj + F10** (le bouton ne connaît alors pas l'état réel).

## Le configurateur (fenêtre du PC)

Inspiré de Logitech G HUB : le panneau **Affectations** à gauche (onglets Commandes, Widgets, Applis, avec recherche) liste tout ce qu'on peut ajouter ; on **glisse** une commande sur une case du boîtier (ou on clique dessus). Au centre, le **contour réel de ton téléphone** (taille, coins arrondis, caméra) tel que détecté à la connexion ; quand tu survoles ou sélectionnes une touche, une étiquette reliée par un trait indique ce qu'elle fait. Option **« Masquer le nom des boutons »** (dans Réglages, ou par bouton, indépendamment du réglage général) pour afficher l'icône en plein.


La fenêtre de KeyraDeck PC est un **configurateur** : elle affiche une copie du Stream Deck de ton téléphone, et tout ce que tu changes apparaît tout de suite sur le téléphone.

- **Déplacer** un bouton : glisse-le sur une autre case (si la case est prise, les deux boutons s'échangent).
- **Ajouter une appli** : glisse-la depuis la liste « Applis installées » sur une case, ou clique dessus pour la mettre dans la première case libre.
- **Changer l'icône** : dépose une image (depuis l'Explorateur) sur le bouton, ou clique sur « Choisir une image… ». « Icône auto » remet l'icône de l'appli.
- **Grille** : les boutons **+ / −** sous le Stream Deck règlent le nombre de colonnes et de lignes (5×3 par défaut). Chaque dossier a sa propre grille.
- **Dossiers** : onglets en haut ; double-clic sur un bouton dossier pour l'ouvrir.
- **Plusieurs pages** : le bouton **＋ Page** ajoute une page d'accueil. Sur le téléphone, on passe de l'une à l'autre en glissant le doigt (les petits points en haut du boîtier montrent où tu en es).
- **Boutons qui changent d'état** : « Micro Windows » et « Couper le son » deviennent rouges quand c'est coupé (état réel de Windows). Un raccourci peut aussi être « à bascule » (rouge à chaque appui impair), pratique pour Discord.
- **Macros** : bouton « Raccourci / macro », puis « Enregistrer un raccourci » et appuie sur tes touches (Ctrl+Maj+M…). Tu peux enchaîner plusieurs étapes et des pauses. « Texte à taper » écrit un texte dans la fenêtre active.
- **Tester** : le bouton « ▶ Tester sur le PC » exécute l'action sans passer par le téléphone.
- **Annuler** : Ctrl+Z, ou le bouton « Annuler ». **Suppr** efface le bouton sélectionné.
- **Connecter un téléphone** : le bouton en haut affiche le QR code.

Le dossier **Launchers** reprend le contenu du dossier « Launcher » de ton menu Démarrer. Pour le modifier à la main, clique sur « Détacher du menu Démarrer ».

La configuration est enregistrée dans `%APPDATA%\KeyraDeck\config.json` (bouton « Dossier de config »). Tu peux aussi l'éditer à la main : la fenêtre et le téléphone se mettent à jour dès que tu enregistres.

## Widgets

Comme sur Nova Launcher, un widget se pose sur plusieurs cases d'une page. Dans le configurateur, section **Widgets** à gauche :

Dans le configurateur, les widgets s'affichent **en direct** comme sur le téléphone (heure, charge CPU/RAM, volume, météo, touches média).

Chaque widget existe en plusieurs **modèles à taille fixe** (ex. Horloge 1×1, 2×1, 2×2) : le contenu est dessiné pour cette taille et ne disparaît jamais. **Survole un modèle** dans la galerie (ou dans le panneau de droite) pour voir un aperçu en vraie taille avec les valeurs en direct. Clique sur un modèle pour l'ajouter ; pour en changer, sélectionne le widget et choisis un autre modèle dans le panneau de droite.

- **Pavé média** : 1×1 (lecture/pause), 3×1 (précédent, lecture, suivant), 2×2, 3×2 (avec volume − / muet / +), et **avec titre** 3×1 ou 4×1 : le titre du morceau ou de la vidéo en cours au-dessus, avec sa petite pochette, et précédent / lecture-pause / suivant dessous (agit sur le lecteur affiché, comme « Morceau en cours »).
- **Morceau en cours** : pochette, titre et artiste de ce qui joue sur le PC, avec précédent / lecture / suivant (2×1, 4×1, 2×2 pochette en fond, 4×2 avec barre de progression). Utilise les contrôles média de Windows (le panneau qui apparaît quand on change le volume) : marche avec Spotify, YouTube dans le navigateur, le lecteur Windows, Deezer… sans compte ni clé. Windows 10 (version 1809) ou plus récent.
- **Volume général** : barres 3×1 ou 4×1, ou verticales 1×2 et 1×3.
- **Horloge** : 1×1 (heure), 2×1 (heure et date), 2×2 (grande horloge).
- **Performances PC** : compte-tours (nom en haut à gauche, pourcentage au centre, zone rouge au-delà de 85 %) du processeur en 1×1, + mémoire en 2×1, + carte graphique en 3×1, + disque en 2×2.
- **Appli active** : CPU, RAM, GPU et FPS de l'appli au premier plan sur le PC (ton jeu), en 4×1 ou 2×2. Les FPS se mesurent sur les jeux DirectX (9 à 12) et seulement si KeyraDeck est lancé **en administrateur** (clic droit > Exécuter en tant qu'administrateur) ; sinon le compte-tours FPS affiche « — ».

- **Salon vocal Discord** : 2×1, 2×2, 3×2 (3, 4 ou 6 personnes). Discord doit être relié dans « Connexions ».
- **Météo** : température et conditions de la ville choisie (1×1, 2×1, 2×2). Sélectionne le widget, tape ta ville dans le panneau de droite. Le PC doit avoir internet (service gratuit Open-Meteo, sans compte).

Les widgets s'affichent comme de petits **écrans tactiles encastrés** dans le boîtier (verre sombre, sans relief de touche), pour se distinguer clairement de la grille de boutons.

## Volume, retour visuel, notifications

- **Mélangeur** : le bouton Volume ouvre une fenêtre avec une tranche verticale par appli, comme une table de mixage (volume général en premier). Glisse vers le haut / bas, touche 🔈 pour couper. Pour fermer : croix ✕ ou touche à côté de la fenêtre.
- **Changer de page** : glisse le doigt, ou touche les flèches ‹ › en bas du boîtier (elles apparaissent dès qu'il y a plusieurs pages). Les marges du boîtier sont proportionnelles à la taille de l'écran.
- **Retour visuel** : chaque touche s'illumine à la couleur d'accent quand tu appuies (en plus de la vibration).
- **Notifications** : un message envoyé au PC s'affiche en bandeau sur le téléphone pendant quelques secondes, puis une pastille 🔔 avec le nombre apparaît dans la barre du boîtier (touche-la pour lire la liste). Pour l'instant la source est l'API locale `POST http://localhost:8765/api/notify` (`app`, `title`, `text`) ; la lecture automatique des notifications Windows n'est pas encore branchée.

## Souris et clavier

Le bouton **🖱 Souris** de la barre du boîtier (visible sur toutes les pages) ouvre un **trackpad** : glisse un doigt pour déplacer le curseur du PC, touche pour cliquer, glisse à deux doigts pour défiler, touche à deux doigts pour le clic droit. Les boutons **Clic gauche / Clic droit** en bas restent enfoncés tant que tu appuies (glisser-déposer possible). Le bouton **⌨ Clavier** ouvre un **vrai clavier d'ordinateur AZERTY** en plein écran (le téléphone passe en paysage) : Échap, F1 à F12, Impr, Suppr, chiffres, lettres, ponctuation, Verr. Maj, Maj, Ctrl, Win, Alt, Alt Gr, flèches, et Inser / Début / Fin / Pg ↑ / Pg ↓ en haut. Chaque touche est envoyée au PC comme une vraie touche (elle reste enfoncée tant que le doigt est dessus, et se répète si tu la gardes), donc ça marche aussi dans les jeux. Pour les raccourcis : garde Ctrl d'un doigt et touche C de l'autre, ou touche Ctrl une fois (il s'allume) puis C. Retouche une touche allumée pour l'éteindre. Le bouton **🖱 Souris** revient au trackpad.

Le bouton **Texte** ouvre le clavier du téléphone (pratique pour la dictée vocale ou les emojis) : ce que tu tapes arrive dans la fenêtre active du PC, avec une rangée de touches spéciales et des modificateurs Ctrl / Alt / Maj / Win à toucher avant une lettre.

## Écran du téléphone

À la connexion, le téléphone indique la taille de son écran. Le boîtier remplit alors tout l'écran du téléphone, et le configurateur affiche un boîtier **aux mêmes proportions**. À la première connexion, la grille est adaptée automatiquement (touches de bonne taille, écran bien rempli) ; si tu changes de téléphone, un bouton **« Adapter la grille à ce téléphone »** apparaît sous le boîtier. Quand tu changes la taille de la grille, les boutons et widgets restent à leur place s'ils y tiennent, sinon ils sont replacés.

## Réglages et thèmes

Le bouton **Réglages** du configurateur propose 5 thèmes : **Noir OLED**, **Blanc**, **Rouge**, **Bleu** et **Bordeaux**. Ils changent la couleur du boîtier, des touches, de l'accent et des widgets (verre teinté aux couleurs du thème). Il permet aussi de masquer le nom des boutons et de lancer KeyraDeck PC au démarrage de Windows. Sur le téléphone, le menu ⋮ → **Réglages** règle la vibration, l'écran allumé et l'orientation.

**Pages automatiques** : clique dans une zone vide du configurateur, puis indique dans « Activer cette page… » les applis (ex. `reaper, discord`). Le téléphone change de page quand l'une de ces applis est au premier plan.

## Mises à jour automatiques

À chaque amélioration, GitHub fabrique une nouvelle version. Une fois la version « 0.3 » ou plus récente installée :

- **PC** : un bouton **« Mettre à jour »** apparaît en haut de la fenêtre. Un clic télécharge la nouvelle version, ferme KeyraDeck PC et le relance.
- **Téléphone** : un bandeau **« Mise à jour disponible »** apparaît en bas. Appuie sur **Installer**. Android demande une seule fois l'autorisation d'installer des applis depuis KeyraDeck, puis propose d'installer la mise à jour.

Tes boutons et réglages sont conservés.

## Connexion par câble USB (secours, idéal en jeu)

Quand le Wi-Fi est capricieux, branche simplement le téléphone au PC : KeyraDeck passe tout seul par le câble, et revient sur le Wi-Fi quand tu le débranches. Le petit repère **USB** à côté de la LED du boîtier indique qu'il passe par le câble.

À faire une seule fois :

1. Sur le téléphone : **Paramètres › À propos du téléphone**, touche **7 fois « Numéro de build »** (Samsung : Informations sur le logiciel › Numéro de version ; Xiaomi : Version de MIUI / HyperOS).
2. **Paramètres › Système › Options pour les développeurs** : active **« Débogage USB »**.
3. Branche le téléphone au PC avec un câble de données, puis touche **« Autoriser »** (coche « Toujours autoriser depuis cet ordinateur »).

Le PC s'occupe du reste : il télécharge une fois l'outil Android officiel de Google (« adb », ~7 Mo, rangé dans `%APPDATA%\KeyraDeck\platform-tools`), détecte le téléphone, ouvre le tunnel et ouvre KeyraDeck sur le téléphone. Le guide et l'état du câble sont dans **Connecter un téléphone** sur le PC (et dans **Réglages › Connexion par câble USB** sur le téléphone). Si KeyraDeck n'est pas installé sur le téléphone, un bouton permet de l'installer par le câble. Tout se désactive dans **Réglages › Câble USB** sur le PC.

Rien ne se passe ? Essaie un autre câble ou une autre prise, choisis « Transfert de fichiers » dans la notification USB du téléphone, ou installe le [pilote USB du fabricant](https://developer.android.com/studio/run/oem-usb?hl=fr).

Technique : `adb reverse tcp:8765 tcp:8766`. Le port 8766 du PC n'écoute que sur le PC lui-même et n'accepte que la connexion du deck (avec le code), jamais le configurateur.

## Pour les développeurs

- PC : `dotnet run --project pc/PhoneDeckPC` (Windows, .NET 8). `--console` lance le serveur sans fenêtre, `--data <dossier>` change le dossier de config (par défaut `%APPDATA%\KeyraDeck`).
- Android : ouvrir `android/` dans Android Studio, ou `./gradlew assembleDebug`.
- Protocole : messages JSON sur `ws://IP:8765/ws?token=CODE`. Téléphone → PC : `getLayout`, `press`, `getMixer`, `setVolume`, `setMute`, `setIcon`, `resetIcon`, `ping`. PC → téléphone : `layout`, `mixer`, `toast`, `pong`.
