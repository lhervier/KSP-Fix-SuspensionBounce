# A lander dropped onto its end-stops

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): the third culprit, a bottomed
wheel hides its weight from the tyres
([the chapter](../the-culprits/a-bottomed-wheel-hides-its-weight-from-the-tyres.md)), on landing legs rather than on
wheels.

The LT-1 landing leg is the one stock wheel or leg whose collider sits off its part's centre (0.119 m lower). The game
leaves that offset out when it works out how far a suspension is compressed: an LT-1 resting on its end-stop reads
0.873 of its travel, never 1. This mod adds the offset back, so that it sees an LT-1 on its end-stop.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) reads the game's figure: it shows an LT-1
short of its end-stop, and never counts it as bottomed out; the share of the weight the tyres read, which it also
shows, is right.

## The craft

[diag/SuspensionBounceLegKerbinSuspended.craft](../../diag/SuspensionBounceLegKerbinSuspended.craft): a Mk1 command
pod, an OKTO2 probe core, four Z-100 batteries, an FL-T100 tank, empty, and a telescopic ladder, on four LT-1 legs,
1.2 t in all, hanging
from three launch clamps; the legs' springs are set by hand at their lowest (0.05), so that the lander ends up resting
on its end-stops. Copy it into `Ships/SPH` of a sandbox game.

## By hand

Launch the craft on the runway, wait for it to hang still, and stage: the clamps let it go, and it falls 4.6 m onto
its legs, at 9.3 m/s. Then watch it, and the navball's heading.

Without this mod, the legs reach their end-stops, and the lander rests on them; but its feet read none of its weight:
it slowly turns on itself, a degree or more in a few seconds, and slides. With this mod, it rests on its end-stops and
stays still.

## By script

[diag/automation/check.py](../../diag/automation/check.py) launches the craft through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), lets it hang for 2 s, stages, and reads the legs
at every physics step for 3 s from the moment it is let go (straight from the game, the offset added back), then
records KSP Diag - Suspension for 8 s, the lander at rest. Its first lines say how to run it:

    python check.py lander-drop --folder <your sandbox game> --trials 3 --label stock

Played three times in each configuration, KSP 1.12.5, KSP Community Fixes, Terrain Precision Fix and AutoStrut Fix;
the three trials
of a configuration agree, the table gives their range.

| | Without this mod | This mod |
|---|---|---|
| **The fall**: speed at the touch | 9.3 m/s | 9.3 m/s |
| Speed when the legs reach their end-stops | 7.3 m/s | 8.3 m/s |
| Speed it bounces back up at | 3.0 to 3.1 m/s | 4.3 to 4.4 m/s |
| Time a leg spends on its end-stop over the first 3 s | 78 % | 69 % |
| **At rest**: share of the weight the tyres read (median) | 0 % | 99 % |
| Turn over 8 s | -2.6° to 1.3° | 0.0° |
| Speed over the last third | 4.2 to 4.3 mm/s | 0.3 to 0.4 mm/s |
| Parts at the end, `KSP.log` | 12 of 12, nothing | 12 of 12, nothing |

The legs bounce the lander back up faster with this mod: their dampers, which this mod lowers for the mass they carry,
slow the fall less before the end-stops.
