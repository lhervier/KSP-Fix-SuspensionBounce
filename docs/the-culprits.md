# The culprits

Part of [Suspension Bounce Fix](../README.md). How KSP 1.12.5 sets the spring and the damper of a suspension: a simple
base model, the patches it got for the cases where it was not enough, then what those patches leave unanswered, and
what throws a light vessel, or a part placed alone, into the air.

## The base model

In KSP, a suspension is a spring and a damper, which KSP sets on its own by four rules. The suspension measures how far
it has sunk with a ray cast along its axis: it works on any surface. The spring follows the weight, shared equally
between the wheels on the ground: every vessel sinks about as far. The damper follows the spring, and so only grows as
the square root of the mass: right for a leg carrying about a ton, softer above that without harm, too stiff below.
Last, the setting only happens on contact: in the air, the wheel keeps its old settings.

**→ Full chapter: [The base model](the-culprits/the-base-model.md)**

## The patches

KSP got several corrections for the cases where this model was not enough. Each has its side effects, and several
make the culprits below:

- **The half-ton floor**: each wheel carries at least the weight of half a ton on Kerbin, so that a very small spring
  is not rounded down to zero; a light vessel then gets the damper of half a ton.
- **Rounding and the write threshold**: the spring is only written when it changes by more than 3, probably against a
  loop between the spring and the compression; a small spring stays at zero, hence the floor.
- **The boost with compression**: the spring stiffens up to three times near the end of its travel, and the damper
  with it, at the moment of impact.
- **The big-vessel correction**: above about forty tons, the damper stiffens up to nearly four times, from the mass of
  the whole vessel, not from what each leg carries.
- **The automatic sliders**: set at the first contact, the damper up to 2 on Eve.
- **High gravity**: above 1.5 g, so on Eve, the spring is tripled and the damper multiplied by 1.2.

**→ Full chapter: [The patches](the-culprits/the-patches.md)**

## What stock leaves unsolved: the culprits

**Why a damper can throw a vessel.** In real life, a damper that is too strong throws nothing: it only makes the leg
slow to settle. But KSP works out physics in small steps, fifty a second. At each step, the damper slows the leg
according to the speed it had at the previous step. On a loaded leg, this braking takes off a little speed at each
step. On a leg that carries almost nothing, it is so strong that in a single step it does more than stop it: it sends
it back the other way, faster than it came. The next step does it again, and in a few steps the vessel leaves at tens
of metres per second. Both culprits lead there.

### First culprit: a damper too strong for a light vessel

Because of the floor, a leg carrying 50 kg gets the damper of a leg carrying half a ton, far too strong for it: it
throws it. A heavy world makes it worse: a small rover that stands still on Kerbin bounces on and on on Eve. The mod
brings the damper down under a limit.

**→ Full chapter: [First culprit: a damper too strong for a light vessel](the-culprits/a-damper-too-strong-for-a-light-vessel.md)**

### Second culprit: a suspension in the air keeps its old settings

In the air, a wheel keeps the spring and the damper of its last contact. A part placed alone in EVA construction,
which KSP makes weigh up to 20 t for its first seconds, thus keeps a suspension set for 20 t, which throws it when it
touches the ground again at its own mass. The mod brings the damper down under the limit as soon as the wheel is in
the air.

**→ Full chapter: [Second culprit: a suspension in the air keeps its old settings](the-culprits/a-suspension-in-the-air-keeps-its-settings.md)**
