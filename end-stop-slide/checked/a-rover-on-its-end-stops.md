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
  on a slope of about 20° west of the space centre, on soft springs, resting on its end-stops.

Each save holds the soft springs twice. Without this mod, the game uses its own sliders, set by hand at their lowest
(0.05) on flat ground, at 0.3 on the slope. This mod hides those sliders and uses its own setting, saved with the rover:
the one its suspension window makes (open it from the toolbar, tick all four wheels, keep Kerbin as the body, type a
mass of 0.01 t, share the weight by the centre of mass, and press *Auto*: the springs are predicted for a rover of
10 kg, and the wheels sink onto their end-stops). Both saves were made with this mod, the rover settled on its
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

Played once without this mod, KSP 1.12.5 and KSP Community Fixes, the springs as saved. With this mod, KSP 1.12.5, KSP
Community Fixes, Terrain Precision Fix, KSP-Fix-AutoStrut and Parking Brake, three times in each configuration, the
springs as saved: with its default settings; with Fixed Tuning alone, `damperBounce = false` and
`endStopSlide = false` in the mod's `settings.cfg`; and with Fixed Tuning and this fix, `damperBounce = false`.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Flat ground, above 20 m/s**: share of the weight the tyres read (5th percentile, median, 95th) | -1 %, 0 %, 1 % | 34 %, 34 %, 34 % | 98 %, 99 %, 100 % | 98 %, 99 %, 100 % |
| Speed across the heading, 95th percentile | 0.15 m/s | 0.02 m/s | 0.03 m/s | 0.03 m/s |
| Angle between the speed and the heading, 95th percentile | 0.36° | 0.05° | 0.07° to 0.08° | 0.07° to 0.08° |
| Wheel readings where the tyre skids | 64 % | 7 % | 1.3 to 1.6 % | 1.4 to 1.6 % |

On flat ground, Fixed Tuning alone already keeps the rover in a straight line: its soft springs still carry a third of
its weight. This fix gives the tyres the whole weight back, and they skid five times less.

| | Without this mod | Fixed Tuning alone | Fixed Tuning and this fix | This mod |
|---|---|---|---|---|
| **Slope**: share of the weight the tyres read (median) | 31 % | 36 to 37 % | 99 % | 99 % |
| Top speed over the ground | — | 0.04, 0.04 and 1.3 m/s | 0.007 m/s | 0.001 to 0.007 m/s |
| How far it turned in 30 s | — | −6.5° to 0.2° | −0.2° to 0.2° | 0.0° to 0.03° |
| Speed over the last 10 s | up to 1.1 m/s | up to 42, 42 and 974 mm/s | up to 0.16 mm/s | up to 0.57 mm/s |

On the slope, this fix is what holds the rover. The trial at 974 mm/s with Fixed Tuning alone is the one where Parking
Brake held the rover as it loaded and it never looked still: the script let it go after 15 s.
