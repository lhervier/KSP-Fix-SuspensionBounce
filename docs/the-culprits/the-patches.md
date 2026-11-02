# The patches

Part of [Suspension Bounce Fix](../../README.md), [the culprits](../../README.md#the-culprits): the patches of KSP 1.12.5 on
[the base model](the-base-model.md), each with its consequences.

KSP got several corrections for the cases where its base model was not enough. Squad did not comment on them: why they
are there is worked out from what they do.

## The half-ton floor

Whatever its real share, KSP acts as if each wheel carried at least the weight of half a ton on Kerbin. Probably for
this: under a very small weight, the spring worked out is so weak that the rounding of
[the next patch](#rounding-and-the-write-threshold) leaves it at zero, and the leg is crushed against its stop.

Consequences:

- **No more zero spring.** A lander set down on a low gravity body, each of whose legs carries barely a few tens of
  kilograms of weight, is no longer crushed onto its stops.
- **A light vessel is held up as half a ton would be.** Its legs barely sink, and get the damper of half a ton, far too
  strong for them: this is [the first culprit](a-damper-too-strong-for-a-light-vessel.md).

<details>
<summary>In the code</summary>

The `minMass` of `SuspensionLoadBalancer.FixedUpdate`, which is `0.5`:

```csharp
suspensionModules[j].sprungMassGravity = Mathf.Max((float)(num * (double)share * (double)multiplier), minMass);
```

</details>

## Rounding and the write threshold

KSP rounds the spring and the damper to the unit, and only writes a new spring on the wheel if it differs from the old
one by more than 3. Probably against a loop: the spring depends on the compression
([next patch](#the-boost-with-compression)), and the compression on the spring; worked out again at each step, it could
make the suspension vibrate. This threshold suits springs of a few tens or hundreds, not of a few units: a spring that
should go from 0 to 1 stays at 0.

Consequences:

- **No vibration** between the spring and the compression, if that was indeed the aim.
- **A small spring is never written**: under 3, it stays at zero. This is what makes
  [the floor](#the-half-ton-floor) necessary.
- **The spring changes in steps**, lagging behind what KSP works out, since the boost with compression keeps changing
  it.

<details>
<summary>In the code</summary>

The end of `ModuleWheelSuspension.SuspensionSpringUpdate`; `oscillationDamper` is `3`:

```csharp
damper = Mathf.Max(Mathf.Round(damper), 0.1f);
spring = Mathf.Max(Mathf.Round(spring), 0.1f);
if (spring - wheel.wheelCollider.springRate > oscillationDamper)
{
    wheel.wheelCollider.springRate = spring;
}
else if (wheel.wheelCollider.springRate > spring + oscillationDamper)
{
    wheel.wheelCollider.springRate = spring;
}
if (Mathf.Abs(damper - wheel.wheelCollider.damperRate) > 1.05f)
{
    wheel.wheelCollider.damperRate = damper;
}
```

</details>

## The boost with compression

The further the wheel sinks, the stiffer its spring, up to twice its starting stiffness; and when it goes past 80 % of
its travel, it stiffens further, the more so the longer it stays there, up to three times. Probably against the stop:
a wheel that reaches the end of its travel knocks, and bounces back. Under high gravity, this boost comes in from the
first centimetres.

Consequences:

- **Fewer hits on the stop** when landing and over bumps.
- **A spring that changes with the compression**, which creates the loop that
  [the write threshold](#rounding-and-the-write-threshold) fights. On Eve, its stiffness changes a lot at the slightest
  compression.
- **A damper that stiffens with it**, since it follows the spring: it is strongest at the moment of impact, where a
  light vessel has the least margin.

<details>
<summary>In the code</summary>

In `ModuleWheelSuspension.SuspensionSpringUpdate`, `boost` grows with the compression (`suspCompression`, from 0 to 1)
and with gravity; `boostClamp` is `0.5`. `BoostCurve` turns it into a multiplier of the spring:

```csharp
boost = 1f + Mathf.Clamp(suspCompression * BoostCurve((float)base.vessel.mainBody.GeeASL * (float)base.vessel.gravityMultiplier, 1f), -1f, boostClamp);
...
spring = Mathf.Clamp(springTweakable * springRatio * sprungMassGravity * BoostCurve(boost, boostRatio + autoBoost), 0.01f, springClampMax);
```

```csharp
private float BoostCurve(float b, float power)
{
    return Mathf.Round(Mathf.Clamp(1f / Mathf.Abs(1f - 2f / Mathf.Pow(Mathf.Clamp(b, 0f, 2f), Mathf.Clamp(power, 0.01f, autoBoostClamp))), 0.01f, 10000000f) * 10f) / 10f;
}
```

`UpdateAutoBoost` raises `autoBoost` as long as the compression is above 0.8, and lowers it again below 0.4:

```csharp
else if (st > 0.8f)
{
    autoBoost = Mathf.Clamp(autoBoost + 0.2f * Time.fixedDeltaTime, 0.85f, 2f);
}
else if (st < 0.4f)
{
    autoBoost = Mathf.Max(0f, autoBoost - 0.2f * Time.fixedDeltaTime);
}
```

</details>

## The big-vessel correction

Above about forty tons, the damper stiffens with the vessel's mass and with the compression of the legs, up to nearly
four times. Probably against big vessels that sway on their legs, which the base model damps less. It reads the mass of
the whole vessel, not what each leg carries.

Consequences:

- **Very big vessels on few legs are better damped.**
- **Nothing below about forty tons**, even for a leg carrying several tons. Without harm: a vessel of 20 t on four legs
  already settles in two oscillations.

<details>
<summary>In the code</summary>

`damperFudge`, in `ModuleWheelSuspension.SuspensionSpringUpdate`, goes from 1 to 15 under the square root of the
damper, from the mass of the whole vessel times the wheel's compression:

```csharp
float num = sprungMass * suspCompression;   // sprungMass = vessel.totalMass
...
damperFudge = Mathf.Lerp(1f, 15f, Mathf.InverseLerp(10f, 100f, num));
damper = Mathf.Sqrt(Mathf.Clamp(spring * damperRatio * damperFudge, 0.01f, damperClampMax)) * damperTweakable;
```

</details>

## The automatic sliders

Each suspension has two sliders in the part's window, "Spring" and "Damper". As long as the automatic setting is on, as
it is by default, KSP sets them on its own, once, at the first contact: the spring the stiffer as the leg is loaded
near its maximum load, the damper from the body's gravity, 1.1 under 1 g, 1 on Kerbin, then stronger and stronger up
to 2 on the heaviest body of the system, Eve (1.7 g). So that the player has nothing to set.

Consequences:

- **Nothing for the player to set**, on every body.
- **A damper twice as strong on Eve** as on Kerbin, for the same mass to slow down: it makes
  [the first culprit](a-damper-too-strong-for-a-light-vessel.md) worse.

<details>
<summary>In the code</summary>

In `ModuleWheelSuspension.SuspensionSpringUpdate`; `MaximumSurfaceGeeASL` is the highest surface gravity of the bodies
with a solid surface, Eve's in stock, and `damperLerpBase` is `1`:

```csharp
if (autoSpringDamper && (!autoSpringDamperSet || (maximumLoad > 0f && num > maximumLoad)))
{
    if (base.vessel.mainBody.GeeASL < 1.0)
    {
        damperTweakable = 1.1f;
    }
    else
    {
        float t2 = Mathf.InverseLerp(damperLerpBase, (float)PSystemManager.Instance.MaximumSurfaceGeeASL, (float)base.vessel.mainBody.GeeASL);
        damperTweakable = (float)Math.Round(Mathf.Lerp(1f, 2f, t2), 2);
    }
    if (spring > 0f && (!autoSpringDamperSet || (maximumLoad > 0f && num > maximumLoad)))
    {
        float value = suspensionDistance * spring / (float)PhysicsGlobals.GravitationalAcceleration;
        float t = Mathf.InverseLerp(0f, maximumLoad * 2f, value);
        springTweakable = (float)Math.Round(Mathf.Lerp(1f, 3f, t), 2);
        autoSpringDamperSet = true;
    }
}
```

</details>

## High gravity

Above 1.5 g, so only on Eve in stock, KSP triples the spring and multiplies the damper by 1.2. Probably so that a
vessel is not crushed onto its legs there. With the slider set to 2, a suspension gets on Eve a damper 2 × 1.2 = 2.4
times as strong as on Kerbin for the same spring, while the mass to slow down has not changed.

Consequences:

- **More margin before the stop** on Eve: the vessel sinks there three times less than the weight rule alone would
  have it.
- **An even stronger damper**, on top of the slider at 2: it makes
  [the first culprit](a-damper-too-strong-for-a-light-vessel.md) worse.
- **A jump at 1.5 g**: on a body of a planet pack at 1.49 g or at 1.51 g, the same vessel gets a spring three times
  different.

<details>
<summary>In the code</summary>

In `ModuleWheelSuspension.SuspensionSpringUpdate`, once the spring and the damper are worked out; `highGeeThreshold` is
`1.5`, `highGeeSpringTweakable` `3`, `highGeeDamperTweakable` `1.2`:

```csharp
if (adjustForHighGee && base.vessel.mainBody.GeeASL > (double)highGeeThreshold)
{
    spring *= highGeeSpringTweakable;
    damper *= highGeeDamperTweakable;
}
```

</details>
