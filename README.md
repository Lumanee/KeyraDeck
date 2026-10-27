# PhoneDeck

Transforme ton téléphone Android en Stream Deck / Touch Portal pour ton PC Windows :
des gros boutons pour lancer tes applis, des raccourcis clavier, et un mélangeur de volume par appli.

- **PhoneDeck PC** (`pc/`) : petite appli Windows (C# .NET 8) qui reçoit les appuis et agit sur le PC.
- **PhoneDeck** (`android/`) : l'appli Android (Kotlin, Jetpack Compose) avec les boutons.

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

## Installer et essayer

### 1. Télécharger

Va dans **Releases → « Dernière version (prototype) »** sur la page GitHub du projet, et télécharge :

- `PhoneDeckPC-Windows.zip` pour le PC
- `PhoneDeck.apk` pour le téléphone

Ces fichiers sont fabriqués automatiquement par GitHub à chaque modification du code (onglet **Actions**).

### 2. Sur le PC

1. Dézippe `PhoneDeckPC-Windows.zip` où tu veux (par exemple `Documents\PhoneDeck`).
2. Lance `PhoneDeckPC.exe`.
   - Windows peut afficher « Windows a protégé votre ordinateur » (l'appli n'est pas signée) : clique sur **Informations complémentaires → Exécuter quand même**.
   - Le pare-feu Windows demande l'autorisation : coche **Réseaux privés** et valide. Sans ça, le téléphone ne pourra pas se connecter.
3. Une fenêtre affiche un **QR code**, l'adresse IP et un **code** à 6 caractères.

Fermer la fenêtre la range près de l'horloge (l'appli reste active). Pour quitter : clic droit sur l'icône près de l'horloge → **Quitter**.

### 3. Sur le téléphone

1. Copie `PhoneDeck.apk` sur le téléphone (ou télécharge-le directement depuis le téléphone) et ouvre-le.
2. Android demande d'autoriser l'installation d'applis inconnues pour ton navigateur / gestionnaire de fichiers : accepte.
3. Ouvre **PhoneDeck**, touche **Scanner le QR code** et vise l'écran du PC.

Le téléphone et le PC doivent être sur le **même réseau Wi-Fi / box**.
Si le scan ne marche pas, saisis l'adresse IP, le port (8765) et le code affichés sur le PC.

### 4. Régler Discord (micro / sourdine)

Discord ne réagit aux raccourcis que s'ils sont déclarés comme raccourcis **globaux** :

1. Discord → Paramètres utilisateur → **Raccourcis clavier** → **Ajouter un raccourci**.
2. Action **« Activer/désactiver le micro »** → raccourci **Ctrl + Maj + F9**.
3. Action **« Activer/désactiver le son »** (sourdine) → raccourci **Ctrl + Maj + F10**.

