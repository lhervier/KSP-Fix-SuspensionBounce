# A rover on its end-stops

Part of [Suspension Bounce Fix](../../README.md), [End-Stop Slide](../README.md#checking-the-culprit):
[a bottomed wheel hides its weight from the tyres](../a-bottomed-wheel-hides-its-weight-from-the-tyres.md).

[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) shows, among others, the share of the
vessel's weight its suspensions carry, as the tyres read it: this mod holds the end-stops itself, and adds the force it
holds to it.

## The saves

Copy them into the folder of a sandbox game, under `saves`, and load them from the quickload menu (Alt+F9 by
default).

- [saves/SuspensionBounceEndStopsFlat.sfs](../saves/SuspensionBounceEndStopsFlat.sfs): `SuspensionBounce Rover`,
  2.4 t on four TR-2L wheels, on flat ground near the space centre, on soft springs: all four wheels rest on their
  end-stops.
- [saves/SuspensionBounceEndStopsSlope.sfs](../saves/SuspensionBounceEndStopsSlope.sfs): the same rover, brakes on,
  on a slope of about 20° west of the space centre, on soft springs, resting on its end-stops; saved without Parking
  Brake.

Each save holds the soft springs twice. Without this mod, the game uses its own sliders, set by hand at their lowest
(0.05) on flat ground, at 0.3 on the slope. This mod hides those sliders and uses its own setting, saved with the rover:
the one its suspension window makes (open it from the toolbar, tick all four wheels, keep Kerbin as the body, type a
mass of 0.01 t, share the weight by the centre of mass, and press *Auto*: the springs are predicted for a rover of
10 kg, and the wheels sink onto their end-stops). The saves were made with this mod, the rover settled on its
end-stops and still.

## By hand

Load the save: nothing to set, with this mod or without it.

- **On flat ground**: drive the rover at full speed, about 24 m/s, in a straight line. Without this mod, it slides
  sideways as it goes, as on ice: on the navball, the speed marker drifts away from the heading. With this mod, it
  holds its line. With Fixed Tuning alone, it holds its line too, though its tyres read only a third of its weight.
- **On the slope**: load the save and wait. Without this mod, and with Fixed Tuning alone, the rover slides down,
  brakes on. With this mod, it holds.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer), the springs as saved, and records KSP Diag -
Suspension: during a drive of 800 m at 24 m/s on flat ground, for 30 s on the slope. Its first lines say how to run it,
and how to choose a configuration of this mod (its `settings.cfg`, KSP started again):

    python check.py end-stops-flat --folder <your sandbox game> --label stock
    python check.py end-stops-slope --folder <your sandbox game> --label stock
    python check.py end-stops-flat --folder <your sandbox game> --trials 3 --label modded
    python check.py end-stops-slope --folder <your sandbox game> --trials 3 --label modded

Played three times in each configuration, KSP 1.12.5, KSP Community Fixes,
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision),
[KSP-Fix-AutoStrut](https://github.com/lhervier/KSP-Fix-AutoStrut) and
[Physics Easing Fix](https://github.com/lhervier/KSP-Fix-PhysicsEasing), without Parking Brake, the springs as saved:
without this mod; with this mod and its default settings; with Fixed Tuning alone, `damperBounce = false` and
`endStopSlide = false` in the mod's `settings.cfg`; and with Fixed Tuning and this fix, `damperBounce = false`.
Physics Easing Fix holds the rover still as it loads, and lets it go once its wheels hold it.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Flat ground, above 20 m/s**: share of the weight the tyres read (5th percentile, median, 95th) | -0.8 to -0.4 %, 0 %, 0.4 to 0.8 % | 37 %, 37 %, 37 % | 97 to 98 %, 99 %, 100 % | 97 to 98 %, 99 %, 100 % |
| Speed across the heading, 95th percentile | 0.16 to 0.38 m/s | 0.03 to 0.04 m/s | 0.04 to 0.05 m/s | 0.04 to 0.05 m/s |
| Angle between the speed and the heading, 95th percentile | 0.38° to 0.91° | 0.07° to 0.10° | 0.09° to 0.12° | 0.10° to 0.12° |
| Wheel readings where the tyre skids | 64 to 66 % | 7 % | 1.6 % | 1.6 % |

On flat ground, Fixed Tuning alone already keeps the rover in a straight line: its soft springs still carry a little
over a third of its weight. This fix gives the tyres the whole weight back, and they skid four times less.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Slope**: share of the weight the tyres read (median) | 31 % | 39 % | 99 % | 99 % |
| Top speed over the ground | 1.5 m/s | 1.2 m/s | 0.003 m/s | 0.003 to 0.005 m/s |
| How far it turned in 30 s | −5.9° to −6.0° | −7.4° to −7.6° | −0.02° to −0.03° | 0.0° to −0.02° |
| Speed over the last 10 s | 1.1 m/s | 0.88 m/s | up to 0.37 mm/s | up to 0.41 mm/s |

On the slope, this fix is what holds the rover. Without this mod, it slides down, brakes on, at about 1.1 m/s, and with
Fixed Tuning alone at about 0.9 m/s, turning as it goes. With this fix, it holds from the moment Physics Easing Fix
lets it go.
