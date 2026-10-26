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

- **Pavé média** : précédent, lecture/pause, suivant. Avec 2 lignes de haut, on ajoute volume −, muet et volume +.
- **Volume général** : un curseur pour le volume Windows (vertical si le widget est plus haut que large).
- **Horloge** : l'heure et la date.
- **CPU / RAM** : la charge du processeur et de la mémoire du PC.

Glisse un widget pour le déplacer, règle sa taille avec les boutons **+ / −** dans le panneau de droite.

## Réglages et thèmes

Le bouton **⚙ Réglages** du configurateur propose des thèmes de couleurs (le boîtier du téléphone change de couleur), des couleurs libres, et le lancement de PhoneDeck PC au démarrage de Windows. Sur le téléphone, le menu ⋮ → **Réglages** règle la vibration, l'écran allumé et l'orientation.

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
