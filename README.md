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

## Modifier les boutons

Dans la fenêtre PhoneDeck PC, clique sur **Ouvrir le dossier de configuration** et ouvre `config.json` avec le Bloc-notes.
Dès que tu enregistres, le téléphone se met à jour tout seul.

Exemple de bouton :

```json
{
  "id": "spotify",
  "label": "Spotify",
  "icon": "auto",
  "color": "#1DB954",
  "action": { "type": "open", "target": "Spotify" }
}
```

- `id` : un nom unique, sans espace.
- `target` : le nom de l'appli tel qu'il apparaît dans le menu Démarrer. Le bouton **Voir la liste des applis détectées** te donne tous les noms possibles. On peut aussi mettre un chemin complet (`C:\\...\\appli.exe`) ou une adresse web.
- `args` : (facultatif) ce qu'on donne à l'appli, par exemple une adresse web pour Chrome.
- `color` : (facultatif) la couleur de fond de la tuile.

Types d'action :

| `type` | Exemple | Effet |
|---|---|---|
| `open` | `"target": "Steam"` | Lance une appli, un fichier ou un site |
| `url` | `"target": "https://twitch.tv"` | Ouvre un site dans le navigateur par défaut |
| `keys` | `"keys": "Ctrl+Shift+M"` | Raccourci clavier. Petite macro : `"Ctrl+C, wait:200, Ctrl+V"`. Touches média : `PlayPause`, `Next`, `Prev`, `VolumeUp`… |
| `folder` | `"page": "jeux"` | Ouvre une autre page comme un dossier |
| `mixer` | | Ouvre le mélangeur de volume |

Une page peut avoir `"startMenuFolder": "NomDuDossier"` : ses boutons sont alors créés à partir des raccourcis de ce dossier du menu Démarrer.

Si `config.json` contient une erreur, l'ancienne configuration reste active et l'erreur s'affiche dans le journal de la fenêtre PC.

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
