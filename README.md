# Mappa — Configuration & Architecture (Personne B)

Module central du projet *Ingénierie son et lumière* (contrôle de milliers de
LED adressables). Ce dépôt couvre le rôle **Personne B** : définir les
**contrats** qui découplent l'outil de création (C) du routage matériel (A).

Implémentation en **C# / .NET** (`netstandard2.1`, compatible Unity).

Il fournit :

- **`State`** — le contrat de données entre l'authoring (C) et le routage (A).
- **`Config`** — la description paramétrable de l'installation physique.
- **`RoutingPlan`** — la résolution *entité → univers / canal DMX*, prête à être
  consommée par A.
- **Save / Load JSON** + **rechargement à chaud** (reconfiguration en direct).
- **Failover** — redéploiement des univers d'un contrôleur en panne.
- Un **générateur** du mapping du mur LED + **formes non-2D** (araignée 3D).
- Un **émetteur ArtNet** de référence + un **banc de perf**.

## Architecture

Trois blocs découplés, communiquant *uniquement* via `State` et `Config` :

```
  Authoring (C)                Personne B                 Routage (A)
 ┌──────────────┐        ┌───────────────────┐        ┌──────────────┐
 │ crée des     │  écrit │      State        │  lit   │ encode ArtNet│
 │ animations   ├───────▶│ (buffer RVBW)     │◀───────┤ + envoie UDP │──▶ Matériel
 └──────────────┘        │                   │        └──────────────┘   (BC216,
                         │  Config /         │  entité → univers/canal    LED, lyres)
                         │  RoutingPlan      ├───────────────▲
                         └───────────────────┘
```

