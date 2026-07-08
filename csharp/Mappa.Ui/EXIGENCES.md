# Conformité aux exigences — Personne A (Routage / émission ArtNet)

> Audit du code au **8 juillet 2026**. Références vérifiées dans les sources
> (pas d'à-peu-près). Statut : ✅ fait · 🟡 partiel · ❌ manquant.
>
> Rappel de périmètre : je suis **Personne A** (lire `State` + `RoutingPlan`,
> encoder DMX → ArtNet, émettre en UDP à 40 Hz). Je suis noté d'abord sur **P2**,
> et je contribue naturellement à **P8** (debug réseau) et **P7** (entrée eHuB).
> Les autres critères (P1/P4 = Personne B, P3 = Personne C) sont listés pour
> situer, mais ne sont pas mon cœur de note.

## Tableau de bord

| Crit. | Intitulé | Responsable | Statut | Où |
| ----- | -------- | ----------- | ------ | -- |
| **P2** | **Routage temps réel** | **A (moi)** | 🟡 **solide, 1 trou** | `RoutingPlan.cs`, `ArtNet.cs`, `Mappa.Ui/RoutingEngine.cs` |
| **P8** | **Débogage / monitoring** | **A (moi)** | 🟡 **2/3** | `Mappa.Ui/Faker.cs`, visualiseur DMX, CLI `scan` |
| **P7** | Interop eHuB (bonus) | A (moi) | ❌ manquant | — |
| P1 | Configuration | B | ✅ | `Config.cs`, `Persistence.cs`, `Failover.cs`, import `.xlsx` |
| P4 | Architecture découplée | B | ✅ | `State.cs`, `RoutingPlan.cs`, `ARCHITECTURE.md` |
| P3 | Outil de création | C | 🟡 | CLI `anim`/`text`/`image`, `Text.cs`, `ImageArt.cs` |
| P5 | Démo / preuve | équipe | 🟡 | `Mappa.Bench`, visualiseur UI |
| P6 | Interactivité (bonus) | équipe | ❌ | — |

---

## P2 — Routage (mon critère principal) 🟡

**Exigé** : recevoir l'état RVBW, l'envoyer aux bons contrôleurs / univers / canaux ;
mémoire minimale, **paquets ArtNet minimum**, CPU minimum, **thread séparé de l'UI**,
zéro latence / tearing / artefact.

| Sous-exigence | Statut | Preuve |
| ------------- | ------ | ------ |
| État → univers/canal correct | ✅ | `RoutingPlan.Render()` projette le buffer dense sur `byte[512]`/univers, avec débordement d'univers automatique (170 LED RVB/univers) |
| Encodage ArtNet conforme | ✅ | `ArtNet.cs` : en-tête `Art-Net\0`, OpCode `0x5000`, univers 15 bits, séquence, UDP 6454 |
| Émission par contrôleur (bonne IP) | ✅ | `ArtNetSender.SendPlan()` route chaque univers vers l'IP du contrôleur via `Failover.UniverseOf` |
| Mémoire minimale | ✅ | buffer dense RVBW réutilisé, paquets DMX pré-alloués une fois, **zéro allocation par frame** |
| CPU minimal | ✅ | plan précalculé ; `Render` = copies d'octets. `Mappa.Bench` : ~0,19 ms/frame pour 16 384 entités / 128 univers (budget 25 ms) |
| **Thread séparé de l'UI** | ✅ | `Mappa.Ui/RoutingEngine.cs` : `Thread` dédié « Mappa-Routing » à 40 Hz ; l'UI ne lit qu'un **snapshot** copié sous verrou. Aucun appel UI dans la boucle chaude |
| Pas de tearing | ✅ | double-buffer : l'UI ne voit jamais une frame à moitié écrite |
| **Paquets ArtNet minimum** | ❌ | **`SendPlan` envoie TOUS les univers à chaque frame**. Pas de *dirty-tracking* : un univers noir/inchangé est quand même émis |

### Le seul vrai trou de P2 : le *dirty-tracking*

`ArtNetSender.SendPlan` (voir `csharp/Mappa/ArtNet.cs`) boucle sur tout le
dictionnaire de paquets et émet chaque univers, chaque frame. L'exigence dit
« send **minimum** ArtNet packets ». Correctif attendu : ne ré-émettre un univers
que si ses 512 octets ont changé depuis la dernière frame (comparaison / hash par
univers), avec éventuellement un *keep-alive* périodique (ArtNet recommande de
ré-émettre au moins toutes ~800 ms même sans changement).

---

## P8 — Débogage / monitoring (mon critère secondaire) 🟡

**Exigé** : (a) *fakers* générateurs de signal ; (b) visualiser les univers DMX en
grille 2D **avant** encapsulation ArtNet ; (c) écouter le réseau ArtNet et afficher
les données reçues.

| Sous-exigence | Statut | Preuve |
| ------------- | ------ | ------ |
| (a) Fakers | ✅ | `Mappa.Ui/Faker.cs` (`RainbowFaker`) injecte une animation dans le `State` sans Unity ni eHuB. Côté CLI : `scan`, `anim`, `text`, `image` génèrent aussi du signal |
| (b) Visualiseur DMX 2D pré-ArtNet | ✅ | `Mappa.Ui/MainWindow` : 1 ligne = 1 univers, 1 case = 1 LED RVB, rendu depuis le snapshot DMX **avant** `SendPlan`. Dessin dans un seul `WriteableBitmap` (pas 16 k contrôles) |
| (c) Sniffer ArtNet (réception) | ❌ | **Aucun récepteur.** `scan` n'écoute pas, il émet. Il faut un `UdpClient` en écoute sur 6454 qui décode les `ArtDMX` reçus et les affiche dans le même visualiseur |

---

## P7 — Interopérabilité eHuB (bonus) ❌

**Exigé** : recevoir l'état au format **eHuB** (flux UDP d'Unity) et le router avec
des perfs correctes.

