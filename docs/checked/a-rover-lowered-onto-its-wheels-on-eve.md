# A rover lowered onto its wheels on Eve

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): the first culprit, a damper
too strong for a light vessel, here a heavy rover on light wheel mounts. It needs the hinges and the KAL-1000
controller of Breaking Ground.

## The saves

[diag/SuspensionBounceHingeRoverEve.sfs](../../diag/SuspensionBounceHingeRoverEve.sfs): `SuspensionBounce Hinge
Rover`, a base of 5 t on six TR-2L wheels at the end of trusses, on Eve, held up on its hinges with its wheels off
the ground, its springs left to the game. A KAL-1000 controller holds the sequence that raised it; played backwards,
it lowers the rover onto its wheels.
[diag/SuspensionBounceHingeRoverEveManual.sfs](../../diag/SuspensionBounceHingeRoverEveManual.sfs) is the same, its
springs set by hand the way a player found to keep the rover on its wheels in stock: springs at their stiffest (3),
dampers at their softest (0.05). Copy the saves into the folder of a sandbox game, under `saves`.

## By hand

1. Load a save from the quickload menu (Alt+F9 by default).
2. Right-click the KAL-1000, set its play speed to 100 %, reverse its direction and play the sequence: the hinges
   lower the rover, and it settles on its wheels.

Without this mod and with the springs left to the game, the wheels kick the rover up as they touch, and it bounces,
sometimes long and high. With the springs set by hand, it settles. With this mod, it settles with the springs left
to the game.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save and plays the sequence backwards at full
speed through [KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), and records
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) for 45 s; its first lines say how to run it:

    python check.py hinge-rover-eve --folder <your sandbox game> --trials 3 --label stock
    python check.py hinge-rover-eve-manual --folder <your sandbox game> --trials 3 --label stock

Played three times each, KSP 1.12.5 and KSP Community Fixes. The rover starts with its wheels off the ground: the
time with no wheel on the ground counts the descent.

| | Without this mod, springs left to the game | Without this mod, springs set by hand | This mod, springs left to the game |
|---|---|---|---|
| Top speed over the ground | 3.5 to 12 m/s | 0.24 to 0.31 m/s | 0.23 to 0.28 m/s |
| Time with no wheel on the ground | 13 to 49 % | 6 % | 6 % |
| Speed over the last 15 s | up to 0.3 mm/s, 1.7 m/s and 12 m/s | up to 0.13 mm/s | up to 0.33 mm/s |
| Parts | 63, none lost | 63, none lost | 63, none lost |
