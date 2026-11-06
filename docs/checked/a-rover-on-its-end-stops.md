# A rover on its end-stops

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): the third culprit, a bottomed
wheel hides its weight from the tyres
([the chapter](../the-culprits/a-bottomed-wheel-hides-its-weight-from-the-tyres.md)).

Each case is played three times: without this mod, with this mod and `bumpStopLoad = false` in its settings (the
part of the mod that answers this culprit is off), and with this mod as installed.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) shows, among others, the share of the
vessel's weight its suspensions carry, as the tyres read it: this mod adds what the end-stops carry to it.

## The saves

Copy them into the folder of a sandbox game, under `saves`, and load them from the quickload menu (Alt+F9 by
default).

- [diag/SuspensionBounceEndStopsFlat.sfs](../../diag/SuspensionBounceEndStopsFlat.sfs): `SuspensionBounce Rover`,
  2.4 t on four TR-2L wheels, on flat ground near the space centre, its springs set by hand at their lowest (0.05):
  all four wheels rest on their end-stops.
- [diag/SuspensionBounceEndStopsSlope.sfs](../../diag/SuspensionBounceEndStopsSlope.sfs): the same rover, brakes on,
  on a slope of about 20° west of the space centre, its springs set by hand at 0.3.

## By hand

- **On flat ground**: drive the rover at full speed, about 24 m/s, in a straight line. Without this mod, it slides
  sideways as it goes, as on ice: on the navball, the speed marker drifts away from the heading. With this mod, it
  holds its line.
- **On the slope**: load the save and wait. Without this mod, the rover slides down, brakes on. With
  this mod, it holds.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer) and records KSP Diag - Suspension: during a
drive of 800 m at 24 m/s on flat ground, for 30 s on the slope. Its first lines say how to run it:

    python check.py end-stops-flat --folder <your sandbox game> --label stock
    python check.py end-stops-slope --folder <your sandbox game> --label stock

Played once in each configuration, KSP 1.12.5 and KSP Community Fixes. A heavy rover on the Mun rests on its
end-stops too, for another reason, the fourth culprit:
[A heavy rover on Kerbin, the Mun and Minmus](a-heavy-rover-on-kerbin-the-mun-and-minmus.md).

| | Without this mod | `bumpStopLoad = false` | This mod |
|---|---|---|---|
| **Flat ground, above 20 m/s**: share of the weight the tyres read (5th percentile, median, 95th) | -1 %, 0 %, 1 % | 0 %, 0 %, 0 % | 82 %, 99 %, 116 % |
| Speed across the heading, 95th percentile | 0.15 m/s | 0.21 m/s | 0.03 m/s |
| Angle between the speed and the heading, 95th percentile | 0.36° | 0.51° | 0.08° |
| Wheel readings where the tyre skids | 64 % | 64 % | 1 % |
| **Slope**: share of the weight the tyres read (median) | 31 % | 31 % | 100 % |
| Speed over the last 10 s | up to 1.1 m/s | up to 1.1 m/s | up to 37 mm/s |
