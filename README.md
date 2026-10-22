# Berry Bury Berry VR

Mod VR pour **Berry Bury Berry** (Steam) : jeu complet en réalité virtuelle via SteamVR, avec gants cartoon suivis au doigt près, rotation au stick, menus au laser ou au doigt et lancer d'objets naturel.

*A SteamVR mod for Berry Bury Berry: full stereo VR, finger-tracked cartoon gloves, smooth/snap turning, laser and finger-touch menus, physical throwing.*

## Installation

1. Télécharge **`BerryVR-Setup.exe`** dans la [dernière release](https://github.com/heurazy/Berry-Burry-Berry-VR/releases/latest).
2. Ferme le jeu, lance `BerryVR-Setup.exe` et choisis **[1] Installer**. Le jeu est trouvé automatiquement via Steam (sinon, colle le chemin du dossier du jeu).
3. Lance **SteamVR**, mets ton casque, puis lance Berry Bury Berry depuis Steam.

Sans SteamVR, le jeu démarre normalement sur l'écran. Pour désinstaller, relance l'installateur et choisis **[2]**.

Testé avec Meta Quest via Virtual Desktop. Les bindings Index (Knuckles) et Vive sont fournis.

## Contrôles

| Main droite | |
|---|---|
| Grip | Ramasser / poser / aspirateur |
| Gâchette | Lancer / utiliser / tirer / cliquer l'UI |
| A | Sauter |
| B | Tap : lâcher, maintien : Gaping Maw |
| Stick | Tourner la vue (fluide ou par crans) |
| Clic stick | Gaping Maw (rotation en mode construction) |

| Main gauche | |
|---|---|
| Stick | Se déplacer |
| Clic stick | Fruits en folie (Berry Blitz) |
| Grip | Sprint |
| Gâchette | Air Blast |
| X | Tap : boutique, maintien : Fruits en folie |
| Y | Tap : pause, maintien : déplacer le trou |
| Menu | Tap : pause, maintien : recentrer |

Les écrans du jeu (PC, …) se touchent aussi directement avec l'index.

## Réglages

`BepInEx\config\berryvr.plugin.cfg` (créé au premier lancement, à modifier jeu fermé) :

- **Comfort** : `TurnMode` (Smooth/Snap), vitesse et angle de rotation, `Vignette` (désactivée par défaut), `HeightOffset` (joueur assis)
- **Feel** : `Haptics`, `FingerTouchUI`
- **Visuals** : `CartoonGloves`, `GloveScale`, position du pistolet (`GunOffsetRight/Up/Forward`, en cm)

## Compiler

Prérequis : .NET SDK, Visual Studio Build Tools (C++), le jeu installé.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Produit `dist\BerryVR-Setup.exe`. Le dossier du jeu est détecté via Steam, ou passe `-GameDir <dossier>`.

## Structure

- `BerryVR/` : le plugin BepInEx (C#), les actions et bindings SteamVR, et `native/` le pont de rendu OpenVR (C++).
- `installer/` : l'installateur en un seul fichier (compilé avec le compilateur C# de Windows).
- `third_party/` : BepInEx 5.4.23.2 (MIT) et `openvr_api.dll` (BSD-3), avec leurs licences.

Aucun fichier du jeu n'est inclus : ses assemblies servent uniquement à la compilation.

## Licence

Apache 2.0, voir [LICENSE](LICENSE). Berry Bury Berry appartient à ses auteurs ; ce mod n'est pas officiel.
