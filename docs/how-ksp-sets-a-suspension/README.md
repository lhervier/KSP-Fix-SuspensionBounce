# How KSP sets a suspension

Part of [Suspension Bounce Fix](../../README.md): how KSP 1.12.5 sets the spring and the damper of a suspension, for
landing legs, wheels and landing gear alike. These pages describe the stock game: a simple base model, then the
patches it got for the cases where it was not enough.

## The base model

In KSP, a suspension is a spring and a damper, which KSP sets on its own by four rules. The suspension measures how far
it has sunk with a ray cast along its axis: it works on any surface. The spring follows the weight, shared equally
between the wheels on the ground: every vessel sinks about as far. The damper follows the spring, and so only grows as
the square root of the mass: right for a leg carrying about a ton, softer above that without harm, too stiff below.
Last, the setting only happens on contact: in the air, the wheel keeps its old settings.

**→ Full chapter: [The base model](the-base-model.md)**

## The patches

KSP got several corrections for the cases where this model was not enough, each with its side effects:

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
- **The gravity curve**: the boost with compression grows at a pace set by a curve of the body's gravity: the whole
  boost on Kerbin, a tenth of it on the Mun, none on Minmus.

**→ Full chapter: [The patches](the-patches.md)**
