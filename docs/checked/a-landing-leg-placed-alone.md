# A landing leg placed alone

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): the second culprit, a
suspension in the air keeps its old settings, and the first one, a damper too strong for a light vessel.

The same damper that throws a light probe throws a landing leg put down alone in EVA construction, but stock hides it
most of the time: `ModuleCargoPart.MakePartSettle` makes a part placed alone weigh 4 t for its first 3 s on the
ground, 20 t below 0.8 g. Under that weight, the damper is no longer too stiff, and the leg simply tips over onto its
flat side, its foot, where its suspension is, off the ground. When its own mass comes back:

- on Kerbin, the leg is lying still, and stays there;
- on Minmus, it is still rolling: its foot touches the ground again, and the damper throws it.

[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision), with its `fixLonePartMass` setting
on, gives the part its own mass from the start: its foot is on the ground, and the damper throws it at once, on
Kerbin too.

## The saves

[diag/SuspensionBounceLegMinmus.sfs](../../diag/SuspensionBounceLegMinmus.sfs) and
[diag/SuspensionBounceLegKerbin.sfs](../../diag/SuspensionBounceLegKerbin.sfs): Bill Kerman, an engineer, on EVA
next to `SuspensionBounce Lander`, a Mk1 pod on a fuel tank and an engine, standing on LT-1 landing legs, on Minmus
and on Kerbin. Copy them into the folder of a sandbox game, under `saves`, and load them from the quickload menu
(Alt+F9 by default).

## By hand

1. Open the EVA construction panel and pick the *Place* tool.
2. Click one of the lander's legs that does not touch the ground: it is now held.
3. Click on the ground, foot down.

Without this mod, in stock, the leg tips over onto its side; on Minmus, it leaves the ground a few seconds later and
climbs tens of metres. With Terrain Precision Fix, the leg leaves the ground within a fraction of a second, spinning:
on Kerbin, it rises a few metres and is destroyed when it lands (`landingLeg1 (…) Exploded!!` in `KSP.log`); on
Minmus, it goes much higher. With this mod, with or without Terrain Precision Fix, the leg tips over onto its side,
as a leg standing on its foot would, and may roll or stand up again before it comes to rest; the mod writes:

```
[KSPFixSuspensionBounce] [DEBUG] Damper of landingLeg1 (3552957469) of Jambes d'atterrissage LT-1 limited to 0.924 for 0.037 t carried and 0.05 t moved, KSP computed 6
```

## By script

[diag/automation/check.py](../../diag/automation/check.py) plays these steps through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer): it clicks the leg, then a point of the ground
given in pixels of a KSP window of 1286 × 795; with a window of another size, give the point in front of Bill with
`--ground`. The leg is not the active vessel: the script takes a screenshot every 4 s and counts the `Exploded`
lines of `KSP.log`.

    python check.py leg-minmus --folder <your sandbox game> --ksp <your KSP folder> --trials 3 --label stock
    python check.py leg-kerbin --folder <your sandbox game> --ksp <your KSP folder> --trials 3 --label stock

Played three times on each world without this mod and with it, KSP 1.12.5, KSP Community Fixes and Terrain Precision
Fix (`fixLonePartMass = true`):

| | Without this mod | With this mod |
|---|---|---|
| Minmus | out of sight 4 s after it is placed, 3 times out of 3 | 20 s after, next to where it was placed, on its side or on its foot, 3 times out of 3 |
| Kerbin | out of sight 4 s after it is placed, 3 times out of 3; `landingLeg1 (…) Exploded!!` 2 times | 20 s after, on its side next to where it was placed, 3 times out of 3; no `Exploded` line |
