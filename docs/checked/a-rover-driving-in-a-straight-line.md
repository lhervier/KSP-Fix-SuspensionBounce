# A rover driving in a straight line

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): a rover set off from standstill
on flat ground, throttle held, its steering let go. Not a culprit: a check that the mod does not make a rover turn on
its own.

A car whose wheel is let go does not keep a perfect line either: the road, the tyres and the start each turn it a
little, and the game's rovers do the same. This mod must not change how often, or how far, a rover turns that way.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) records the rover's yaw rate at every
physics step: summed over the run, it gives how far the rover turned.

## The save

Copy it into the folder of a sandbox game, under `saves`, and load it from the quickload menu (Alt+F9 by default).

- [diag/SuspensionBounceRoverFlat.sfs](../../diag/SuspensionBounceRoverFlat.sfs): `SuspensionBounce Rover`, 2.4 t on
  four TR-2L wheels, on flat ground near the space centre, brakes on, its springs left to the game. It was saved with
  [Parking Brake](https://github.com/jarosm/KSP-ParkingBrake) on: with Parking Brake installed, the rover is held
  still as it loads, so that every run starts from the same place and the same heading.

## By hand

Load the save, wait until the rover is still, then let the parking brake go from the window of its cabin (*Disengage
parking brake*), brakes still on. Turn SAS off, release the brakes, and hold the wheel throttle at 30 % without
touching the steering, for 30 s: the rover reaches about 26 m/s. Watch the heading on the navball.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), lets the parking brake go once the rover is
still, and records KSP Diag - Suspension for 30 s from the moment it holds the throttle. Its first lines say how to run
it:

    python check.py straight-line --folder <your sandbox game> --trials 5 --label stock

It prints, for each trial, how far the rover turned (`turned_deg`, its yaw rate summed over the recording) and its
fastest turn (`max_yaw_rate_deg_s`).

Played five times in each configuration: KSP 1.12.5 with KSP Community Fixes 1.41.1,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut) and Parking Brake, without this mod and with it.
Every run was still under the parking brake before it was let go, and reached 25.8 m/s.

| | Without this mod | This mod |
|---|---|---|
| How far the rover turned in 30 s, each trial | −0.53°, −0.32°, −0.48°, −0.53°, −0.35° | −0.63°, −0.09°, −0.54°, −0.28°, −0.62° |
| Fastest turn, each trial | 0.03 to 0.05°/s | 0.03 to 0.05°/s |

Set off from the same place, the rover keeps its line within a degree, with or without this mod.
