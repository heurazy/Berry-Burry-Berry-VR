BERRY VR - MODE D'EMPLOI (test final)
=====================================

AVANT DE LANCER
---------------
1. Demarrer SteamVR et connecter le casque (Virtual Desktop).
2. Attendre que le casque affiche le "void" SteamVR.
3. Lancer le jeu normalement (Steam ou l'exe).
   - Si OpenVR s'initialise, le rendu stereo VR demarre automatiquement.
   - Sinon le jeu reste en mode flat (aucun blocage).
4. Le log est ici : BepInEx\LogOutput.log
   Chercher "[VR] OpenVR initialized" pour confirmer le mode VR.

CONTROLES (Oculus Touch via Virtual Desktop / Index)
----------------------------------------------------
Manette droite
  Grip               : ramasser / poser / maintenir (vacuum)
  Gachette           : jeter / utiliser / tirer (popgun, vacuum) / cliquer l'UI
  A                  : sauter
  B                  : tap = lacher l'objet | maintien 0,65 s = Gaping Maw
  Stick droit clic   : Gaping Maw (hors mode construction)
                       = rotation 90 degres (en mode construction)
  Stick droit        : rotation (en jeu), fluide ou par crans selon la config

Manette gauche
  Grip               : sprint
  Gachette           : Air Blast
  X                  : tap = Shop | maintien 0,6 s = Fruits en folie (Berry Blitz)
  Y                  : tap = pause | maintien 0,35 s = Move Hole
  Menu (bouton system): tap = pause | maintien 0,8 s = recentrage
  Stick gauche       : locomotion
  Stick gauche clic  : Fruits en folie (Berry Blitz). Pour s'accroupir : se baisser

Clavier
  F1                 : recentrage (secours si le bouton menu est capture par SteamVR)

UI
--
- En jeu, vise l'ecran du PC (ou toute UI 3D interactive) avec la manette droite :
  le laser cyan apparait et la gachette clique. Le stick droit fait defiler les pages du PC.
- Dans les menus (pause, options, shop), un laser cyan suit la manette droite :
  gachette = cliquer, stick droit haut/bas = faire defiler les listes.
- Si les manettes ne sont pas suivies, le laser passe en visee du regard (tete).

EN CAS DE PROBLEME
------------------
- Rendu stereo absent : verifier "OpenVR initialized" dans le log.
- Position bizarre / tourne : F1 ou maintien du bouton menu pour recentrer.
- SteamVR capture le bouton menu : utiliser Y (tap) pour la pause et F1 au clavier.
- L'objet tenu tombe pendant les menus : normal, les entrees de jeu sont coupees en menu.
- Perf faible : le jeu rend 2 vues ; baisser la qualite dans les options du jeu.


REGLAGES (BepInEx\config\berryvr.plugin.cfg, cree au 1er lancement)
--------------------------------------------------------------------
[Comfort]  TurnMode = Smooth | Snap, SmoothTurnSpeed, SnapTurnAngle
           Vignette = false par defaut (true = bords assombris en deplacement)
           VignetteStrength (0-1)
           HeightOffset (metres, -0.6 a 0.8) : monter/descendre la vue (assis)
[Feel]     Haptics = true/false : vibrations (prise, lacher, objet vise, UI)
           FingerTouchUI = true/false : appuyer sur les ecrans avec l'index
[Visuals]  CartoonGloves = true/false, GloveScale (0.6-1.6)
Modifier le fichier jeu ferme, puis relancer.

VISUELS ET INTERACTIONS
-----------------------
- Gants cartoon suivis au doigt pres (squelette SteamVR). Si tu avais
  personnalise les controles de ce jeu dans SteamVR, remets les bindings
  par defaut pour activer les gants.
- Pas de laser permanent : l'objet vise recoit le contour de selection du jeu.
- Ecrans du jeu (PC, ...) : approche l'index a quelques cm pour viser,
  touche l'ecran pour cliquer. La gachette marche toujours a distance.
- Le laser des menus part du bout de l'index du gant.
- Lancer : la vitesse prise est le pic du geste (pas une moyenne lissee),
  les lancers rapides partent donc plus fort et plus droit.