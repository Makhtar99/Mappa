# Fiche récap — Personne B + Projecteurs (soutenance)

> Antisèche 1 page. Rôle B = **garant du découplage**. Je fournis les contrats ;
> A (routage) et C (authoring) les consomment sans jamais se connaître.

## 1. Mon rôle en une phrase

Je fournis les **deux contrats** qui découplent la création (C) du routage (A) :

1. **`State`** — couleur RVBW de chaque entité par frame. Écrit par C, lu par A.
2. **`Config` / `RoutingPlan`** — mapping matériel : entité → univers / canal / contrôleur.

> C ne connaît que des **IDs de pixels**. A ne connaît que le **`State` + `RoutingPlan`**.
> Aucun ne connaît l'autre. Le sens des dépendances (A, C → B, jamais l'inverse) prouve le découplage.

## 2. Vocabulaire (4 briques)

| Terme | En une phrase |
|---|---|
| **Entité** | Un pixel logique, ID unique, coloré par l'artiste |
| **Canal** | Une case DMX (1 octet 0–255) ; 3 par RGB, 4 par RGBW, 1 par RAW1 |
| **Univers** | Un lot de 512 canaux ; l'unité d'envoi réseau |
| **Contrôleur** | Le boîtier physique (IP + sorties, ex. BC216) |

Chaîne : `Entité → (univers, canal) → contrôleur → IP`.

## 3. Mes fichiers (`csharp/Mappa/`)

| Bloc | Fichier | Rôle |
|---|---|---|
| Contrat données | `State.cs` | Buffer dense RVBW réutilisé (0 alloc/frame) |
| Contrat matériel | `Config.cs` | Controller/Universe/Strip/Device/EntityMapping + `Validate()` |
| Résolution routage | `RoutingPlan.cs` | Entité → (univers, canal), débordement auto, `Render()` |
| Persistance + hot reload | `Persistence.cs` | JSON maison (interop Python) + `ConfigManager` |
| Failover | `Failover.cs` | Réaffecte univers d'un contrôleur en panne |
| Générateur mur | `Wall.cs` | Mur 64 bandes / 128 univers |
| Formes 3D | `Shapes.cs` | Araignée 3D (même routage que le mur 2D) |
| Import config | `ConfigBuilder.cs`, `EhubXlsx.cs` | eHuB rows / Excel → `Config` (sans NuGet) |
| Protocole eHuB | `Ehub.cs`, `EhubReceiver.cs` | Encode/décode gzip ; pont C→A |
| Réf. émission (A) | `ArtNet.cs` | Émetteur ArtDMX de référence |
| Adressage fixtures | `FixtureAddressing.cs` | Projo + lyres (chemin CLI/test direct) |
| Outils (C) | `Text.cs`, `ImageArt.cs`, `WallGeometry.cs` | Démo/diagnostic |
| Doc & tests | `README.md`, `ARCHITECTURE.md`, `Mappa.Tests/`, `Mappa.Bench` | — |

## 4. Une frame (le flux)

```
C  ── Set(id, r,g,b) ─────────▶  State (buffer RVBW dense)
                                    │
A  ── plan.Render(state) ───────────┘  →  { univers : byte[512] }
A  ── sender.SendPlan(config, packets) ─▶  UDP ArtNet vers IP contrôleur  ─▶  LED
```
~40 Hz, buffers réutilisés, zéro allocation dans la boucle chaude.

## 5. Décisions d'archi (et pourquoi)

- **Buffer dense** réutilisé → pas de pression GC à 16 000 entités × 40 Hz.
- **Adressage par plages** (`EntityMapping`) → config compacte et lisible.
- **Résolution précalculée** dans `RoutingPlan` → A ne fait qu'une copie d'octets par frame.
- **Config JSON** → versionnable Git, éditable à la main, interop Python.
- **`Validate()`** avant chargement → attrape tôt contrôleur inconnu / débordement / IDs dupliqués.
- **C# `netstandard2.1`** → compatible Unity.

## 6. Performance (juge de P2)