Le bouton bascule l'état mais n'affiche pas encore si tu es muet ou non (ça demandera l'API de Discord, prévu plus tard).

## Le configurateur (fenêtre du PC)

Inspiré de Logitech G HUB : le panneau **Affectations** à gauche (onglets Commandes, Widgets, Applis, avec recherche) liste tout ce qu'on peut ajouter ; on **glisse** une commande sur une case du boîtier (ou on clique dessus). Au centre, le **contour réel de ton téléphone** (taille, coins arrondis, caméra) tel que détecté à la connexion ; quand tu survoles ou sélectionnes une touche, une étiquette reliée par un trait indique ce qu'elle fait. Option **« Masquer le nom des boutons »** (dans Réglages, ou par bouton) pour afficher l'icône en plein.


La fenêtre de PhoneDeck PC est un **configurateur** : elle affiche une copie du Stream Deck de ton téléphone, et tout ce que tu changes apparaît tout de suite sur le téléphone.

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

La configuration est enregistrée dans `%APPDATA%\PhoneDeckPC\config.json` (bouton « Dossier de config »). Tu peux aussi l'éditer à la main : la fenêtre et le téléphone se mettent à jour dès que tu enregistres.

## Widgets

Comme sur Nova Launcher, un widget se pose sur plusieurs cases d'une page. Dans le configurateur, section **Widgets** à gauche :

Dans le configurateur, les widgets s'affichent **en direct** comme sur le téléphone (heure, charge CPU/RAM, volume, météo, touches média).

Chaque widget existe en plusieurs **modèles à taille fixe** (ex. Horloge 1×1, 2×1, 2×2 ; CPU/RAM Mini, Compact, Barre, Anneaux) : le contenu est dessiné pour cette taille et ne disparaît jamais. Clique sur un modèle pour l'ajouter ; pour en changer, sélectionne le widget et choisis un autre modèle dans le panneau de droite.

- **Pavé média** : 1×1 (lecture/pause), 3×1 (précédent, lecture, suivant), 2×2, 3×2 (avec volume − / muet / +).
- **Volume général** : barres 3×1 ou 4×1, ou verticales 1×2 et 1×3.
- **Horloge** : 1×1 (heure), 2×1 (heure et date), 2×2 (grande horloge).
- **CPU / RAM** : charge du processeur et de la mémoire, en 1×1, 2×1, 3×1 ou 2×2 (anneaux).

- **Météo** : température et conditions de la ville choisie (1×1, 2×1, 2×2). Sélectionne le widget, tape ta ville dans le panneau de droite. Le PC doit avoir internet (service gratuit Open-Meteo, sans compte).

Les widgets s'affichent comme de petits **écrans tactiles encastrés** dans le boîtier (verre sombre, sans relief de touche), pour se distinguer clairement de la grille de boutons.

## Volume, retour visuel, notifications

- **Mélangeur** : le bouton Volume ouvre une fenêtre avec une tranche verticale par appli, comme une table de mixage (volume général en premier). Glisse vers le haut / bas, touche 🔈 pour couper. Pour fermer : croix ✕ ou touche à côté de la fenêtre.
- **Changer de page** : glisse le doigt, ou touche les flèches ‹ › en bas du boîtier (elles apparaissent dès qu'il y a plusieurs pages). Les marges du boîtier sont proportionnelles à la taille de l'écran.
- **Retour visuel** : chaque touche s'illumine à la couleur d'accent quand tu appuies (en plus de la vibration).
- **Notifications** : un message envoyé au PC s'affiche en bandeau sur le téléphone pendant quelques secondes, puis une pastille 🔔 avec le nombre apparaît dans la barre du boîtier (touche-la pour lire la liste). Pour l'instant la source est l'API locale `POST http://localhost:8765/api/notify` (`app`, `title`, `text`) ; la lecture automatique des notifications Windows n'est pas encore branchée.

## Souris et clavier

Le bouton **🖱 Souris** de la barre du boîtier (visible sur toutes les pages) ouvre un **trackpad** : glisse un doigt pour déplacer le curseur du PC, touche pour cliquer, glisse à deux doigts pour défiler, touche à deux doigts pour le clic droit. Les boutons **Clic gauche / Clic droit** en bas restent enfoncés tant que tu appuies (glisser-déposer possible). Le bouton **Clavier** ouvre le clavier du téléphone : ce que tu tapes arrive dans la fenêtre active du PC, avec une rangée de touches spéciales (Échap, Tab, flèches, Suppr…) et des modificateurs Ctrl / Alt / Maj / Win à toucher avant une lettre (ex. Ctrl puis C).

## Écran du téléphone

À la connexion, le téléphone indique la taille de son écran. Le boîtier remplit alors tout l'écran du téléphone, et le configurateur affiche un boîtier **aux mêmes proportions**. À la première connexion, la grille est adaptée automatiquement (touches de bonne taille, écran bien rempli) ; si tu changes de téléphone, un bouton **« Adapter la grille à ce téléphone »** apparaît sous le boîtier. Quand tu changes la taille de la grille, les boutons et widgets restent à leur place s'ils y tiennent, sinon ils sont replacés.

## Réglages et thèmes

Le bouton **Réglages** du configurateur propose 5 thèmes : **Noir OLED**, **Blanc**, **Rouge**, **Bleu** et **Bordeaux**. Ils changent la couleur du boîtier, des touches, de l'accent et des widgets (verre teinté aux couleurs du thème). Il permet aussi de masquer le nom des boutons et de lancer PhoneDeck PC au démarrage de Windows. Sur le téléphone, le menu ⋮ → **Réglages** règle la vibration, l'écran allumé et l'orientation.

**Pages automatiques** : clique dans une zone vide du configurateur, puis indique dans « Activer cette page… » les applis (ex. `reaper, discord`). Le téléphone change de page quand l'une de ces applis est au premier plan.

## Mises à jour automatiques

À chaque amélioration, GitHub fabrique une nouvelle version. Une fois la version « 0.3 » ou plus récente installée :

- **PC** : un bouton **« Mettre à jour »** apparaît en haut de la fenêtre. Un clic télécharge la nouvelle version, ferme PhoneDeck PC et le relance.
- **Téléphone** : un bandeau **« Mise à jour disponible »** apparaît en bas. Appuie sur **Installer**. Android demande une seule fois l'autorisation d'installer des applis depuis PhoneDeck, puis propose d'installer la mise à jour.

Tes boutons et réglages sont conservés.

## Connexion par câble USB (facultatif)

Avec les outils Android (`adb`) installés et le débogage USB activé sur le téléphone :

```
adb reverse tcp:8765 tcp:8765
```

Puis dans l'appli, choisis **Connexion par câble USB**, saisis le code et connecte-toi.

## Pour les développeurs

- PC : `dotnet run --project pc/PhoneDeckPC` (Windows, .NET 8). `--console` lance le serveur sans fenêtre, `--data <dossier>` change le dossier de config (par défaut `%APPDATA%\PhoneDeckPC`).
- Android : ouvrir `android/` dans Android Studio, ou `./gradlew assembleDebug`.
- Protocole : messages JSON sur `ws://IP:8765/ws?token=CODE`. Téléphone → PC : `getLayout`, `press`, `getMixer`, `setVolume`, `setMute`, `setIcon`, `resetIcon`, `ping`. PC → téléphone : `layout`, `mixer`, `toast`, `pong`.