- **C** ne connaît que la liste des pixels à colorer (des IDs d'entités).
- **A** ne connaît que le `State` entrant + le `RoutingPlan` pour le router.
- **B** (ce module) est le seul à connaître le mapping physique.

Voir [`ARCHITECTURE.md`](ARCHITECTURE.md) pour les diagrammes et la soutenance (P4).

## Concepts

- **Entité** = un pixel / une LED, identifiée par un **ID unique** (non séquentiel).
- **State** = valeurs RVBW de chaque entité à l'instant *t*. Défaut : noir.
  Implémenté comme un **buffer dense réutilisé** (4 octets RVBW/entité) pour
  tenir ~16 000 entités à 40 Hz sans allocation par frame.
- **Univers DMX512** = 512 canaux → 170 LED RGB (170×3=510) ou 128 RGBW.
- **ArtNet** : les univers commencent à **0**. Un BC216 = 16 sorties, 1 sortie =
  1024 canaux = **2 univers**.

## Prérequis

- [.NET SDK 8.0](https://dotnet.microsoft.com/download) (pour le CLI, les tests
  et le bench). La bibliothèque cible `netstandard2.1` pour Unity.

## Utilisation (CLI)

```bash
cd csharp
dotnet run --project Mappa.Cli -- generate            # configs d'exemple (configs/)
dotnet run --project Mappa.Cli -- demo                # load → state → routage → reconfig
dotnet run --project Mappa.Cli -- failover            # panne + redéploiement d'un contrôleur
dotnet run --project Mappa.Cli -- show ../configs/wall.json
```

### API (pour A et C)

```csharp
using Mappa;

var config = Persistence.LoadConfig("configs/wall.json");
var state  = State.FromConfig(config);   // C écrit dedans
var plan   = new RoutingPlan(config);    // A l'utilise pour router

state.Set(100, 255, 0, 0);               // entité 100 en rouge (côté C)
var packets = plan.Render(state);        // {univers: byte[512]} (côté A)
```

### Rechargement à chaud (reconfiguration en direct — démo P4)

```csharp
var mgr = new ConfigManager("configs/wall_small.json");
mgr.OnReload += cfg => routeur.Rebuild(cfg);   // A se reconstruit
mgr.Load();
mgr.Reload("configs/wall.json");               // bascule en direct
```

### Tolérance aux pannes (P1 « Exhaustivité »)

Quand un contrôleur tombe en panne, on le remplace et on redirige ses univers.
La panne **ne change pas** l'adressage logique (entité → univers/canal) : seul
le contrôleur/IP qui pilote chaque univers change.

```csharp
var moved = Failover.ReplaceController(cfg, "bc216-A", "bc216-C", "192.168.1.12");
// `cfg.EntityMap` intacte → RoutingPlan reconstruit adresse à l'identique,
// seule l'IP de destination des univers déplacés a changé.
```

### Émetteur ArtNet (référence, côté A — jalon « 1 LED réelle »)

```csharp
using var sender = new ArtNetSender();
var packets = plan.Render(state);
sender.SendPlan(config, packets);   // envoie chaque univers à l'IP de son contrôleur
```

## Le mur LED de test

Décrit dans la doc : mur 2 m × 2 m, **64 bandes** de 259 LED (LED de fixation
invisibles aux positions 1, 129, 259), 128 univers. Les entités sont numérotées
par colonnes (100→358, +300 par colonne) et par quarts (offsets +5000).

Le mapping est **généré programmatiquement** par `Wall.BuildWallConfig(...)`
(entièrement paramétrable). Aucun fichier externe requis.

> **Tester sur le vrai panneau** (couleurs, texte, animations en Art-Net) :
> voir le guide pas-à-pas [`TEST_PANNEAU.md`](TEST_PANNEAU.md). La config exacte
> du mur réel est dans [`configs/ecran.json`](configs/ecran.json).

## Formes non-2D

`Shapes` modélise une installation comme un ensemble de **segments** de LED
placés librement dans l'espace 3D. `Shapes.BuildSpiderConfig()` génère une
araignée (corps + 8 pattes coudées) routée par **exactement le même code** que
le mur. Les **positions 3D** sont stockées à part (`*.positions.json`) car A n'en
a pas besoin : le contrat de routage reste identique quelle que soit la géométrie.

## Performance

`Mappa.Bench` mesure `render()` sur le mur de test (16 384 entités, 128 univers) :

```bash
dotnet run -c Release --project Mappa.Bench
```

Résultat mesuré : **~0,19 ms/frame**, soit **~0,7 % du budget d'une frame à
40 Hz** (25 ms) — une marge d'environ ×130. Le pipeline (buffer dense +
plan précalculé) tient donc très largement la synchro son/lumière (juge de P2).

## Configs d'exemple (`configs/`)

| Fichier            | Contenu                                   |
| ------------------ | ----------------------------------------- |
| `mini.json`        | 2 LED — pour débloquer A très tôt         |
| `wall.json`        | Mur 64 bandes + projecteur statique + 4 lyres |
| `wall_small.json`  | Variante réduite (32 colonnes) pour tests |
| `spider.json`      | Forme non-2D (araignée, +positions 3D)    |
| `wall_dual.json`   | Mur réparti sur 2 contrôleurs (scénario de panne) |

## Tests

```bash
cd csharp && dotnet test
```

## Structure

```
csharp/
  Mappa.sln
  Mappa/                Bibliothèque (netstandard2.1, cible Unity)
    State.cs            Contrat State (buffer RVBW dense)
    Config.cs           Modèle de config + validation + (de)sérialisation
    RoutingPlan.cs      Résolution entité → univers/canal (pour A)
    Persistence.cs      Save/Load JSON + ConfigManager (hot reload)
    Wall.cs             Générateur du mur LED
    Shapes.cs           Formes non-2D (segments 3D, araignée)
    Failover.cs         Redéploiement / tolérance aux pannes
    ArtNet.cs           Émetteur ArtNet de référence (côté A)
  Mappa.Cli/            CLI (generate / demo / failover / show)
  Mappa.Bench/          Banc de perf render()
  Mappa.Tests/          Tests xUnit
configs/                Fichiers de configuration d'exemple
docs/                   Schéma d'architecture (SVG) + aperçu 3D
ARCHITECTURE.md         Support de soutenance (P4)
```
