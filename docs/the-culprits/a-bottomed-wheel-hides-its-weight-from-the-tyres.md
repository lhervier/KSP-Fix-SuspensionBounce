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
when braking, or when the player sets their springs very soft.

But the physics applies the force of the end-stop without reporting it: the contact force VPP reads only holds the
spring's. For VPP, a bottomed wheel carries what its spring pushes, not what it really carries. The share that goes
through the end-stop vanishes from its three calculations:

- grip drops, down to the floor VPP keeps for each tyre;
- holding on the slope only makes up for part of the slope;
- the pull back against sliding weakens as much.

No force is made up: it is a force that is missing, and the rover no longer gets the one that should hold it back.

## What you see

**On flat ground**, all wheels on their end-stops, the tyre hardly holds anything sideways any more. The rover keeps
its heading but slides sideways as it drives, as on ice: on the navball, the velocity marker drifts away from the
heading.

<ROVER SLIDING ON FLAT GROUND>

**On a slope**, even with the brakes on and the tyres below their limit, the rover goes down. It does not pick up
speed: the tyre only holds back the missing share by sliding, and holds back all the more as it slides faster. The
rover finds the speed where the two balance, and keeps it.

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

## What the mod changes

The mod does not touch the end-stop: it finds its force, and gives it to VPP. All the work is in finding it, since
neither PhysX nor Unity gives it.

### A force nobody reports

The end-stop of a `WheelCollider` is a constraint PhysX solves along with the rest of the physics, like a contact.
Unity reports it nowhere: `WheelHit.force`, the only force the `WheelCollider` gives, is the spring and damper's.
There is nothing else to read.

### The direct way, closed by Unity

The simplest idea is a balance on the wheel's part alone. The physics says, at each step, how much its velocity
changed; its mass, the gravity, the spring and the tyre force are known. What would be missing are the forces of the
joints that hold it to the rest of the vessel: Unity gives them in principle through `Joint.currentForce`. But on the
joints between KSP parts, `Joint.currentForce` is always zero, even under a load of several tons: a Unity bug known
for years ([Joint.currentForce and Joint.currentTorque are always
zero](https://discussions.unity.com/t/joint-currentforce-and-joint-currenttorque-are-always-zero/675232)). Without
these forces, the balance of the part means nothing.

### The balance of the whole vessel

So the mod does the balance on the whole vessel, where these forces no longer need to be known: a joint pushes one
part as much as it pulls the other, and in the sum over all parts, its two forces cancel out.

At each step, the mod records for each part the change in its velocity and its rotation: the sum, over the whole
vessel, of what its momentum and its angular momentum gain or lose is the sum of all the outside forces received
during the step, and of their torques. The mod takes out all those it knows:

- gravity, as KSP applies it to each part;
- the forces and torques KSP's modules applied (engines, reaction wheels…);
- the force of each spring, along the contact normal, at its contact point;
- the force of each tyre, as VPP applied it.

What is left is what the ground gave on top of the springs: the force of the end-stops. When only one wheel is on its
end-stop, it is all its own. When several are, the mod shares it so as to explain the torques as well: a front wheel
and a rear wheel, a wheel at the top and one at the bottom of a slope, do not carry the same share. It looks for the
forces, all positive, that best explain at once the force left over and its torques about the two axes lying on the
ground.

The mod adds each wheel's share to the force VPP reads for it, just before VPP uses it. VPP then sees what the wheel
really carries: its grip, its holding on the slope and its pull back against sliding get the whole load back.

### The rest

A wheel that is not on its end-stop is left alone: VPP reads its spring, as without the mod. The end-stop does not
hold the weight in one piece but in jolts, from one step to the next (see the *Limits* of the
[README](../../README.md#limits)): the mod gives VPP its average force over a tenth of a second. The wheel damage
module, which also reads the tyre's load to spot impacts in it, keeps the one it read without the mod.

The `bumpStopLoad` setting of the `settings.cfg` file turns this part of the mod off. The rover sliding on flat ground,
and the one going down the slope with its brakes on, with and without it:
[A rover on its end-stops](../checked/a-rover-on-its-end-stops.md).

## In the mod's code

Everything is in [Src/BumpStopLoad.cs](../../Src/BumpStopLoad.cs).

What KSP gives each part for the coming step is recorded in a prefix of `FlightIntegrator.Integrate(Part)`
(`IntegratePrefix`): the vessel's acceleration (`vessel.precalc.integrationAccel`, gravity included), and the forces
and torques its modules gathered (`part.force`, `part.forces`, `part.torque`). The force VPP has just applied to each
tyre is recorded in a postfix of `VehicleBase.ApplyTireForce` (`ApplyTireForcePostfix`).

The balance is done in a postfix of `VehicleBase.CalculateSuspensionForces`, which VPP calls at each step for each
wheel, once the spring force is read and before the tyre's load is drawn from it; only once per vessel and per step,
at its first wheel (`Solve`). For each part, its change in momentum minus what KSP gave it:

```csharp
Vector3 change = body.mass * (body.velocity - state.velocity) / dt;
Vector3 known = body.mass * a.acceleration + a.force;
Vector3 excess = change - known;
force += excess;
torque += Vector3.Cross(arm, excess) + (Spin(body) - state.spin) / dt - a.torque;
```

then, for each wheel on the ground, the force of its spring, which PhysX applies along the contact normal, and that of
its tyre:

```csharp
Vector3 spring = p.hit.normal * p.springForce;
force -= spring;
torque -= Vector3.Cross(p.hit.point - centre, spring);
```

The rest, along the average normal of the ground, and its torques about the two axes lying on the ground, are shared
between the bottomed wheels (`Share`): the positive forces that best explain them, by least squares. Each wheel's
share, averaged, is added to the force VPP is about to read:

```csharp
probe.smoothed += (probe.missing - probe.smoothed) * weight;
...
wheel.hit.force += probe.injected;
```

A postfix of `VehicleBase.CalculateDownforce` keeps the tyre's load as it would have been without the mod, and a
prefix and a postfix of `ModuleWheelDamage.FixedUpdate` show it to the damage module while it updates.
