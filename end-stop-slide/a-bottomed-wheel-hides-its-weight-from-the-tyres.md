# A bottomed wheel hides its weight from the tyres

Part of [Suspension Bounce Fix](../README.md), [End-Stop Slide](README.md#the-culprits): a culprit that makes vessels
slide.

KSP's tyres are not Unity's: wheels, legs and landing gear go through Vehicle Physics Pro (VPP), a vehicle physics
library that KSP ships with. The suspension, though, stays Unity's: a `WheelCollider`, spring and damper, which
measures how far it has sunk and pushes the vessel.

## What VPP reads in the suspension

To know what a tyre can do, VPP needs to know how much the wheel carries. It does not weigh the vessel: it reads the
force the suspension has just applied, spring and damper, and uses it three times.

- **Grip**: the largest force the tyre can give, braking, accelerating or sideways, is proportional to this load. A
  wheel that carries half as much holds half as much.
- **Holding on a slope**: at rest or nearly, VPP pushes each wheel uphill by what it takes for the load it carries not
  to slide.
- **The pull back against sliding**: when the wheel slides on the ground, VPP brakes it all the harder as the mass it
  carries is large, to stop that sliding in one step.

As long as the suspension carries the whole weight, these three calculations are right.

## The end-stop

A suspension has a travel. When the wheel carries more than the spring can hold within that travel, it reaches its
end-stop, and the end-stop carries the rest: nothing unusual, a real car does the same. In KSP, a wheel reaches its
end-stop on a heavy rover on the Mun, whose springs the game leaves soft there
([the gravity curve](../how-ksp-sets-a-suspension/the-patches.md#the-gravity-curve)), under the low wheel of a rover
standing across a slope, under the front wheels when braking, or when the player sets their springs very soft. But
the end-stop of the physics has two faults.

**It does not say what it carries.** The contact force VPP reads only holds the spring's. For VPP, a bottomed wheel
carries what its spring pushes, not what it really carries, and the share that goes through the end-stop vanishes from
its three calculations:

- grip drops, down to the floor VPP keeps for each tyre;
- holding on the slope only makes up for part of the slope;
- the pull back against sliding weakens as much.

No force is made up: it is a force that is missing, and the rover no longer gets the one that should hold it back.

**It does not hold steadily.** The physics moves in small steps, fifty a second. The end-stop only exists at the steps
that start with the wheel *already* past the end of its travel, and it puts the wheel back, in one step, exactly at the
end of its travel. At the next step, the wheel is no longer past it: there is no end-stop, and the vessel falls for a
step; at the step after, the end-stop is back and pushes twice the weight. The vessel shakes on its end-stops, too fast
to be seen, and at each beat the tyres slip a little.

## Why KSP suffers from it

VPP is written for a car made of one rigid body, which carries all its wheels. KSP does otherwise: each wheel is a VPP
vehicle of its own, on its own part, joined to the others. The end-stop of a wheel only holds its part, about a hundred
kilos for a TR-2L, and the rest of the vessel weighs on it through the joints between parts; none of them sees the
whole vessel.

## What you see

**On flat ground**, all wheels on their end-stops, the tyre hardly holds anything sideways any more. The rover keeps
its heading but slides sideways as it drives, as on ice: on the navball, the velocity marker drifts away from the
heading.

<ROVER SLIDING ON FLAT GROUND>

**On a slope**, even with the brakes on and the tyres below their limit, the rover goes down. It does not pick up
speed: the tyre only holds back the missing share by sliding, and holds back all the more as it slides faster. The
rover finds the speed where the two balance, and keeps it. The shaking of the end-stops adds to it: at each beat, the
tyres slip, and the rover goes down a little more.

<ROVER SLIDING DOWN A SLOPE WITHOUT SPEEDING UP>

## Seeing it

[KSP Diag - Suspension](https://github.com/lhervier/KSP-Diag-Suspension) shows, wheel by wheel, how far the
suspension has sunk, the end-stop, the spring force and the grip VPP draws from it, and for the vessel, the share of
its weight the springs carry, its sideways speed and the gap between its velocity and its heading. Any rover shows
it, once its springs are soft enough for its wheels to rest on their end-stops; two ways to get there:

- **With the game's sliders**, without this mod, or with `fixedTuning = false` in its `settings.cfg` file, which gives
  them back, and the *Advanced Tweakables* option: in a wheel's window, switch *Spring/Damper* to *Override*, then set
  *Spring Strength* to its minimum. Do the same on every wheel.
- **With this mod's suspension window**, from the toolbar: tick *typed* next to *Mass*, type a mass much smaller than
  the rover's, and press *Auto*: the springs are set for that mass, too soft for the rover.

Then drive on flat ground.

All wheels are on their end-stops, the share carried by the springs falls close to zero, and each tyre's grip to its
floor: the rover slides sideways.

## In the code

In `VehiclePhysics.VehicleBase`, the contact force `hit.force` is the one Unity's `WheelCollider` applied at the last
step:

```csharp
wheel.grounded = wheelCol.GetGroundHit(out wheel.hit);
```

The tyre's load, which its grip comes from, is drawn from it, with a floor (`CalculateDownforce`):

```csharp
wheel.downforce = wheel.hit.force + wheel.wheelCol.runtimeExtraDownforce;
...
if (wheel.downforce < minimumdownForce)
{
    wheel.downforce = minimumdownForce;
}
```

So are the holding on the slope and the carried mass (`CalculateLocalFrame`):

```csharp
Vector3 verticalForce = gravity.Up * wheel.hit.force / upDotNormal;
wheel.surfaceForce = verticalForce - Vector3.Project(verticalForce, wheel.hit.normal);
...
float supportedMass = (wheel.hit.force + wheel.wheelCol.runtimeExtraDownforce) * (1f / gravity.Magnitude);
...
Vector2 impulseForce = (0f - tireImpulseRatio) * supportedMass * wheel.localWheelVelocity / Time.deltaTime;
wheel.externalTireForce = impulseForce + wheel.localSurfaceForce;
```

The end-stop is the one of PhysX's vehicle SDK, which Unity's `WheelCollider` is built on; the source of PhysX 4.1,
the version of Unity 2019, is public. Before each step, a ray gives how far the wheel has sunk (`dx`); the end-stop is
only switched on when that goes past the travel, and its error is the overshoot
([PxVehicleUpdate.cpp](https://github.com/NVIDIAGameWorks/PhysX/blob/4.1/physx/source/physxvehicle/src/PxVehicleUpdate.cpp),
`processSuspTireWheels`):

```cpp
suspLimitErrors[i] = (w.dot(hitNorm))*(-dx + susp.mMaxCompression);
suspLimitActiveFlags[i] = (dx > susp.mMaxCompression);
```

The solver then asks for the whole error to be corrected within the step
([DyConstraintSetup.cpp](https://github.com/NVIDIAGameWorks/PhysX/blob/4.1/physx/source/lowleveldynamics/src/DyConstraintSetup.cpp)
and [DySolverConstraint1D.h](https://github.com/NVIDIAGameWorks/PhysX/blob/4.1/physx/source/lowleveldynamics/src/DySolverConstraint1D.h)):

```cpp
const PxReal erp = 1.0f;
...
constant = recipResponse * (c.velocityTarget - geomError*recipdt);
```

The wheel starts the next step exactly at the end of its travel, not past it: no end-stop for that step.
