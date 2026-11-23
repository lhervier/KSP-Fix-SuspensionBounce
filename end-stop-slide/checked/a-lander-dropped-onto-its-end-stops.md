# A lander dropped onto its end-stops

Part of [Suspension Bounce Fix](../../README.md), [End-Stop Slide](../README.md#checking-the-culprit):
[a bottomed wheel hides its weight from the tyres](../a-bottomed-wheel-hides-its-weight-from-the-tyres.md), on landing legs rather than
on wheels.

The LT-1 landing leg is the one stock wheel or leg whose collider sits off its part's centre (0.119 m lower). The game
leaves that offset out when it works out how far a suspension is compressed: an LT-1 resting on its end-stop reads
0.873 of its travel, never 1. This mod adds the offset back, so that it sees an LT-1 on its end-stop, and so does
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension): it reads an LT-1 on its end-stop at 1.0,
and counts it as bottomed out. It also records the game's vertical speed, as the gauge above the navball shows it, at
every physics step: the fall figures below come from it.

## The craft

[saves/SuspensionBounceLegKerbinSuspended.craft](../saves/SuspensionBounceLegKerbinSuspended.craft): a Mk1 command
pod, an OKTO2 probe core, four Z-100 batteries, an FL-T100 tank, empty, and a telescopic ladder, on four LT-1 legs,
1.2 t in all, hanging from three launch clamps, on soft springs, so that the lander ends up resting on its end-stops.
Copy it into `Ships/SPH` of a sandbox game.

The craft holds the soft springs twice. Without this mod, the game uses its own sliders, set by hand at their lowest
(0.05). This mod hides those sliders and uses its own setting, saved with the craft: the one its suspension window
makes in the SPH (open it from the toolbar, tick all four legs, keep Kerbin as the body, type a mass of 0.01 t, share
the weight by the centre of mass, and press *Auto*).

## By hand

Launch the craft on the runway: nothing to set, with this mod or without it. Wait for it to hang still, and stage:
the clamps let it go, and it falls 4.6 m onto its legs. Then watch it, and the navball's heading.

Without this mod, the legs reach their end-stops, and the lander rests on them; but its feet read none of its weight:
it slowly turns on itself, and slides. With this mod, it rests on its end-stops and stays still.

## By script

[diag/automation/check.py](../../diag/automation/check.py) launches the craft through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), the springs as saved, lets it hang for 2 s,
and stages, KSP Diag - Suspension recording from just before: the fall, the touch and the 3 s after. It then records
KSP Diag - Suspension for 8 s more, the lander at rest. Its first lines say how to run it, and how to choose a
configuration of this mod (its `settings.cfg`, KSP started again):

    python check.py lander-drop --folder <your sandbox game> --trials 3 --label stock
    python check.py lander-drop --folder <your sandbox game> --trials 3 --label modded

KSP-Util-MCPServer refuses to launch a craft holding a part module the game does not have: without this mod, put in
the sandbox game, under the same name, a copy of the craft without this mod's four `ModuleSuspensionTuning` modules,
the rest unchanged.

Played three times in each configuration, KSP 1.12.5, KSP Community Fixes,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut) and
[Physics Easing Fix](https://github.com/lhervier/KSP-Fix-PhysicsEasing), without
[Parking Brake](https://github.com/jarosm/KSP-ParkingBrake), which this mod is not compatible with, the springs as
saved: without this mod; with this mod and its default settings; with Fixed Tuning alone (`damperBounce = false` and
`endStopSlide = false` in the mod's `settings.cfg`); and with Fixed Tuning and this fix (`damperBounce = false`).
The table gives the range of the three trials. The fall is read from the first recording, from the first physics step
a leg touches the ground on; the rest from the second, the lander at rest.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **The fall**: speed at the touch | 9.3 m/s | 9.3 m/s | 9.3 m/s | 9.3 m/s |
| Speed it bounces back up at | 2.8 to 6.2 m/s | 5.6 and 5.8 m/s; 0.34 m/s | 0.12 to 0.34 m/s | 0.12 to 0.33 m/s |
| Time with a leg on its end-stop over the first 3 s | 73 to 85 % | 35 and 41 %; 87 % | 91 to 97 % | 94 to 97 % |
| Time with no leg on the ground over the first 3 s | 0 | 27 and 28 %; 0 | 0 | 0 |
| **At rest**: share of the weight the tyres read (median) | 0 % | 73 to 80 % | 93 % | 93 % |
| Turn over 8 s | −2.1° to 0.4° | 0.0° to −0.08° | 0.0° | 0.0° |
| Speed over the last third | 4.2 to 4.7 mm/s | 0.5 to 1.0 mm/s | 0.25 to 0.35 mm/s | 0.24 to 0.35 mm/s |
| Parts at the end, `KSP.log` | 12 of 12, nothing | 12 of 12, nothing | 12 of 12, nothing | 12 of 12, nothing |

With Fixed Tuning alone, the legs reach their end-stops within a step and the game's end-stop throws the lander back
up: it left the ground in two trials out of three. This fix catches the legs on their end-stops: Damper Bounce makes
no difference here.
