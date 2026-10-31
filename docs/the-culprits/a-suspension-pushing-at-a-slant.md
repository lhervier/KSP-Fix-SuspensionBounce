# Troisième coupable : une suspension qui pousse de biais

Partie de [Suspension Bounce Fix](../../README.md), [les coupables](../the-culprits.md) : un coupable qui projette.

Quand la pièce penche ou se couche, l'enfoncement que lit sa suspension saute d'un pas à l'autre (voir
[le modèle de base](the-base-model.md)). KSP n'en tient pas compte : le ressort et l'amortisseur restent ceux d'une
suspension droite. Les poussées deviennent violentes, changent de sens à chaque pas, et grandissent. Plus la suspension
penche, plus la même poussée secoue la pièce : environ deux fois plus fort à 50°. Une jambe couchée dont le pied frôle
le sol, ou le flanc d'un atterrisseur, finit projetée ainsi.

<details>
<summary>Dans le code</summary>

Ni `ModuleWheelSuspension.SuspensionSpringUpdate` ni `SuspensionLoadBalancer.FixedUpdate` ne regardent l'angle entre
l'axe de la suspension et la surface que touche son rayon.

</details>

## Ce que le mod change

Il mesure l'inclinaison de la suspension sur la surface que touche son rayon. Jusqu'à 40°, rien ne change. Pas moins :
certaines jambes sont penchées par construction, et une LT-2 posée sur un sol plat penche déjà de 32° ; plus bas, tous
les atterrisseurs sur LT-2 s'enfonceraient un peu plus que sans le mod. La jambe couchée qu'il s'agit de sauver était
projetée à 48-52°. Sur une pente, la jambe aval penche de son angle plus celui de la pente : les LT-2 aval d'un
atterrisseur posé sur plus de 8° environ passent 40°, et gardent 85 % de leur ressort et de leur amortisseur sur 13°.
Au-delà de 40°, la suspension perd peu à peu son ressort et son amortisseur : il en reste 40 % à
60°, rien quand elle est couchée à plat. [La limite du premier coupable](a-damper-too-strong-for-a-light-vessel.md#ce-que-le-mod-change)
baisse aussi avec l'inclinaison. Une jambe couchée dont le pied frôle le sol, ou le flanc d'un atterrisseur, ne pousse
presque plus, et ne peut plus être projetée. C'est le seul endroit où le mod touche au ressort. En contrepartie, une
roue de rover qui touche une paroi raide, un rocher ou le bord d'un cratère a une suspension plus molle tant qu'elle la
touche ; c'est encore à vérifier.

## Dans le code du mod

Dans [Src/SuspensionDamper.cs](../../Src/SuspensionDamper.cs), `LeanCosine` mesure l'inclinaison : le cosinus de
l'angle entre l'axe de la suspension et la normale de la surface que touche son rayon, ou la verticale du lieu quand
la roue ne touche rien.

```csharp
if (state != null && state.grounded && state.hit.normal != Vector3.zero)
{
    normal = state.hit.normal;
    point = state.hit.point;
}
else
{
    point = wheel.wheelCollider.transform.position;
    normal = FlightGlobals.getUpAxis(suspension.vessel.mainBody, point);
}
return Vector3.Dot(wheel.SuspensionAxis, normal);
```

`Fade` en tire la part du ressort et de l'amortisseur que la suspension garde : tout jusqu'à 40°
(`FullUpToDegrees`), puis de moins en moins, rien quand elle est couchée à plat.

```csharp
if (cos >= CosFullUpTo)
{
    return 1f;
}
if (cos <= 0f)
{
    return 0f;
}
float ratio = cos / CosFullUpTo;
return ratio * ratio;
```

Dans `Apply`, appelé par les deux postfixes, l'amortisseur de KSP est multiplié par cette part (`damper *= fade`), et
[la limite du premier coupable](a-damper-too-strong-for-a-light-vessel.md#dans-le-code-du-mod) par le carré du
cosinus. Le ressort de KSP n'est remplacé que tant que la suspension penche au-delà de 40°, et une dernière fois au pas
où elle repasse en deçà :

```csharp
if (isFaded || wasFaded)
{
    wheel.wheelCollider.springRate = stockSpring * fade;
}
```
