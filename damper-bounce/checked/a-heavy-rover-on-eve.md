# A heavy rover on Eve

Part of [Suspension Bounce Fix](../../README.md), [Damper Bounce](../README.md#checking-the-culprit):
[a damper too strong for what it moves in a step](../a-damper-too-strong-for-what-it-moves.md), here a heavy
rover whose wheels hang from light trusses.

## The saves

Two saves of the same rover, at rest on Eve: a Mk2 lander can, three ore tanks, six TR-2L wheels each at the end of a
truss, 26 parts. Its suspensions were never set: without this mod, the game sets them; with it, the mod sets them as
the save loads, from the rover's mass shared equally among its wheels
([Suspensions never set](../../fixed-tuning/README.md#suspensions-never-set)). Copy the saves into the folder of a
sandbox game, under `saves`.

- [saves/SuspensionBounceHeavyRoverEve.sfs](../saves/SuspensionBounceHeavyRoverEve.sfs): `SuspensionBounce Heavy
  Rover`, 14.05 t, stock parts only: 4.9 t of parts, and 9 t of ore in three Small Holding Tanks. Its brakes are on,
  at 100 %: it stands on a slope of 5.5°, and at 50 % they let it roll down once it moves.
- [saves/SuspensionBounceBulldozerEve.sfs](../saves/SuspensionBounceBulldozerEve.sfs): `SuspensionBounce
  Bulldozer`, the same rover with a blade of 3.5 t in front in place of one of its ore tanks, 13.96 t in all. The
  blade is a part of [KSP-Mod-Bulldozer](https://github.com/lhervier/KSP-Mod-Bulldozer), which must be installed.

## By hand

Load a save from the quickload menu (Alt+F9 by default), and watch the rover.

<table>
<tr>
<td width="50%"><img src="../imgs/bulldozer-stock-eve.gif" alt="The bulldozer loaded on Eve, without this mod"></td>
<td width="50%"><img src="../imgs/bulldozer-modded-eve.gif" alt="The bulldozer loaded on Eve, with this mod"></td>
</tr>
<tr>
<td valign="top"><em>KSP 1.12.5, KSP Community Fixes and KSP-Mod-Bulldozer: the bulldozer.</em></td>
<td valign="top"><em>KSP 1.12.5, KSP Community Fixes, KSP-Mod-Bulldozer and this mod: the bulldozer.</em></td>
</tr>
</table>

Without this mod, the rover hops on its wheels as soon as physics starts, and its wheels break as they come down.
With this mod, it stays on its wheels.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads a save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer) and records
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) for 30 s; its first lines say how to run it:

    python check.py heavy-rover-eve --folder <your sandbox game> --ksp <your KSP folder> --label stock
    python check.py bulldozer-eve --folder <your sandbox game> --ksp <your KSP folder> --trials 3 --label stock

Without this mod, each save was played three times: the bulldozer with KSP 1.12.5, KSP Community Fixes and
KSP-Mod-Bulldozer; the heavy rover with KSP 1.12.5, KSP Community Fixes,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut) and
[Parking Brake](https://github.com/jarosm/KSP-ParkingBrake), which its save was made with. Its wheels break as it
loads, before the script, which waits for Parking Brake to let it go, starts recording: the table gives what
`KSP.log` and the end of the trial show. With this mod, KSP 1.12.5, KSP Community Fixes, Terrain Precision Fix and
KSP-Fix-AutoStrut, without Parking Brake, which this mod is not compatible with (for the bulldozer, the blade's part
from KSP-Mod-Bulldozer, its plugin left out: it clears ground scatters, nothing this case uses), each save was played
three times with its default settings, and three times with Fixed Tuning alone, `damperBounce = false` and
`endStopSlide = false` in the mod's `settings.cfg`; the rover settles on its own as it loads.

| | Without this mod | Fixed Tuning alone | This mod |
|---|---|---|---|
| **Heavy rover**: top speed over the ground | — | 1.4 to 2.0 m/s | 0.27 m/s |
| Time with no wheel on the ground | — | 5 to 10 % | 0 |
| How far it turned in 30 s | — | −10°, 39°, 173° | −0.07° to 0.12° |
| Speed over the last 10 s | — | up to 9.6 mm/s, 0.1 mm/s and 1.0 m/s | up to 1.8 mm/s |
| At the end | on its wheels once, on its back twice; 2 parts lost once | on its wheels, 26 parts | on its wheels, 26 parts |
| `KSP.log` | `Hit Eve … at 61.2 to 265.9 m.s and damaged wheel.`, 2 trials out of 3 | `Hit Eve … at 52.9 to 94.0 m.s and damaged wheel.`, 3 trials out of 3 | nothing |
| **Bulldozer**: top speed over the ground | 1.7 to 2.4 m/s | 1.5 to 2.0 m/s | 0.30 m/s |
| Time with no wheel on the ground | 8 to 14 % | 6 to 17 % | 0 |
| How far it turned in 30 s | — | −28°, 12°, −8.2° | 0.6° to 0.9° |
| Speed over the last 10 s | — | up to 1.3 m/s, 17 mm/s and 1.6 m/s | up to 0.45 mm/s |
| `KSP.log` | `Hit Eve … at 48.0 to 123.3 m.s and damaged wheel`, at 2 trials out of 3 | `Hit Eve … at 72.3 to 103.4 m.s and damaged wheel.`, 2 trials out of 3 | nothing |
