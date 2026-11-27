# A rover driving in a straight line

Part of [Suspension Bounce Fix](../../../README.md), [End-Stop Slide](../README.md#checked):
a rover set off from standstill on flat ground, throttle held, its steering let go. Not a culprit: a check that
the mod does not make a rover turn on its own.

A car whose wheel is let go does not keep a perfect line either: the road, the tyres and the start each turn it a
little, and the game's rovers do the same. This mod must not change how often, or how far, a rover turns that way.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) records the rover's yaw rate at every
physics step: summed over the run, it gives how far the rover turned.

## The save

Copy it into the folder of a sandbox game, under `saves`, and load it from the quickload menu (Alt+F9 by default).

[saves/SuspensionBounceRoverFlat.sfs](../saves/SuspensionBounceRoverFlat.sfs): `SuspensionBounce Rover`, 2.4 t on four
TR-2L wheels, on flat ground near the space centre, brakes on, its suspensions never set: without this mod, the game
sets them; with it, the mod sets them as the save loads, from the rover's mass shared equally among its wheels
([Suspensions never set](../../fixed-tuning/README.md#suspensions-never-set)). Saved at rest, without Parking Brake.

## By hand

Load the save, and wait until the rover is still. Turn SAS off, release the brakes, and hold the wheel throttle at
30 % without touching the steering, for 30 s: the rover reaches about 26 m/s. Watch the heading on the navball.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), and records KSP Diag - Suspension for 30 s from
the moment it holds the throttle. Its first lines say how to run it:

    python check.py straight-line --folder <your sandbox game> --trials 5 --label stock

It prints, for each trial, how far the rover turned (`turned_deg`, its yaw rate summed over the recording) and its
fastest turn (`max_yaw_rate_deg_s`).

Played without Parking Brake, which this mod is not compatible with, KSP 1.12.5, KSP Community Fixes 1.41.1,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut) and
[Physics Easing Fix](https://github.com/lhervier/KSP-Fix-PhysicsEasing): five times without this mod; and three
times in each configuration of this mod: with its default settings; with Fixed Tuning alone, `damperBounce = false` and
`endStopSlide = false` in the mod's `settings.cfg`; and with Fixed Tuning and this fix, `damperBounce = false`.
Physics Easing Fix held the rover still as it loaded, and let it go at rest; every run reached 25.8 to 25.9 m/s.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| How far the rover turned in 30 s, each trial | 0.12°, 0.12°, 0.09°, 0.09°, 0.11° | 0.11°, 0.06°, 0.11° | 0.12°, 0.05°, 0.06° | 0.12°, 0.06°, 0.14° |
| Fastest turn, each trial | 0.016 to 0.030°/s | 0.015 to 0.018°/s | 0.017 to 0.022°/s | 0.017 to 0.026°/s |
| Time with no wheel on the ground | 0 | 0 | 0 | 0 |

Set off from standstill, the rover keeps its line within an eighth of a degree, with this mod or without it, in each
of its configurations: the mod changes nothing here.
