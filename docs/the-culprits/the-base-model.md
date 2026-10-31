# Le modèle de base

Partie de [Suspension Bounce Fix](../../README.md), [les coupables](../the-culprits.md) : comment KSP 1.12.5 règle
le ressort et l'amortisseur d'une suspension, avant toute rustine.

Dans KSP, une suspension, c'est un ressort et un amortisseur. La raideur du ressort décide de combien la jambe
s'enfonce sous le poids du vaisseau. L'amortisseur ralentit les mouvements de la jambe, pour qu'elle ne rebondisse pas
sans fin. KSP les fait fonctionner tout seul, selon quatre règles simples.

**La suspension mesure son enfoncement le long de son axe**, comme un bâton tendu devant soi : un rayon part de la
roue dans l'axe de la jambe, et touche ce qu'il rencontre. L'enfoncement est la distance jusqu'à ce point.
Conséquences :

- **Elle marche sur toute surface** : le sol, une piste, un bâtiment, le flanc d'un autre vaisseau.
- **Elle pousse contre ce qu'elle touche**, même quand ce n'est pas le sol : une jambe couchée contre un atterrisseur
  pousse contre lui.
- **De biais, la mesure devient instable.** Quand la pièce penche ou se couche, le rayon rencontre la surface de biais :
  le moindre mouvement de la pièce déplace alors loin le point touché, et l'enfoncement lu saute d'un pas à l'autre
  (voir [le troisième coupable](a-suspension-pushing-at-a-slant.md)).

![Le rayon de la suspension pour une jambe debout, contre un autre vaisseau, et couchée](../../imgs/suspension-ray.svg)

*Le rayon de la suspension (en rouge) dans trois positions de la jambe. Couchée, le rayon en clair est celui de la même
jambe, tournée de quelques degrés.*

**Le ressort suit le poids.** KSP partage le poids du vaisseau à parts égales entre les roues qui touchent le sol, et
donne à chacune un ressort à la mesure de sa part. Plus une jambe porte, plus son ressort est raide. Conséquences :

- **Le même enfoncement pour tous.** Un vaisseau léger ou lourd, sur Kerbin ou sur la Mune, s'enfonce à peu près
  autant sur ses jambes, et le joueur n'a rien à régler.
- **Le côté chargé s'affaisse.** Toutes les jambes reçoivent le même ressort, celui de la part moyenne, même quand
  elles ne portent pas autant : en pente, les jambes aval portent plus que leur part et s'enfoncent davantage, comme
  sur une vraie voiture, dont les ressorts sont identiques.
- **Un tout petit poids donne un tout petit ressort**, si petit que KSP finit par l'arrondir à zéro : la jambe
  s'écrase contre sa butée. D'où [le plancher](the-patches.md#le-plancher-dune-demi-tonne).
- **La part change quand une roue touche ou quitte le sol.** Quand un rover se pose, la première roue qui touche reçoit
  un moment la part du vaisseau entier, puis sa part diminue à mesure que les autres touchent : son ressort change
  d'un pas à l'autre.

**L'amortisseur suit le ressort.** Il est calculé à partir du ressort seul, avec un coefficient propre à chaque pièce :
un ressort raide reçoit un amortisseur fort. Conséquences :

- **Rien d'autre à connaître.** L'amortisseur se règle de lui-même chaque fois que le ressort change.
- **La masse n'y entre qu'à moitié.** Elle y entre à travers le ressort, mais pas assez : pour amortir pareil quelle
  que soit la masse, l'amortisseur devrait croître comme la masse, il ne croît que comme sa racine. Il convient à une
  jambe qui porte de l'ordre d'une tonne. Pour une jambe qui porte plusieurs tonnes, il est plus mou, sans que ça
  gêne : un vaisseau de 20 t sur quatre jambes se pose en deux oscillations, en deux secondes sur Kerbin, cinq sur la
  Mune. Pour une jambe qui porte beaucoup moins, il est trop dur ([premier coupable](a-damper-too-strong-for-a-light-vessel.md)).

**Le réglage n'a lieu qu'au contact.** KSP ne recalcule le ressort et l'amortisseur que pendant que la roue touche
quelque chose ; en l'air, la roue garde ceux de son dernier contact. Conséquences :

- **Rien à calculer en l'air**, où la jambe ne porte rien et reste détendue à fond, quel que soit son ressort.
- **Au contact suivant, la roue pousse d'abord avec ses anciens réglages**, avant que KSP ne les recalcule. Sans
  conséquence si le vaisseau a gardé sa masse entre-temps ; sinon, voir
  [le deuxième coupable](a-suspension-in-the-air-keeps-its-settings.md).

<details>
<summary>Dans le code</summary>

`SuspensionLoadBalancer.FixedUpdate` (un module de vaisseau) donne à chaque suspension le poids qu'elle porte,
`sprungMassGravity` : la masse du vaisseau fois la gravité de surface en g, arrondie au dixième, partagée à parts
égales entre les roues au sol :

```csharp
num = Math.Round(num * vessel.gravityMultiplier * vessel.mainBody.GeeASL * 10.0) / 10.0;
...
share = Mathf.Round(100f / divisor) / 100f;
suspensionModules[j].sprungMassGravity = Mathf.Max((float)(num * (double)share * (double)multiplier), minMass);
```

`ModuleWheelSuspension.SuspensionSpringUpdate` transforme ce poids en ressort, puis le ressort en amortisseur
(`springRatio` et `damperRatio` sont propres à chaque pièce) :

```csharp
spring = Mathf.Clamp(springTweakable * springRatio * sprungMassGravity * BoostCurve(boost, boostRatio + autoBoost), 0.01f, springClampMax);
damper = Mathf.Sqrt(Mathf.Clamp(spring * damperRatio * damperFudge, 0.01f, damperClampMax)) * damperTweakable;
```

`ModuleWheelSuspension.FixedUpdate` ne l'appelle que pendant que la roue touche quelque chose :

```csharp
if (HighLogic.LoadedSceneIsFlight && baseSetup && !base.part.packed && wheel != null && wheel.IsGrounded)
{
    vesselMass = (float)base.vessel.totalMass;
    SuspensionSpringUpdate(vesselMass);
}
```

</details>
