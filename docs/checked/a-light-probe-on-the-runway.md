# A light probe on the runway

Part of [Suspension Bounce Fix](../../README.md), [checked](../../README.md#checked): the first culprit, a damper
too strong for a light vessel.

## The craft

[diag/SuspensionBounce.craft](../../diag/SuspensionBounce.craft): an OKTO2 probe core, three LT-05 micro landing
struts, a Z-100 battery and two OX-STAT solar panels, about 100 kg in all, so about 33 kg per leg. Copy it into
`Ships/SPH` of a sandbox game.

## By hand

Launch the craft on the runway, and watch the altimeter.

Without this mod, the probe leaves the runway the moment physics starts: the altimeter, which reads 70 m on the
runway, goes above 230 m, then the probe falls back, bounces up again, and so on. Putting it back on the ground by any
other means (the *Set Position* tool of the debug menu, for one) ends the same way. With this mod, the probe settles
on its legs within a fraction of a second, and stays there.

With `logLevel = Debug` in the settings, the mod writes a line for each leg whose damper it lowers:

```
[KSPFixSuspensionBounce] [DEBUG] Damper of miniLandingLeg (749513586) of SuspensionBounce limited to 0.833 for 0.033 t carried and 0.07 t moved, KSP computed 7
```

## By script

[diag/automation/check.py](../../diag/automation/check.py) launches the craft through
[KSP-Util-MCPServer](https://github.com/lhervier/KSP-Util-MCPServer) and records
[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) for 20 s; its first lines say how to run it:

    python check.py probe --folder <your sandbox game> --trials 3 --label stock

Played once without this mod and once with it, KSP 1.12.5 and KSP Community Fixes:

| | Without this mod | With this mod |
|---|---|---|
| Time with no leg on the ground | 99 % | 0 |
| Top speed over the ground | 66 m/s | 0.04 m/s |
| Parts at the end | 0 of 7 | 7 of 7 |
| `KSP.log` | `batteryPack`, `solarPanels5` (twice), `probeCoreOcto2.v2` `Exploded!!` | nothing |
