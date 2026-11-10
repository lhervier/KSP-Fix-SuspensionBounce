# Reprise : rejouer les cas *Checked* avec la lecture des vitesses par les poses

Fichier temporaire de la branche `wheel-creep`, à supprimer avant de la fusionner. Pour reprendre, coller le prompt
ci-dessous dans une nouvelle conversation, lancée dans `kspmod\`.

---

On reprend le travail sur le correctif « WheelCreep » de KSP-Fix-SuspensionBounce, branche `wheel-creep`, sur le PC
portable. J'ai poussé KSP-Diag-Suspension depuis le fixe à midi.

**Où on en est** (détail : `claude-notes\correctifs-proposes\rover-qui-tourne-seul.md`, « Reprendre ici ») :
- Le 2026-11-10 au matin, j'ai essayé en jeu la variante seule (`tyreReadsPoses = true`, ancre coupée) : conduite du
  rover à plat, du Bulldozer et du rover à charnières d'Eve très agréable ; rovers garés freins serrés tenus (rover
  lourd Mune 0,001 mm/s, Eve 0,006 mm/s, grue 0,01 mm/s après une première minute de mise en place).
- J'ai cru la variante suffisante, et l'ancre a été retirée (`8056d58`). Mais elle ne corrige pas la rotation freins
  lâchés de `sans-lame-eve-cap71` : +8,6° à 25 s, comme le stock (l'ancre : +0,57°) ; le rover pivote sur lui-même
  en s'arrêtant en haut de la pente. Deux coupables, donc : les vitesses fantômes (`PoseMotion`) et le pneu de VPP
  sans adhérence statique (l'ancre). Fait le 2026-11-10 au matin : l'ancre est revenue, **par roue seulement**, sur
  le glissement que `PoseMotion` lit dans les poses (l'ancre rigide, `GroundedAnchor` et la trace `TEMPORARY` sont
  retirés) ; son seuil de vitesse, qui la désarmait sur une roue libre qui roule, est corrigé. Grue, rovers garés et
  rover d'Eve freins lâchés tenus ; je l'ai conduit, il se pilote bien. Pas encore commité. Détail : la note,
  « Reprendre ici ». Partie `claude-checked` recréée sur le portable, Parking Brake 0.4.4 dans la réserve.
- Limite ajoutée au README : le rover lourd d'Eve bouge d'environ 20 cm et 1° quand Parking Brake le lâche, puis tient ;
  un autre bug, à corriger plus tard.

**À faire** :
1. Récupérer KSP-Diag-Suspension (`python outils\sync.py`, ou le cloner s'il n'y est pas), le construire, l'importer
   dans la réserve de `ksp-dev\` (`outils\gamedata.py refresh`).
2. Rejouer les cas *Checked* avec le build actuel (script
   `claude-notes\archives\suspension-bounce-2026-11-08-reference\checked.py`, qui s'appuie sur
   `diag\automation\check.py`). Attention : son entrée `KSPFixSuspensionBounce-7b77570` (avant) et la DLL de
   KSP-Mod-Bulldozer (il faut Parallax dans `KSPDIR` pour le compiler) manquent dans la réserve du portable.
3. Réécrire le README (deux coupables de plus : le pneu qui lit son glissement dans la vitesse du Rigidbody, et le pneu
   sans adhérence statique ; Limits, WIP,
   pages *Checked*), puis supprimer ce fichier `REPRISE.md`.

Comme d'habitude, je commite moi-même dans les dépôts de mods.
