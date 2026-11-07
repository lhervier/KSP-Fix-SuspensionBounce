# Third culprit: a bottomed wheel hides its weight from the tyres

Part of [Suspension Bounce Fix](../../README.md), [the culprits](../../README.md#the-culprits): a culprit that makes
vessels slide.

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
end-stop on a heavy rover on the Mun, under the low wheel of a rover standing across a slope, under the front wheels
when braking, or when the player sets their springs very soft. But the end-stop of the physics has two faults.

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
its weight the springs carry, its sideways speed and the gap between its velocity and its heading. On any rover, in
game, with the *Advanced Tweakables* option:

1. In a wheel's window, switch *Spring/Damper* to *Override*, then set *Spring Strength* to its minimum. Do the same
   on every wheel.
2. Drive on flat ground.

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

## What the mod changes

The mod holds the end-stops of the wheels on the ground itself, and tells the tyres what it holds. A single mechanism
answers both faults: the force is its own, it no longer has to look for it.

### The vessel taken as one body

At each step, the mod makes the vessel the rigid body VPP expects: its mass, its centre of mass, its inertia (that of
each part, brought back to the centre of mass), its velocity and its rotation, drawn from the momentum and the angular
momentum of all its parts. To that it adds the forces known for the step: gravity, as KSP applies it to each part; the
forces and torques of KSP's modules (engines, reaction wheels…); the spring of each wheel; the force of each tyre, as
VPP applied it.

### One step ahead

For each wheel on the ground, the mod knows the travel it has left before its end-stop. It looks for the smallest
forces, never pulling, such that by the end of the coming step no wheel has gone past the end of its travel. The
wheels are handled together: a force on one wheel also tilts the vessel, and changes what the others have to hold. A
wheel resting on its end-stop gets its share of the load, beyond what its spring carries; a wheel leaving it, or still
far from it, gets nothing; a wheel coming fast is slowed just enough to reach it. These forces are applied to the
wheels' parts, at the contact point, along the axis of the suspension, as the end-stop they replace: PhysX's end-stop
has nothing left to correct, and no longer beats. The axis matters at speed: measured along the normal of the ground
instead, a normal tilted for a single step, by a bump or a seam between two pieces of terrain, would turn part of the
forward speed into a speed towards the end-stop, and call for a force many times the weight.

### Sharing between the wheels

Four wheels for three equations (one force, two torques): there are endless ways to carry the same vessel. The mod
takes the evenest. A sharing left free to drift from one step to the next changes neither the weight carried nor the
torques, but ends up putting the whole vessel on two wheels across a diagonal: the other two tyres have no load left,
and the rover slides. The mod works it out in passes: the first finds the evenest sharing, the next ones keep that
sharing and make up what the first left short.

### What the tyres see

The force the mod applies to a wheel is added, as it is, to the one VPP reads for it, at the same step: grip, holding
on the slope and the pull back against sliding get the whole load back.

### When it works

The mod only follows a vessel while one of its wheels could reach its end-stop during the coming step: while the travel
the wheel has left is no more than what it would cover in a step, at its speed towards the end-stop plus a step of
free fall. The rest of the time, it only reads the wheels' contacts.

### Checking it

What PhysX's end-stop still pushes is read through a balance of the whole vessel: what its momentum and its angular
momentum gain or lose over a step, minus all the known forces and the mod's own. The joints between parts cancel out
in it: a joint pushes one part as much as it pulls the other. The same balance on the wheel's part alone would need the
forces of its joints, which Unity gives in principle through `Joint.currentForce`; but on the joints between KSP parts,
`Joint.currentForce` is always zero, even under a load of several tons, a Unity bug known for years
([Joint.currentForce and Joint.currentTorque are always
zero](https://discussions.unity.com/t/joint-currentforce-and-joint-currenttorque-are-always-zero/675232)). While the
mod holds the end-stops, what is left is close to zero.

### The rest

A wheel that lands hard enough to cross its whole travel in a single step reaches the end of it before the mod could
slow it: PhysX's end-stop catches it, as without the mod, and the mod holds the wheel from the next step on. The wheel
damage module, which also reads the tyre's load to spot impacts in it, keeps the one it read without the mod.

The `bumpStopLoad` setting of the `settings.cfg` file turns this part of the mod off. The rover resting on its
end-stops, on flat ground and on a slope, with and without it:
[A rover on its end-stops](../checked/a-rover-on-its-end-stops.md).

## In the mod's code

Everything is in [Src/BumpStopLoad.cs](../../Src/BumpStopLoad.cs).

What KSP gives each part for the coming step is recorded in a prefix of `FlightIntegrator.Integrate(Part)`
(`IntegratePrefix`): the vessel's acceleration (`vessel.precalc.integrationAccel`, gravity included), and the forces
and torques its modules gathered (`part.force`, `part.forces`, `part.torque`). The force VPP has just applied to each
tyre is recorded in a postfix of `VehicleBase.ApplyTireForce` (`ApplyTireForcePostfix`).

The work is done in a postfix of `VehicleBase.CalculateSuspensionForces`, which VPP calls at each step for each wheel,
once the spring force is read and before the tyre's load is drawn from it; only once per vessel and per step, at its
first wheel (`Solve`). It first reads the contact of each wheel, and follows the vessel only if one of them could reach
its end-stop during the step:

```csharp
float closing = -Vector3.Dot(p.body.GetPointVelocity(p.hit.point), p.axis);
near = (1f - p.compression) * travel <= Mathf.Max(0f, closing + fall) * dt;
```

The vessel as one body (`HoldEndStops`): the momentum, the angular momentum and the inertia of all its parts, about
its centre of mass:

```csharp
momentum += p;
angularMomentum += b.spin + Vector3.Cross(offset, p);
inertia.AddPrincipal(b.frame, b.inertia);
inertia.AddPoint(b.mass, offset);
...
Vector3 velocity = momentum / mass;
Vector3 spin = inverse.Times(angularMomentum);
Vector3 acceleration = known / mass;
Vector3 angularAcceleration = inverse.Times(knownTorque);
```

For each wheel on the ground, along the axis of its suspension (`normal` is that axis, read with the contact), the
speed at which its contact would move away from its end-stop at the end of the step without the mod's force, plus the
speed that would just close the travel it has left; the mod's forces must not bring it below zero:

```csharp
float gap = Mathf.Max(0f, (1f - p.compression) * travel);
float separating = Vector3.Dot(normal, velocity + Vector3.Cross(spin, arm));
rhs[k] = separating + dt * (Vector3.Dot(normal, acceleration) + Vector3.Dot(holdArm[k], angularAcceleration)) +
    gap / dt;
```

How a force on one wheel moves the contact of another, through the mass and the inertia of the vessel, is
`a[k * n + j]`. The forces are found by projected Gauss-Seidel, none negative, in passes (`Sweep`): the first pulls
them towards zero, the next ones towards the answer of the pass before:

```csharp
float s = -rhs[k] / dt + lambda * target[k];
for (int j = 0; j < n; j++)
{
    if (j != k)
    {
        s -= a[k * n + j] * f[j];
    }
}
float value = s / (a[k * n + k] + lambda);
value = float.IsNaN(value) || value < 0f ? 0f : value;
```

Each force is applied to its wheel's part, at the contact point:

```csharp
p.heldForce = p.axis * f[k];
p.heldPoint = p.hit.point;
if (f[k] > 0f && p.body != null)
{
    p.body.AddForceAtPosition(p.heldForce, p.heldPoint, ForceMode.Force);
}
```

and added to the force VPP is about to read for that wheel:

```csharp
probe.injected = probe.held;
wheel.hit.force += probe.injected;
```

A postfix of `VehicleBase.CalculateDownforce` keeps the tyre's load as it would have been without the mod, and a
prefix and a postfix of `ModuleWheelDamage.FixedUpdate` show it to the damage module while it updates.
