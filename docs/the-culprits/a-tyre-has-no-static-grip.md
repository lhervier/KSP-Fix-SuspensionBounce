# Sixième coupable : le pneu n'a pas d'adhérence statique

Fait partie de [Suspension Bounce Fix](../../README.md), [les coupables](../../README.md#the-culprits) : un coupable qui
fait tourner les vaisseaux. **Le correctif est désactivé par défaut** (réglage `staticGrip`) : il fait osciller
certains vaisseaux légers (voir [Régression](#régression)).

Un vrai pneu à l'arrêt tient sans glisser, jusqu'à sa limite d'adhérence. Celui de VPP ne tient qu'en glissant : il n'a
pas de mémoire du chemin parcouru.

## Comment VPP retient un pneu lent

Sous 1 m/s environ au contact, VPP retient le pneu par deux forces, ajoutées à celles du frottement :

- **la force de surface** : la part du poids porté par la roue qui la ferait descendre la pente, poussée vers le haut de
  la pente, pleine sous 0,5 m/s et effacée à 1 m/s ;
- **la force d'arrêt** : la moitié de la vitesse du contact, multipliée par la masse portée, annulée en un pas ; un frein
  proportionnel au glissement.

La force de surface tient la pente que voit la roue. Tout ce qu'elle ne compte pas n'est tenu que par la force d'arrêt :
les sols inégaux, où chaque roue voit sa propre pente et où les roues se poussent les unes les autres ; un vaisseau qui
pèse plus sur une roue que ne le dit sa suspension ; une pièce qui pousse. Or la force d'arrêt est nulle sans
glissement : pour retenir une force qui dure, le pneu doit glisser, à vitesse constante, et d'autant plus vite que la
force est grande. Il n'y a pas d'équilibre à l'arrêt.

## Pourquoi KSP en souffre

Le défaut est celui de VPP, pas celui de KSP. Mais KSP pose ses rovers pour des heures sur le sol irrégulier des corps
célestes, freins lâchés ou non, là où le moindre glissement constant finit par se voir.

## Ce qu'on voit

Freins lâchés, roues droites, un rover qui roule dans un creux remonte l'autre bord, s'arrête, et repart en arrière.
Au moment où il s'arrête, il pivote sur lui-même, d'environ un degré par seconde, alors que son centre n'a presque plus
de vitesse.

<ROVER QUI PIVOTE EN S'ARRÊTANT DANS UN CREUX>

Mesuré sur Eve, sur un rover de 10 t : 8,6° en 25 s, dont 2,1° pendant les deux secondes où il passe par l'arrêt (4 cm/s
au plus bas). Ensuite, en roulant à 1,5 m/s, il ne tourne plus que de 0,015° par seconde. Un virage naturel tourne
d'autant plus vite que le vaisseau roule vite ; celui-ci tourne surtout à l'arrêt. Ses roues droites ripent de côté, en
sens opposés à l'avant et à l'arrière, et le pneu ne s'y oppose qu'en proportion de cette vitesse.

## Le voir

<SAUVEGARDE À AJOUTER DANS diag/ : LE ROVER D'EVE DANS SON CREUX>

1. Charger la sauvegarde, desserrer Parking Brake, puis lâcher les freins.
2. Suivre le cap du rover pendant 25 s.

## Dans le code

Dans `VehiclePhysics.VehicleBase.CalculateLocalFrame`, la force de surface, effacée entre 0,5 et 1 m/s, puis la force
d'arrêt, proportionnelle à la vitesse du contact (`tireImpulseRatio` vaut 0,5) :

```csharp
float surfaceForceWeight = Mathf.InverseLerp(1f, 0.25f, wheel.wheelVelocity.sqrMagnitude);
...
Vector3 verticalForce = gravity.Up * wheel.hit.force / upDotNormal;
wheel.surfaceForce = verticalForce - Vector3.Project(verticalForce, wheel.hit.normal);
...
wheel.localSurfaceForce *= surfaceForceWeight;
...
float supportedMass = (wheel.hit.force + wheel.wheelCol.runtimeExtraDownforce) * (1f / gravity.Magnitude);
...
Vector2 impulseForce = (0f - tireImpulseRatio) * supportedMass * wheel.localWheelVelocity / Time.deltaTime;
wheel.externalTireForce = impulseForce + wheel.localSurfaceForce;
```

Aucun terme ne retient le chemin parcouru : à vitesse nulle, la force d'arrêt est nulle.

## Ce que change le mod

Avec le réglage `staticGrip = true` du fichier `settings.cfg`, le mod donne au pneu la mémoire qui lui manque : un
ressort par roue, vers un point d'ancrage. **Il est désactivé par défaut**, à cause de la [régression](#régression)
décrite plus bas ; les pages *Checked* sont jouées avec les réglages par défaut, donc sans lui.

### L'ancre

Quand le contact d'une roue s'arrête presque, le mod pose une ancre à l'endroit où il est. Tant qu'il reste presque au
repos, il additionne le chemin que le contact parcourt depuis l'ancre, et ajoute à la force que VPP demande au pneu un
ressort qui le ramène vers elle : la masse portée par la roue, divisée par le carré de 0,15 s. Le ressort tient de côté,
toujours ; le long de la roue, seulement quand elle freine, puisqu'une roue libre doit pouvoir rouler.

« Presque au repos » : un glissement de moins de 5 cm/s, l'ancre pleine ; au-delà, elle s'efface, et disparaît à
10 cm/s. Pour une roue libre, seul compte le glissement de côté, pas le roulement. Comme la force de surface de VPP,
l'ancre s'efface aussi quand le contact dépasse 0,5 m/s, et disparaît à 1 m/s. Un contact qui glisse vite est laissé à
VPP, comme sans le mod : un atterrissage, un dérapage, un virage ne sont pas des forces qui durent.

### Le pneu reste le juge

L'ancre n'a pas de limite à elle : c'est l'adhérence du pneu qui borne ce qu'il transmet. Quand le pneu ne transmet pas
toute la force que demande l'ancre, le mod rapproche l'ancre pour qu'elle ne demande que ce que le pneu a transmis : le
pneu dérape, et se raccroche là où il s'arrête. Une roue de réaction plus forte que l'adhérence fait donc toujours
tourner le rover, comme sans le mod. Un écart de moins de 2 % entre la force demandée et celle transmise n'est que
l'arrondi du calcul du pneu de VPP : l'ancre n'est pas déplacée pour si peu, sinon elle ne ferait que raccourcir, et le
vaisseau glisserait.

### Il lui faut le cinquième coupable corrigé

Le chemin parcouru se lit dans la vitesse du contact que lit VPP. Avec la vitesse fantôme du
[cinquième coupable](a-tyre-fights-a-slide-that-does-not-exist.md), chaque ancre additionnerait celle de sa roue, un
glissement qui n'existe pas : les roues tireraient les unes contre les autres, jusqu'à épuiser l'adhérence de leurs
pneus. Mesuré avec la vitesse d'Unity : un rover garé sur la Mune glisse de 7,4 mm/s ; la grue de la Mune, de côté, de
0,25 mm/s. C'est pourquoi l'ancre n'est installée qu'avec `tyreReadsPoses = true`, le correctif du cinquième coupable.

### Ce qu'on obtient

Le rover d'Eve, freins lâchés, ne tourne plus que de 0,5° en 25 s au lieu de 8,6°, et ne pivote plus en s'arrêtant.
Garés freins serrés, Parking Brake desserré, les rovers lourds ne bougent plus du tout : en une minute, celui d'Eve
tournait encore de 0,12° avec le seul cinquième coupable corrigé, il ne tourne plus ; celui de la Mune dérive de
0,0004 mm/s au lieu de 0,004. La grue, dont le ravitailleur se balance, se pose comme sans l'ancre, un peu plus
lentement : 0,002 mm/s après une minute et demie. Au volant, rien ne change.

## Régression

Avant un ajustement qu'il fait lui-même (plus bas), ce correctif faisait osciller une sonde : une sonde de 100 kg
posée sur trois jambes LT-05, immobile au départ, se mettait à tourner sur elle-même, d'un côté puis de l'autre, de
plus en plus fort : la vitesse de rotation double toutes les 4 s environ, jusqu'à ±10°/s, ±5° de
part et d'autre, une oscillation toutes les 3 s, qui ne s'arrêtait plus. Sans l'ancre, la même oscillation s'éteint
en 5 s.

<SONDE QUI OSCILLE SUR ELLE-MÊME>

La cause est dans VPP. Avant d'appliquer la force du pneu, VPP la lisse : à chaque pas, la force appliquée ne fait
qu'une part du chemin vers celle que calcule le pneu, `tireForceSharpness` fois la durée du pas. La force de l'ancre,
ajoutée à celle que VPP demande au pneu, passe par ce lissage : elle arrive en retard. Un ressort en retard pousse
encore quand le vaisseau revient déjà vers lui : il lui rend de l'énergie à chaque oscillation, au lieu de lui en
prendre. Sur un vaisseau lourd, l'amortissement de VPP l'emporte ; sur une sonde légère aux jambes écartées, non.

La plupart des roues de KSP ont `tireForceSharpness = 10` (la valeur par défaut de `ModuleWheelBase`), soit environ
0,1 s de retard ; la jambe d'atterrissage miniature LT-05 a 1 dans son `.cfg`, soit environ une seconde. Assoupli onze
fois (0,5 s au lieu de 0,15 s), le ressort ne faisait plus osciller la sonde, mais tenait d'autant moins.

Le correctif, quand il est activé, donne donc à la LT-05 le lissage de toutes les autres roues de KSP, 10 : la sonde
reste immobile, ancre active (150 s, rotation sous 0,002°/s). Le réglage désactivé, la LT-05 garde son lissage de KSP.
Le risque demeure pour les roues et les jambes que les mods ajoutent : chacune a ses réglages, et la marge dépend
aussi de la masse et de l'écartement des roues. Aucune raideur n'est sûre pour toutes. D'où le réglage désactivé par
défaut.

Dans `VehiclePhysics.VehicleBase.ApplyTireForce`, le lissage, que KSP active pour toutes les roues
(`ModuleWheelBase`) :

```csharp
if (tireForceSmoothing)
{
    wheel.tireForce.x = Mathf.Lerp(wheel.lastTireForce.x, wheel.tireForce.x, tireForceSharpness * Time.deltaTime);
    wheel.tireForce.y = Mathf.Lerp(wheel.lastTireForce.y, wheel.tireForce.y, tireForceSharpness * Time.deltaTime);
}
```

## Dans le code du mod

Tout est dans [Src/TyreStaticGrip.cs](../../Src/TyreStaticGrip.cs).

Un postfixe de `VehicleBase.CalculateLocalFrame`, une fois que VPP a calculé la force qu'il demande au pneu, avance
l'ancre et y ajoute son ressort :

```csharp
Vector2 slide = wheel.localWheelVelocity;
bool braking = HoldWhenBraking && wheel.brakeTorque > 0f;
float sliding = braking ? slide.magnitude : Mathf.Abs(slide.x);
float fade = Mathf.InverseLerp(ReleaseSpeed, HoldSpeed, sliding) *
    Mathf.InverseLerp(1f, 0.25f, wheel.wheelVelocity.sqrMagnitude);
...
float stiffness = wheel.hit.force / gravity / (SpringTime * SpringTime);
anchor.offset.x += slide.x * Time.deltaTime;
...
anchor.gain = stiffness * fade;
anchor.force = -anchor.gain * anchor.offset;
wheel.externalTireForce += anchor.force;
```

Un préfixe de `VehicleBase.ApplyTireForce`, une fois que le pneu a calculé ce qu'il transmet (`tireForce`) de ce qu'on
lui demandait (`externalTireForce`), rapproche l'ancre quand il en manque plus que l'arrondi (`Reseat`) :

```csharp
float anchorPassed = passed - (asked - anchorForce);
float sign = Mathf.Sign(anchorForce);
float shortfall = Mathf.Abs(anchorForce) - anchorPassed * sign;
if (shortfall <= Mathf.Max(ReseatTolerance, ReseatShare * Mathf.Abs(asked)))
{
    return offset;
}
moved = true;
return anchorPassed * sign <= 0f ? 0f : -anchorPassed / gain;
```

Un préfixe de `ModuleWheelBase.wheelSetup`, où KSP transmet à VPP les réglages de la roue, donne à la LT-05
(`miniLandingLeg`) le lissage des autres roues. `tireForceSharpness` se lit dans le `.cfg` de la pièce et ne s'enregistre
jamais : le changement dure autant que la pièce, et seulement quand le correctif est installé.

```csharp
AvailablePart info = __instance.part != null ? __instance.part.partInfo : null;
if (info != null && info.name == MiniLeg && __instance.tireForceSharpness < MiniLegSharpness)
{
    __instance.tireForceSharpness = MiniLegSharpness;
}
```
