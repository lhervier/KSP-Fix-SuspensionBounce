# Fourth culprit: below 1 g, the automatic spring is too soft

Part of [Suspension Bounce Fix](../../README.md), [the culprits](../../README.md#the-culprits): a culprit that puts
wheels on their end-stops.

On Kerbin, a rover at rest sinks to three quarters of its suspension travel. On the Mun, the same rover, if it is
heavy, sinks all the way: its wheels rest on their end-stops. It is not that the rover weighs too much there, nor that
its wheels are too weak: it is that KSP, under a gravity weaker than Kerbin's, gives it a spring much softer than it
would on Kerbin for the same load.

## The spring's boost

KSP sets a suspension's spring, at each step its wheel touches the ground, in
`ModuleWheelSuspension.SuspensionSpringUpdate`. It starts from the load the wheel carries (`sprungMassGravity`, which
already follows the body's gravity: on the Mun, a wheel carries six times less than on Kerbin) and multiplies it by a
boost that grows as the suspension sinks:

```csharp
boost = 1f + Mathf.Clamp(suspCompression * BoostCurve((float)base.vessel.mainBody.GeeASL * (float)base.vessel.gravityMultiplier, 1f), -1f, boostClamp);
...
spring = Mathf.Clamp(springTweakable * springRatio * sprungMassGravity * BoostCurve(boost, boostRatio + autoBoost), 0.01f, springClampMax);
```

This boost is what lets a rover wheel carry its weight within its travel: without it, the spring of a TR-2L or a
RoveMax, sunk all the way, only carries about half the load it is worked out for. With it, on Kerbin, a wheel at three
quarters of its travel has a spring three times stiffer, and holds.

## The gravity curve

But the pace at which this boost grows with how far the wheel sinks is weighted by a curve of the body's gravity,
`BoostCurve(GeeASL, 1)`, which is 1 / |1 − 2 / G| (G the surface gravity in g), rounded to the tenth:

```csharp
private float BoostCurve(float b, float power)
{
    return Mathf.Round(Mathf.Clamp(1f / Mathf.Abs(1f - 2f / Mathf.Pow(Mathf.Clamp(b, 0f, 2f), Mathf.Clamp(power, 0.01f, autoBoostClamp))), 0.01f, 10000000f) * 10f) / 10f;
}
```

It is 1 on Kerbin, more than 1 above (5.7 on Eve), and drops fast below:

| Body | Gravity | Curve |
|---|---|---|
| Eve | 1.7 g | 5.7 |
| Kerbin | 1 g | 1 |
| Laythe, Tylo | 0.8 g | 0.7 |
| Moho, Duna | 0.28-0.3 g | 0.2 |
| Mun, Ike, Dres, Vall, Eeloo | 0.11-0.24 g | 0.1 |
| Minmus, Gilly, Bop, Pol | 0.05 g and less | 0 |

The boost tops out at 1.5 (`boostClamp`). On Kerbin, a wheel reaches it at two thirds of its travel: its spring is
then three times stiffer. On the Mun, sunk all the way, it only reaches 1.1: its spring is only 1.2 times stiffer. On
Minmus, there is no boost at all any more.

Nothing in physics justifies this curve: the spring already starts from a load that follows gravity. A wheel that
carries six times less, under a spring worked out for six times less, would sink as far as on Kerbin if the boost were
the same. With the curve, it sinks much further, and a heavy enough rover reaches its end-stops.

## The half-ton floor

Another patch joins in: the load KSP works out the spring for never goes below the weight of half a ton on Kerbin,
about 4.9 kN, whatever the body (the floor, see [The patches](the-patches.md)). On a light body, this floor weighs
heavily: a wheel there is often worked out for much more than it carries, and its spring, even without a boost, holds
it easily.

- **On Minmus** (0.05 g), a wheel of a 10 t rover on six wheels carries about 0.8 kN. Its spring, worked out for
  4.9 kN, holds roughly half of that at the end of its travel without any boost: far more than its load. For a TR-2L
  to reach its end-stop there, it would have to carry nearly five tons.
- **On the Mun** (0.17 g), the same wheel carries about 2.8 kN, still below the floor. But half of 4.9 kN, multiplied
  by the boost of 1.2 the curve leaves it, only holds 2.7: the wheel reaches its end-stop.

It is the floor that puts the gap on the Mun. Without it, Minmus would be worse, since its curve is 0; with it, a
vessel of reasonable size does not reach its end-stops there. The culprit strikes where gravity is weak enough for the
curve to break the boost, but strong enough for a wheel's load to exceed what a spring worked out on the floor holds:
the Mun, and going by these formulas (only the Mun is measured here) the bodies of similar gravity, Ike, Dres, Vall,
Eeloo, and to a lesser extent Duna and Moho. On Minmus, Gilly, Bop and Pol, only a very heavy rover gets there. Laythe
and Tylo lose little: their curve is 0.7, and the boost still reaches its cap there.

## What you see

A rover on its end-stops falls to the [third culprit](a-bottomed-wheel-hides-its-weight-from-the-tyres.md): the
end-stop carries the rest of its weight, which the tyres do not see. On the Mun, a heavy rover parked with its brakes on
slides: on a slope, it goes down slowly, at a constant speed, without ever stopping.

The player who notices it switches their springs to *Override* and stiffens them with the slider, until the wheels come off
their end-stops; or sets them at random, for lack of knowing what is going on.

## What the mod changes

Below 1 g, the mod redoes the spring KSP has just worked out, with the same formulas, but with Kerbin's curve, 1, in
place of the body's. A wheel then sinks as on Kerbin, on every lighter body. It only keeps this spring if it is stiffer
than KSP's, and only writes it on the wheel with the same thresholds as KSP. In automatic mode as in manual: the curve
is not a slider, and a spring set by hand depends on it too.

On Kerbin and above, the curve is 1 or more: the mod changes nothing. The floor stays KSP's: on a very light body, the
spring of a lightly loaded wheel was already stiffer than its load calls for, and the boost, which grows with how far
the wheel sinks, stays small there since it sinks little. The `kerbinCurve` setting of the `settings.cfg` file turns
this part of the mod off.

## Seeing it

With [KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension), which gives, wheel by wheel, how far
the suspension has sunk, the end-stop and the spring. The same 10 t rover on six TR-2L, brakes on, springs left to the
game, loaded on Kerbin, on the Mun and on Minmus: the saves and the script are in
[A heavy rover on Kerbin, the Mun and Minmus](../checked/a-heavy-rover-on-kerbin-the-mun-and-minmus.md). Without the
mod, it sinks to three quarters on Kerbin, to half on Minmus, and down to its end-stops on the Mun.

Beware of one reading of the Diag: the share of the weight it shows carried by the suspensions is the one the tyres
read. With the mod, it includes the end-stop force the mod gives them (the third culprit): it says nothing about this
culprit, which the sinking and the end-stop show.

## In the mod's code

A postfix of `ModuleWheelSuspension.SuspensionSpringUpdate`, in
[Src/SpringGravityCurve.cs](../../Src/SpringGravityCurve.cs), which runs before the damper's (that one starts from
the damper written here). It does nothing when the body's curve is not weaker than Kerbin's; it reads the body's
gravity alone, without `Vessel.gravityMultiplier`, which KSP lowers while it puts a vessel down on loading, on Kerbin
too:

```csharp
float gee = (float)s.vessel.mainBody.GeeASL;
float geeCurve = BoostCurve(s, gee, 1f);
float kerbinCurve = BoostCurve(s, 1f, 1f);
if (geeCurve >= kerbinCurve)
{
    return;
}
```

Then KSP's formulas, with Kerbin's curve in place of the body's:

```csharp
float boost = 1f + Mathf.Clamp(s.suspCompression * kerbinCurve, -1f, BoostClamp(s));
float spring = Mathf.Clamp(s.springTweakable * s.springRatio * s.sprungMassGravity *
    BoostCurve(s, boost, s.boostRatio + s.autoBoost), 0.01f, SpringClampMax(s));
```
