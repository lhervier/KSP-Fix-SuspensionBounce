# Reprise : essai de la lecture des vitesses par les positions

Fichier temporaire de la branche `wheel-creep`, à supprimer avant de la fusionner. Pour reprendre, coller le prompt
ci-dessous dans une nouvelle conversation, lancée dans `kspmod\`.

---

On reprend le travail sur le correctif « WheelCreep » de KSP-Fix-SuspensionBounce, branche `wheel-creep`. Je suis sur
le PC portable, et je veux essayer en jeu la variante `TyreReadsPositions` seule, le patch `TyreStaticGrip` coupé.

**Contexte** (le détail est dans `claude-notes\correctifs-proposes\rover-qui-tourne-seul.md`, chapitre « La grue : ancre
rigide, vitesses fantômes », et son « Reprendre ici ») :
- Sur un vaisseau de plusieurs pièces dont les joints portent une charge, le `rb.velocity` d'une pièce contient une
  vitesse fantôme stable, qui ne déplace rien : PhysX corrige les joints par la position, puis retire cette correction
  de la vitesse qu'il rend. Sur la grue de la Mune, on mesure +1,3 et −2,9 mm/s sur deux roues qui avancent pourtant
  ensemble.
- VPP lit le glissement du pneu par `GetPointVelocity` : chaque pneu combat donc un glissement qui n'existe pas. Sur la
  grue, les pneus sont à 99 % de l'adhérence, et elle dérive.
- La variante 1, `PoseMotion.TyreReadsPositions` (`Src/PoseMotion.cs`), donne au pneu la vitesse tirée de la pose de
  la roue, entre ce pas et le précédent. Seule, patch coupé, elle fait mieux que le patch partout où elle a été
  mesurée : grue 0,02 mm/s, rover lourd de la Mune et rover d'Eve freins serrés tenus. Elle ne change rien à la
  descente freins lâchés, à la conduite ni à la rotation par la roue de réaction. Deux trajets lents (+6 s) ont été
  vus une fois et ne se sont pas reproduits.
- Si elle tient, elle remplace l'ancre de `TyreStaticGrip`, qui est bien plus compliquée.

**Préparer le portable** :
1. `python outils\sync.py` (il ne crée pas les branches de travail), puis `git checkout wheel-creep` dans
   `KSP-Fix-SuspensionBounce`. KSP-Util-MCPServer reste sur `main`, qui a `click_part_menu_field` (sert à
   `diag\automation\check.py`). Scripts, sauvegardes et logs de la journée : `claude-notes\archives\wheelcreep-2026-11-09\`.
2. Construire les deux, puis les déployer dans `KSPDIR` par `outils\gamedata.py` (KSP arrêté, `echo $KSPDIR` avant).
3. Dans le `settings.cfg` **déployé** (`GameData\KSPFixSuspensionBounce\PluginData\` de `KSPDIR`, pas celui du dépôt),
   mettre `staticGrip = false` et `tyreReadsPoses = true`.

**Mes essais en jeu**, que tu me prépares un par un (sauvegardes de `diag\`, partie `claude-checked`) :
- conduite lente et manœuvres : `SuspensionBounceRoverFlat`, le Bulldozer, le rover à charnières d'Eve ;
- rovers garés, freins serrés, en pente : rovers de la Mune et d'Eve, la grue (`SuspensionBounceCraneMun`) ;
- surveiller un trajet anormalement lent, et comparer au besoin avec `tyreReadsPoses = false`.

**Ensuite, si c'est concluant** :
1. Rejouer les cas *Checked* avec la variante seule (script
   `claude-notes\archives\suspension-bounce-2026-11-08-reference\checked.py`, qui s'appuie sur
   `diag\automation\check.py`).
2. Retirer l'ancre (`TyreStaticGrip`, `GroundedAnchor`, `AnchorReadsPositions`) et la trace marquée `TEMPORARY`.
3. Réécrire le README (Limits, WIP, pages *Checked*), puis supprimer ce fichier `REPRISE.md`.

Comme d'habitude, je commite moi-même dans les dépôts de mods.