- ❌ **Pas de récepteur eHuB.** Le `State` est aujourd'hui alimenté par le Faker
  ou la CLI, pas par un flux réseau.
- ⚠️ **Piège de vocabulaire** : `csharp/Mappa/EhubXlsx.cs` lit le *tableau
  d'adressage* Excel (config statique P1). Ce **n'est pas** le protocole eHuB
  temps réel (UDP, header `eHuB`, type `2`, sextuors GZip, messages `config`).
- Travail à faire : un `UdpClient` en écoute qui parse les messages `update`
  (dé-GZip → sextuors → `State.Set`) et `config` (plages d'IDs → positions),
  puis nourrit le même `RoutingEngine`. Comme le Faker et l'entrée réseau écrivent
  tous deux dans le `State`, l'architecture est déjà prête à les brancher.

---

## Critères hors de mon cœur (contexte équipe)

- **P1 Configuration** ✅ — `Config.cs` (contrôleurs/IP/univers/bandes/appareils),
  `Persistence.cs` (save/reload JSON + `ConfigManager` hot reload), `Failover.cs`
  (contrôleur en panne → réaffectation d'univers), import `.xlsx` (CLI + UI),
  `Config.Validate()`. Mise à l'échelle et formes non-2D via `Shapes.cs` / configs
  d'exemple (`generate`).
- **P4 Architecture** ✅ — découplage par deux contrats (`State`, `Config`/`RoutingPlan`),
  documenté dans `ARCHITECTURE.md`. La création ne produit qu'une liste de pixels ;
  le routage ignore comment elle est produite.
- **P3 Création** 🟡 — outils intégrés basiques (`anim`, `text`, `image`) ; pas de
  synchro audio ni d'animation des lyres démontrée.
- **P5 Preuve** 🟡 — perf prouvée par `Mappa.Bench` ; le « wow » du spectacle dépend
  de P3.
- **P6 Interactivité** ❌ — aucun contrôle live (clavier/manette/caméra).

---

## Ma feuille de route (priorité pour Personne A)

1. **P2 — Dirty-tracking dans `SendPlan`** : n'émettre que les univers modifiés
   (+ keep-alive). C'est le seul trou de mon critère principal et le plus rentable.
2. **P8 — Sniffer ArtNet** : `UdpClient` en écoute 6454, décodage `ArtDMX`,
   affichage dans le visualiseur existant (comparer émis vs reçu).
3. **P7 — Récepteur eHuB** (bonus) : parser le flux UDP d'Unity pour remplacer le
   Faker par de vraies données ; réutilise directement le `RoutingEngine`.
4. **UI** : sélecteur d'onglet Excel + bouton « Sauver en .json » ; panneau
   contrôleurs avec failover en direct (démo P1 percutante).

### Résumé en une phrase
Mon **cœur de routage (P2) est fonctionnel et performant, thread séparé compris** ;
il me manque le **dirty-tracking** (paquets minimum) pour être complet, plus le
**sniffer ArtNet** (P8) et le **récepteur eHuB** (P7, bonus) côté réception réseau.
