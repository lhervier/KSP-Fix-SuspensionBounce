# The culprits

Part of [Suspension Bounce Fix](../README.md). How KSP 1.12.5 sets the spring and the damper of a suspension, and
the three ways that throws a light vessel, or a part placed alone, into the air.

## How KSP sets a suspension

A suspension in KSP is a spring and a damper. The stiffness of the spring decides how far the leg sinks under the
vessel's weight. The damper slows the leg's movements down, so that it does not bounce forever. KSP sets both by
itself, all the time, as long as the wheel touches something.

**The stiffness follows the weight.** KSP shares the vessel's weight between the wheels that touch the ground, and
gives each one a spring sized to its share. The more a leg carries, the stiffer its spring. As a result, a light or a
heavy vessel, on Kerbin or on the Mun, sinks about as far on its legs, and the player has nothing to set.

**With a floor.** Whatever its real share, KSP treats each wheel as if it carried at least the weight of half a ton on
Kerbin. Probably for this reason: under a very small weight, the stiffness it works out gets so low that the spring
stays at zero, and the leg is crushed against its stop.

**The damper follows the stiffness, not the mass.** It is worked out from the spring alone: a stiff spring gets a
strong damper, and the mass the leg carries plays no part. There is a correction for heavy vessels, beyond some forty
tons: their damper hardens with the mass and with how far the legs sink. But it reads the mass of the whole vessel,
not what each leg carries: 40 t on 20 legs is treated as 40 t on 4. It plays no part in the bounces on this page.

## First culprit: a damper too strong for a light vessel

A leg carrying 50 kg gets, because of the floor, the stiffness of a leg carrying half a ton, and the damper that goes
with it. For that leg, the damper is far too strong.

In real life, a damper that is too strong throws nothing: it only makes the leg slow to settle. But KSP works out
physics in small steps, fifty a second. At each step, the damper brakes the leg according to the speed it had at the
step before. On a loaded leg, that braking takes a little speed off at each step. On a leg that carries almost
nothing, it is so strong that in a single step it does more than stop it: it sends it back the other way, faster than
it came. The next step does it again, and within a few steps the vessel is off at tens of metres per second.

## Second culprit: a suspension in the air keeps its old settings

KSP only sets the stiffness and the damping while the wheel touches something. In the air, the wheel keeps those of
its last contact. At the next contact, it first pushes with these old settings, then KSP works them out again. That
does no harm if the vessel has kept its mass in the meantime.

A part placed alone in EVA construction does not: for its first three seconds on the ground, KSP makes it weigh 4 t,
and even 20 t under low gravity. A leg that leaves the ground during that time keeps a suspension set for 20 t. When
its foot touches the ground again, back to its own 50 kg, that suspension throws it.
[Terrain Precision Fix](https://github.com/lhervier/KSP-Fix-TerrainPrecision), which gives the part its own mass from
the start, removes that case, but then lets the first culprit act at once.

## Third culprit: a suspension that pushes at a slant

A suspension measures how far it sinks along its axis, like a stick held out in front of you: it touches whatever it
meets, the ground or the side of another vessel. When the part leans or lies down, the stick meets the surface at a
slant. The slightest movement of the part then moves the point it touches a long way, and how far the suspension reads
it has sunk jumps from one step to the next.

KSP does not take that into account: the stiffness and the damping stay those of an upright suspension. The pushes
become violent, change direction at each step, and grow. The more the suspension leans, the harder the same push
shakes the part: about twice as hard at 50°. A leg lying down whose foot brushes the ground, or the side of a lander,
ends up thrown that way.

## In the code

**`SuspensionLoadBalancer.FixedUpdate`** (a vessel module) gives each suspension the weight it carries,
`sprungMassGravity`: the vessel's mass times the surface gravity in g, rounded to a tenth, shared evenly between the
wheels on the ground, and never below `minMass`, which is `0.5`:

```csharp
num = Math.Round(num * vessel.gravityMultiplier * vessel.mainBody.GeeASL * 10.0) / 10.0;
...
suspensionModules[j].sprungMassGravity = Mathf.Max((float)(num * (double)share * (double)multiplier), minMass);
```

**`ModuleWheelSuspension.SuspensionSpringUpdate`** turns that weight into a spring, then the spring into a damper;
`damperFudge` is the correction for heavy vessels:

```csharp
damperFudge = Mathf.Lerp(1f, 15f, Mathf.InverseLerp(10f, 100f, num));   // num = vessel mass * compression
spring = Mathf.Clamp(springTweakable * springRatio * sprungMassGravity * BoostCurve(boost, boostRatio + autoBoost), 0.01f, springClampMax);
damper = Mathf.Sqrt(Mathf.Clamp(spring * damperRatio * damperFudge, 0.01f, damperClampMax)) * damperTweakable;
```

It then rounds both, and writes the spring to the wheel only when it changes by more than `oscillationDamper`, which
is `3`: a spring that should go from 0 to 1 stays at 0.

```csharp
spring = Mathf.Max(Mathf.Round(spring), 0.1f);
if (spring - wheel.wheelCollider.springRate > oscillationDamper)
{
    wheel.wheelCollider.springRate = spring;
}
```

**`ModuleWheelSuspension.FixedUpdate`** only calls it while the wheel touches something:

```csharp
if (HighLogic.LoadedSceneIsFlight && baseSetup && !base.part.packed && wheel != null && wheel.IsGrounded)
{
    vesselMass = (float)base.vessel.totalMass;
    SuspensionSpringUpdate(vesselMass);
}
```

Neither method looks at the angle between the suspension's axis and the surface its ray hits.
