# A heavy rover on Kerbin, the Mun and Minmus

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): the fourth culprit, below 1 g
the automatic spring is too soft ([the chapter](../the-culprits/the-automatic-spring-too-soft-below-1-g.md)), and
the third, a bottomed wheel hides its weight from the tyres
([the chapter](../the-culprits/a-bottomed-wheel-hides-its-weight-from-the-tyres.md)).

Each case is played four times: without this mod, with this mod and `kerbinCurve = false` in its settings (the part
of the mod that answers the fourth culprit is off), with this mod and `bumpStopLoad = false` (the part that answers the
third culprit is off), and with this mod as installed.
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) shows, wheel by wheel, how far the
suspension sinks, whether it is bottomed out, and its spring.

## The saves

The same rover, `SuspensionBounce Heavy Rover`, about 10 t on six TR-2L wheels each at the end of a truss, brakes
on, its springs left to the game, on three worlds. Copy the saves into the folder of a sandbox game, under `saves`,
and load them from the quickload menu (Alt+F9 by default).

- [diag/SuspensionBounceRoverKerbin.sfs](../../diag/SuspensionBounceRoverKerbin.sfs): on Kerbin, on a slope of 3°.
- [diag/SuspensionBounceRoverMun.sfs](../../diag/SuspensionBounceRoverMun.sfs): on the Mun, on a slope of 7°.
- [diag/SuspensionBounceRoverMinmus.sfs](../../diag/SuspensionBounceRoverMinmus.sfs): on Minmus, on a slope of 7°.

## By hand

Load a save and wait, KSP Diag - Suspension open. Without this mod, the rover sinks to three quarters of its travel on
Kerbin, to half of it on Minmus, and to the end of it on the Mun, where it slides down the slope, brakes on. With this
mod, it sinks on the Mun about as far as on Kerbin, and holds.

## By script

[diag/automation/check.py](../../diag/automation/check.py) loads the save through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer) and records KSP Diag - Suspension for 60 s;
its first lines say how to run it:

    python check.py rover-kerbin --folder <your sandbox game> --label stock
    python check.py rover-mun --folder <your sandbox game> --label stock
    python check.py rover-minmus --folder <your sandbox game> --label stock

Played once in each configuration, KSP 1.12.5 and KSP Community Fixes. How far the wheels on the ground sink and
their spring are averaged over the last 20 s.

| | Without this mod | `kerbinCurve = false` | `bumpStopLoad = false` | This mod |
|---|---|---|---|---|
| **Kerbin**: how far the wheels sink, spring | 0.73, 289 kN/m | 0.73, 289 kN/m | 0.73, 289 kN/m | 0.73, 289 kN/m |
| **Mun**: how far the wheels sink, spring | bottomed out, 23 kN/m | bottomed out, 23 kN/m | 0.67, 61 kN/m | 0.67, 61 kN/m |
| Mun: time with a wheel bottomed out | 100 % | 100 % | 0 | 0 |
| Mun: share of the weight the tyres read (median) | 71 % | 100 % | 100 % | 100 % |
| Mun: speed over the last 20 s | up to 115 mm/s | up to 8.5 mm/s | up to 2.8 mm/s | up to 1.3 mm/s |
| **Minmus**: how far the wheels sink, spring | 0.50, 20 kN/m | 0.50, 20 kN/m | 0.44, 24 kN/m | 0.44, 24 kN/m |

On the Mun with `kerbinCurve = false`, the share of the weight the tyres read includes the force of the end-stops,
which the mod hands them: the wheels are still bottomed out.
