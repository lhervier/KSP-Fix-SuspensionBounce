# A rover driving in a straight line

Part of [Suspension Bounce Fix](../../README.md), [End-Stop Slide](../README.md#checking-the-culprit):
a rover set off from standstill on flat ground, throttle held, its steering let go. Not a culprit: a check that
the mod does not make a rover turn on its own.

A car whose wheel is let go does not keep a perfect line either: the road, the tyres and the start each turn it a
little, and the game's rovers do the same. This mod must not change how often, or how far, a rover turns that way.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) records the rover's yaw rate at every
physics step: summed over the run, it gives how far the rover turned.

## The save

Copy them into the folder of a sandbox game, under `saves`, and load them from the quickload menu (Alt+F9 by default).

- [saves/SuspensionBounceRoverFlat.sfs](../saves/SuspensionBounceRoverFlat.sfs): `SuspensionBounce Rover`, 2.4 t on
  four TR-2L wheels, on flat ground near the space centre, brakes on, its suspensions never set: without this mod,
  the game sets them; with it, the mod sets them as the save loads, from the rover's mass shared equally among its
  wheels ([Suspensions never set](../../fixed-tuning/README.md#suspensions-never-set)). For the trials with this mod,
  which is not compatible with Parking Brake: saved without it, at rest.
- [saves/SuspensionBounceRoverFlatStock.sfs](../saves/SuspensionBounceRoverFlatStock.sfs): the same, saved with
  [Parking Brake](https://github.com/jarosm/KSP-ParkingBrake) on, for the trials without this mod: when Parking Brake
  holds the rover as it loads, every run starts from the same place and the same heading. Without Parking Brake, the
  game puts this rover 6 m lower as it loads.

## By hand

Load the save of the configuration played, wait until the rover is still, then, if Parking Brake is installed and still
holds it, let the parking brake go from the window of its cabin (*Disengage parking brake*), brakes still on. Turn SAS
off, release the brakes, and hold the wheel throttle at 30 % without touching the steering, for 30 s: the rover reaches
about 26 m/s. Watch the heading on the navball.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save (the Stock one when Parking Brake is
installed) through [KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), lets the parking brake go once
the rover is still, if Parking Brake is installed and still holds it, and records KSP Diag - Suspension for 30 s from
the moment it holds the throttle. Its first lines say how to run it:

    python check.py straight-line --folder <your sandbox game> --trials 5 --label stock

It prints, for each trial, how far the rover turned (`turned_deg`, its yaw rate summed over the recording) and its
fastest turn (`max_yaw_rate_deg_s`).

Played five times without this mod: KSP 1.12.5 with KSP Community Fixes 1.41.1,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut) and Parking Brake; every run was still under the
parking brake before it was let go, and reached 25.8 m/s. With this mod, KSP 1.12.5, KSP Community Fixes, Terrain
Precision Fix and KSP-Fix-AutoStrut, without Parking Brake, three times in each configuration: with its default
settings; with Fixed Tuning alone, `damperBounce = false` and `endStopSlide = false` in the mod's `settings.cfg`; and
with Fixed Tuning and this fix, `damperBounce = false`. The rover settled on its own as it loaded; every run reached
25.9 m/s.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| How far the rover turned in 30 s, each trial | −0.53°, −0.32°, −0.48°, −0.53°, −0.35° | 0.12°, 0.10°, 0.04° | 0.12°, 0.09°, 0.08° | 0.11°, 0.07°, 0.09° |
| Fastest turn, each trial | 0.03 to 0.05°/s | 0.016 to 0.023°/s | 0.016 to 0.020°/s | 0.016 to 0.026°/s |
| Time with no wheel on the ground | — | 0 | 0 | 0 |

Set off from standstill, the rover keeps its line within half a degree without this mod, and within an eighth of a
degree with it, in each of its configurations.
