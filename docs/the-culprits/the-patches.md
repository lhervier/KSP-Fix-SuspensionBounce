# Les rustines

Partie de [Suspension Bounce Fix](../../README.md), [les coupables](../the-culprits.md) : les rustines de KSP 1.12.5
sur [le modèle de base](the-base-model.md), chacune avec ses conséquences.

KSP a reçu plusieurs corrections pour les cas où son modèle de base ne suffisait pas. Squad ne les a pas commentées : leur
raison d'être est déduite de ce qu'elles font.

## Le plancher d'une demi-tonne

Quelle que soit sa part réelle, KSP fait comme si chaque roue portait au moins le poids d'une demi-tonne sur Kerbin.
Sans doute pour ceci : sous un tout petit poids, le ressort calculé est si faible que les arrondis de
[la rustine suivante](#larrondi-et-le-seuil-décriture) le laissent à zéro, et la jambe s'écrase contre sa butée.

Conséquences :

- **Plus de ressort nul.** Un atterrisseur posé sur un astre à faible gravité, dont chaque jambe porte à peine
  quelques dizaines de kilos de poids, ne s'écrase plus sur ses butées.
- **Un vaisseau léger est porté comme une demi-tonne.** Ses jambes s'enfoncent à peine, et reçoivent l'amortisseur de
  la demi-tonne, beaucoup trop fort pour elles : c'est
  [le premier coupable](a-damper-too-strong-for-a-light-vessel.md).

<details>
<summary>Dans le code</summary>

Le `minMass` de `SuspensionLoadBalancer.FixedUpdate`, qui vaut `0.5` :

```csharp
suspensionModules[j].sprungMassGravity = Mathf.Max((float)(num * (double)share * (double)multiplier), minMass);
```

</details>

## L'arrondi et le seuil d'écriture

KSP arrondit le ressort et l'amortisseur à l'unité, et n'écrit un nouveau ressort sur la roue que s'il diffère de plus
de 3 de l'ancien. Sans doute contre une boucle : le ressort dépend de l'enfoncement
([rustine suivante](#le-renfort-à-lenfoncement)), et l'enfoncement du ressort ; recalculé à chaque pas, il pourrait
faire vibrer la suspension. Ce seuil convient à des ressorts de quelques dizaines ou centaines, pas de quelques
unités : un ressort qui devrait passer de 0 à 1 reste à 0.

Conséquences :

- **Pas de vibration** entre le ressort et l'enfoncement, si c'était bien le but.
- **Un petit ressort n'est jamais écrit** : sous 3, il reste à zéro. C'est ce qui rend
  [le plancher](#le-plancher-dune-demi-tonne) nécessaire.
- **Le ressort change par paliers**, en retard sur ce que KSP calcule, puisque le renfort à l'enfoncement le fait
  varier sans cesse.

<details>
<summary>Dans le code</summary>

La fin de `ModuleWheelSuspension.SuspensionSpringUpdate` ; `oscillationDamper` vaut `3` :

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

## Le renfort à l'enfoncement

Plus la roue s'enfonce, plus son ressort durcit, jusqu'à deux fois sa raideur de départ ; et quand elle dépasse 80 %
de sa course, il durcit encore, d'autant plus qu'elle y reste, jusqu'à trois fois. Sans doute contre la butée : une
roue qui touche le bout de sa course cogne, et rebondit. Sous forte gravité, ce renfort arrive dès les premiers
centimètres.

Conséquences :

- **Moins de butées** à l'atterrissage et sur les bosses.
- **Un ressort qui change avec l'enfoncement**, ce qui crée la boucle que combat
  [le seuil d'écriture](#larrondi-et-le-seuil-décriture). Sur Eve, sa raideur varie fortement au moindre enfoncement.
- **Un amortisseur qui durcit avec lui**, puisqu'il suit le ressort : il est le plus fort au moment du choc, là où un
  vaisseau léger a le moins de marge.

<details>
<summary>Dans le code</summary>

Dans `ModuleWheelSuspension.SuspensionSpringUpdate`, `boost` grandit avec l'enfoncement (`suspCompression`, de 0 à 1)
et avec la gravité ; `boostClamp` vaut `0.5`. `BoostCurve` en fait un multiplicateur du ressort :

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

`UpdateAutoBoost` monte `autoBoost` tant que l'enfoncement dépasse 0,8, et le redescend sous 0,4 :

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

## La correction des gros vaisseaux

Au-delà d'une quarantaine de tonnes, l'amortisseur durcit avec la masse du vaisseau et avec l'enfoncement des jambes,
jusqu'à près de quatre fois. Sans doute contre les gros vaisseaux qui oscillent sur leurs jambes, que le modèle de
base amortit moins. Elle lit la masse du vaisseau entier, pas ce que porte chaque jambe.

Conséquences :

- **Les très gros vaisseaux, peu de jambes, sont mieux amortis.**
- **Rien en dessous d'une quarantaine de tonnes**, même pour une jambe qui porte plusieurs tonnes. Sans que ça gêne :
  un vaisseau de 20 t sur quatre jambes se pose déjà en deux oscillations.

<details>
<summary>Dans le code</summary>

`damperFudge`, dans `ModuleWheelSuspension.SuspensionSpringUpdate`, passe de 1 à 15 sous la racine de l'amortisseur,
selon la masse du vaisseau entier fois l'enfoncement de la roue :

```csharp
float num = sprungMass * suspCompression;   // sprungMass = vessel.totalMass
...
damperFudge = Mathf.Lerp(1f, 15f, Mathf.InverseLerp(10f, 100f, num));
damper = Mathf.Sqrt(Mathf.Clamp(spring * damperRatio * damperFudge, 0.01f, damperClampMax)) * damperTweakable;
```

</details>

## Le réglage automatique des curseurs

Chaque suspension a deux curseurs dans la fenêtre de la pièce, « Spring » et « Damper ». Tant que le réglage
automatique est actif, comme par défaut, KSP les règle seul, une fois, au premier contact : le ressort d'autant plus
raide que la jambe est chargée près de sa charge maximale, l'amortisseur selon la gravité de l'astre, 1,1 sous 1 g,
1 sur Kerbin, puis de plus en plus fort jusqu'à 2 sur l'astre le plus lourd du système, Eve (1,7 g). Pour que le
joueur n'ait rien à régler.

Conséquences :

- **Rien à régler pour le joueur**, sur tous les astres.
- **Un amortisseur deux fois plus fort sur Eve** que sur Kerbin, pour la même masse à freiner : il aggrave
  [le premier coupable](a-damper-too-strong-for-a-light-vessel.md).

<details>
<summary>Dans le code</summary>

Dans `ModuleWheelSuspension.SuspensionSpringUpdate` ; `MaximumSurfaceGeeASL` est la plus forte gravité de surface des
astres à sol solide, celle d'Eve en stock, et `damperLerpBase` vaut `1` :

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

## La forte gravité

Au-delà de 1,5 g, donc sur Eve seulement en stock, KSP triple le ressort et multiplie l'amortisseur par 1,2. Sans
doute pour qu'un vaisseau ne s'y écrase pas sur ses jambes. Avec le curseur réglé à 2, une suspension reçoit sur Eve un
amortisseur 2 × 1,2 = 2,4 fois plus fort que sur Kerbin pour le même ressort, alors que la masse à freiner n'a pas
changé.

Conséquences :

- **Plus de marge avant la butée** sur Eve : le vaisseau s'y enfonce trois fois moins que ne le voudrait la seule
  règle du poids.
- **Un amortisseur plus fort encore**, qui s'ajoute au curseur à 2 : il aggrave
  [le premier coupable](a-damper-too-strong-for-a-light-vessel.md).
- **Un saut à 1,5 g** : sur un astre d'un pack de planètes à 1,49 g ou à 1,51 g, le même vaisseau reçoit un ressort trois
  fois différent.

<details>
<summary>Dans le code</summary>

Dans `ModuleWheelSuspension.SuspensionSpringUpdate`, une fois le ressort et l'amortisseur calculés ;
`highGeeThreshold` vaut `1.5`, `highGeeSpringTweakable` `3`, `highGeeDamperTweakable` `1.2` :

```csharp
if (adjustForHighGee && base.vessel.mainBody.GeeASL > (double)highGeeThreshold)
{
    spring *= highGeeSpringTweakable;
    damper *= highGeeDamperTweakable;
}
```

</details>
