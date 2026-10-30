# The fix

Part of [Suspension Bounce Fix](../README.md). What the mod changes for each of the three
[culprits](the-culprits.md), the two ways it can set the damper, and how it does it in the code.

## What the mod keeps

The mod keeps the spring KSP works out, the floor of half a ton included: a vessel sinks on its legs as it would
without it. Removing the floor would bring back the spring stuck at zero that it prevents. The mod only changes the
spring of a suspension that leans a lot (third culprit); otherwise, it only touches the damper.

## First culprit: a damper sized to what the leg carries

The mod works out what each leg carries: the vessel's mass shared between the wheels that touch the ground, without
rounding or floor. A leg far from the vessel's centre of mass carries less than that share when the vessel moves: a
push at its wheel turns the vessel as much as it lifts it, and the wheel answers with only part of the vessel's mass.
A wheel at the corner of a rover, alone on the ground while the rest of the rover still stands on other parts, would
otherwise count the whole rover as its share. The mod takes the smaller of the two: the share, and the mass the vessel,
taken as one rigid body, sets in motion at the wheel.

From it, it works out a limit: the damper that takes off half the leg's speed in a single physics step. A damper that
stops the leg in one step would already be on the edge, and a rover's wheels, at the end of trusses whose joints give
way, set even less mass in motion at first. A stronger damper from the game is brought down to that limit; a weaker
one is left as it is.

The limit grows with the mass carried: an ordinary lander stays far below it. Very light vessels and parts placed
alone reach it, and so do light rovers with stiff suspensions, as on Eve, where KSP stiffens every suspension; they
then settle instead of being thrown or bouncing on.

## Second culprit: the suspension set in the air too

At each step, the mod also sets the damper of a wheel in the air, which KSP leaves as it was at the last contact. It
cannot know how many wheels will touch the ground at the next contact: it takes the smallest share the leg can carry,
the one it will have when all the vessel's suspensions touch. At the first step of the next contact, the damper is
already no more than the leg can take.

A leg placed alone, which left the ground with the settings of its 20 t, touches the ground again with a damper sized
to its 50 kg. The spring keeps its last setting.

## Third culprit: the leaning suspension

The mod measures how far the suspension's axis leans on the surface its ray hits: the ground, or the side of another
vessel; in the air, on the vertical.

- Up to 30°, the suspension keeps all its spring and all its damper.
- Beyond that, both weaken more and more: a third is left at 60°, and nothing when the axis lies flat on the surface,
  or points away from it.
- The limit of the first culprit also drops with the lean, by half at 45°: the more the suspension leans, the harder
  the same push shakes the part, and the weaker the damper must be not to send it back.

A leg lying down whose foot brushes the ground, or the side of a lander, then barely pushes any more, and can no
longer be thrown. On the other hand, a rover wheel touching a steep face, a rock or the edge of a crater, gets a
softer suspension for as long as it touches it: this is still to be checked (*Work in progress* in the README).

## The two modes

The `mode` setting in `GameData/KSPFixSuspensionBounce/PluginData/settings.cfg`, read when KSP starts:

- **`Minimal`** (the default): the mod keeps KSP's spring and damper, unless the damper is above the limit, or the
  axis leans more than 30°. It writes nothing on a suspension it does not change: every other vessel behaves exactly
  as without it.
- **`MassBased`**: the mod works out every damper again from the spring and the mass the leg carries, in place of the
  correction for heavy vessels. In stock, the more a leg carries, the more it swings before it settles, until that
  correction steps in, and a leg that carries little is held too stiffly. With this mode, every vessel settles on its
  legs the same way, whatever its weight: as, in stock, a lander of about one ton per leg does. This changes how every
  lander and every rover settles. The lean and the limit apply too.
- **`Stock`**: the mod changes nothing.

## In the code

Two Harmony postfixes on `ModuleWheelSuspension`, in [Src/SuspensionDamper.cs](../Src/SuspensionDamper.cs).

**On the ground**: a postfix of `SuspensionSpringUpdate`, which the game calls at every step where the wheel touches
something. The mass carried is the vessel's mass shared between the wheels on the ground, counted once per vessel and
per step in the `suspensionModules` list of the `SuspensionLoadBalancer`:

```csharp
float share = (float)vessel.totalMass / Mathf.Max(count.Grounded, 1);
Apply(__instance, wheel, share, StockDamper(__instance), false);
```

**In the air**: a postfix of `FixedUpdate`, for a wheel that touches nothing (`wheel.IsGrounded` false), on a part
that is not `packed`. The mass is shared between all the vessel's suspensions:

```csharp
float share = (float)vessel.totalMass / Mathf.Max(count.All, 1);
Apply(__instance, wheel, share, StockDamper(__instance), true);
```

Both start from the spring and the damper the game last worked out (its private fields `spring` and `damper`), not
from the wheel's, which may already be the mod's.

**The lean**: `LeanCosine` returns the cosine of the angle between `wheel.SuspensionAxis` and the normal of the
surface hit (`wheelState[0].hit.normal`), or the local vertical (`FlightGlobals.getUpAxis`) when the wheel touches
nothing. `Fade` turns it into the share of its spring and damper the suspension keeps:

```csharp
if (cos >= CosFullUpTo)   // cos 30°
{
    return 1f;
}
if (cos <= 0f)
{
    return 0f;
}
float ratio = cos / CosFullUpTo;
return ratio * ratio;
```

**The limit** of the damper, in both modes:

```csharp
float limit = share * Mathf.Max(cos, 0f) * Mathf.Max(cos, 0f) / (2f * Time.fixedDeltaTime);
```

**The mass set in motion** at the wheel, before the limit and `MassBased` use it: `VesselInertia`, in
[Src/VesselInertia.cs](../Src/VesselInertia.cs), adds up once per vessel and per step the mass, the centre of mass and
the inertia tensor of the parts' rigidbodies (each part's own inertia, turned to world axes, plus its mass about the
centre of mass). At the point the wheel touches, along the normal of the surface, the vessel answers as a mass m with
1 / m = 1 / M + (r × n) · I⁻¹ · (r × n), r the point from the centre of mass, n the normal:

```csharp
share = Mathf.Min(share, (float)inertia.EffectiveMass(point, normal));
```

**`MassBased`**: the game's formula, with the mass carried in place of `damperFudge`:

```csharp
return Mathf.Sqrt(Mathf.Max(spring * suspension.damperRatio * mass, 0f)) * suspension.damperTweakable;
```

**Writing to the wheel**:

```csharp
if (massBased || isLimited || wasLimited || isFaded || wasFaded)
{
    wheel.wheelCollider.damperRate = damper;
}
if (isFaded || wasFaded)
{
    wheel.wheelCollider.springRate = stockSpring * fade;
}
```

In `Minimal`, the mod only writes what it changes, and once more at the step where it stops changing it: the game only
writes a spring or a damper that differs from the wheel's by more than 3 or 1.05, and would otherwise keep the mod's.
In the air, a limited suspension stays marked as such by the mod, so that the game sets its own back at the next
contact.

**The log**: with `logLevel = Debug`, a line in `KSP.log` when a suspension starts or stops being limited or
weakened, and, in `MassBased`, a line the first time each suspension is set.