| Métrique | Valeur |
|---|---|
| `render()` moyen | **~0,19 ms / frame** |
| Budget frame 40 Hz | 25 ms |
| Budget utilisé | **~0,7 %** (marge ≈ ×130) |

## 7. Les 3 scénarios de flexibilité (P4)

1. **Reconfig en direct** — `ConfigManager.Reload(...)`, échange atomique, la boucle ne s'arrête jamais.
2. **Panne contrôleur** — `Failover.ReplaceController(...)` : l'adressage logique reste **intact**, seule l'IP change.
3. **Géométrie 3D** — `spider.json` : même code de routage qu'un mur plat.

---

## 8. Projecteurs (projo statique + 4 lyres)

**Cible** : ArtNet univers **33** sur `192.168.1.48` (contrôleur ctrl-4).

### Côté Unity (`assets/Scripts/`) — chemin temps réel
| Script | Rôle |
|---|---|
| `ProjectorController.cs` | Projo statique 4 canaux R/V/B/W |
| `LyreController.cs` | Lyre 13 canaux (pan/tilt 16 bits, dimmer, RGB, W, macro) |
| `DeviceEmitter.cs` | Envoie en UDP eHuB (gzip, 40 Hz) vers Mappa.Ui |
| `ProjectorVisual.cs` | Cône coloré **décoratif** (n'affecte pas le réseau) |
| `ProjectorTimeline.cs` | Séquence démo ~30 s : diagonales rouge/blanc en croix |
| `ProjectorKeyboardControl.cs` | Pilotage clavier (Espace, R/G/B/W, +/−) |

**Convention temps réel** : `1 entité eHuB = 1 canal DMX` (type `LedType.RAW1`).
`RoutingPlan.Render()` traite `Channels == 1` en copiant l'octet R au canal.

### Mapping (docs/DEMAIN.md — inversion gauche→droite dans ecran.json)
| Nom Unity | Entités eHuB | Canaux DMX | Adresse phys. | Position |
|---|---|---|---|---|
| Projector | 1..4 (R,V,B,W) | 1..4 | 001 | statique |
| Lyre 1 | 10..22 | 70..82 | 070 | GAUCHE |
| Lyre 2 | 30..42 | 50..62 | 050 | |
| Lyre 3 | 50..62 | 30..42 | 030 | |
| Lyre 4 | 70..82 | 10..22 | 010 | DROITE |

### Côté cœur B — `FixtureAddressing.cs` (helper d'adressage direct)
Helper statique qui écrit **directement** un `byte[512]` (utile pour la CLI et
des tests isolés). Il porte encore l'ancien mapping Excel (1 entité = 3 canaux
RGB, projecteur au canal 168) et reste testé **isolément** dans
`Mappa.Tests/FixtureSpotsTests.cs`.

> **La convention de PRODUCTION est `RAW1`**, portée par `configs/ecran.json` :
> 1 entité = 1 canal DMX, tout sur l'univers global 135 (= ArtNet **33**).
> Projecteur = entités 1..4 → canaux 0..3 ; lyres = 13 canaux chacune
> (`channel_start` 69/49/29/9, ordre inversé gauche→droite côté Unity).
> C'est cohérent avec `ProjectorController`/`LyreController` (Unity) et le
> `RoutingPlan` (`Channels == 1`). Les tests `EcranConfig_*` valident ce chemin
> RAW1 de bout en bout (`FixtureSpotsTests` + `EhubBridgeTests`).

> **Note d'oral** : `ecran.json` est une **donnée d'installation** chargée à
> l'exécution (`Persistence.LoadConfig`), au même titre que `wall.json` ou
> `spider.json`. Le cœur B ne dépend d'aucune config figée — on branche le
> panneau du jour et le même `RoutingPlan` le route. C'est la flexibilité P4.

---

## 9. Trois phrases à retenir

1. « Toute la communication passe par **deux contrats** : le `State` (par frame) et la `Config`/`RoutingPlan` (matériel). »
2. « Grâce à ce découplage, création, routage et matériel évoluent **en parallèle et se testent isolément**. »
3. « Le mur 2D, la reconfig à chaud et l'araignée 3D utilisent **exactement le même code** — seule la config change. »
