# Personne B — Synthèse de mon passage (contrats & découplage)

> Document de synthèse personnel : ce que j'ai écrit, ce que chaque fichier fait,
> à quoi il sert dans l'architecture, et les pistes d'amélioration. À utiliser
> comme antisèche de soutenance et comme feuille de route.
>
> _Mis à jour après le `pull` de `main` (merge des branches Authoring + UI) :
> mon cœur B (`csharp/Mappa/`) est inchangé, mais il vit désormais dans une
> solution multi-projets où A (UI/routage) et C (authoring) **consomment
> réellement mes contrats**. Voir §0._

## 0. Contexte multi-projets (après merge de `main`)

La solution `csharp/Mappa.sln` regroupe maintenant les trois rôles côte à côte —
ce qui **valide en pratique** le découplage que je garantis :

| Projet | Rôle | Dépend de mon cœur B ? |
| --- | --- | --- |
| **`Mappa/`** | **Mon cœur B** : contrats + config + routage + failover | — (c'est moi) |
| `Mappa.Ui/` | **A** : `RoutingEngine` temps réel (40 Hz), visualiseur DMX, émission ArtNet | Oui : lit `State` + `RoutingPlan` |
| `Mappa.Authoring.Core/` + `.App` + `.Cli` | **C** : timeline, effets, shows, émission eHuB | Oui : écrit dans `State` |
| `Mappa.Cli/` | Outils CLI (generate / demo / failover / show) | Oui |
| `Mappa.Bench/` | Banc de perf `render()` | Oui |
| `Mappa.Tests/` + `Mappa.Authoring.Tests/` | Tests xUnit | Oui |

Point d'oral : **rien dans mon cœur B ne référence l'UI ni l'authoring**. Ce sont
eux qui dépendent de mes contrats, jamais l'inverse. Le sens des dépendances
prouve le découplage.

Deux consommateurs de référence à connaître (ils ne sont **pas** de moi, mais ils
utilisent mes contrats) :

- **`Mappa.Ui/RoutingEngine.cs`** (A) : la boucle temps réel qui appelle
  `RoutingPlan.Render(state)` à 40 Hz sur un thread dédié, avec **hot reload
  atomique** via `SetConfig(...)` — l'exact mécanisme que mon `ConfigManager`
  déclenche. C'est la preuve vivante que la reconfig à chaud ne casse pas la
  boucle.
- **`Mappa.Authoring.Core/`** (C) : écrit des couleurs dans le `State` via des
  IDs d'entités, sans jamais connaître l'adressage physique.

## 1. Mon rôle en une phrase

Je suis le **garant du découplage**. Je ne crée pas les animations (rôle C) et
je n'envoie pas les paquets réseau (rôle A) : je fournis les **deux contrats**
qui permettent à C et A de travailler sans jamais se connaître.

Les deux contrats :

1. **`State`** — les données par frame : couleur RVBW de chaque entité à
   l'instant *t*. Écrit par C, lu par A.
2. **`Config` / `RoutingPlan`** — le mapping matériel : *quelle entité va sur
   quel univers / quel canal / quel contrôleur*. Défini par moi, consommé par A.

Message clé : **C ne connaît que des IDs de pixels. A ne connaît que le `State`
et le `RoutingPlan`. Aucun ne connaît l'autre.** On peut donc remplacer, tester
ou reconfigurer chaque bloc indépendamment.

## 2. Vocabulaire (les 4 briques)

| Terme | En une phrase | Monde |
| --- | --- | --- |
| **Entité** | Un pixel / une LED logique, identifié par un ID unique, coloré par l'artiste | Logique |
| **Canal** | Une case DMX (1 octet, 0-255) ; 3 par LED RGB, 4 par LED RGBW | Adressage |
| **Univers** | Un lot de 512 canaux ; l'unité d'envoi réseau (≈170 LED RGB) | Transport |
| **Contrôleur** | Le boîtier physique (IP + sorties, ex. BC216) qui reçoit les univers et pilote les LED | Physique |

Chaîne complète : `Entité → (univers, canal) → contrôleur → IP`.

## 3. Les fichiers que j'ai écrits

Tout le cœur vit dans `csharp/Mappa/` (bibliothèque `netstandard2.1`, compatible
Unity, **sans dépendance externe**).

### 3.1 Les contrats (le cœur du rôle B)

#### `State.cs` — contrat de données par frame

- **Ce qu'il fait** : stocke la couleur RVBW de chaque entité dans un **buffer
  dense** (4 octets/entité), indexé par un *slot* contigu, avec un dictionnaire
  `id → slot`. Expose `Set/SetRgb/SetColor/Fill/Clear` (écriture C) et
  `Get/Buffer/SlotOf` (lecture A).
- **À quoi il sert** : c'est le point de rendez-vous entre C et A. Un ID inconnu
  est ignoré en écriture (C reste découplé du dimensionnement) et renvoie du noir
  en lecture.
- **Décision d'archi** : buffer dense réutilisé d'une frame à l'autre → **aucune
  allocation à 40 Hz** sur ~16 000 entités (pas de pression GC).

#### `Config.cs` — contrat matériel (statique, rechargeable)

- **Ce qu'il fait** : modélise l'installation — `Controller`, `Universe`,
  `Strip`, `Device`, `EntityMapping` — et fournit `EntityIds()` (liste triée
  dédupliquée) et `Validate()` (contrôleur inconnu, débordement de canal,
  chevauchement d'IDs).
- **À quoi il sert** : décrit *le matériel* de façon compacte (adressage par
  **plages** `EntityMapping` plutôt qu'une entrée par LED) et **lisible**.
- **Détail important** : `Universe.Index` (identifiant **global** unique dans la
  config) est découplé de `ArtNetUniverse` (numéro **local** réellement envoyé
  sur le fil au contrôleur, ex. 0..31).

#### `RoutingPlan.cs` — résolution entité → adresse physique (le pont vers A)

- **Ce qu'il fait** : précalcule, pour chaque entité, son `EntityAddress`
  `(univers, canal, nb de canaux)` avec **débordement automatique** d'univers
  quand on dépasse 512 canaux. `Render(state)` projette le `State` sur un
  `byte[512]` par univers (buffers réutilisés).
- **À quoi il sert** : A ne recalcule **rien** par frame — juste une copie
  d'octets aux bons offsets. C'est ce qui rend le pipeline si rapide.

#### `Persistence.cs` — Save/Load JSON + `ConfigManager` (hot reload)

- **Ce qu'il fait** : sérialise/désérialise la `Config` en **JSON** via un
  mini-parseur maison (aucune dépendance, interopérable avec l'implémentation
  Python). `ConfigManager` gère la config courante, la validation au chargement,
  et l'événement `OnReload` pour la **reconfiguration en direct**.
- **À quoi il sert** : la config est un livrable versionnable (Git), éditable à
  la main, et **rechargeable à chaud** sans relancer l'application.

### 3.2 Tolérance aux pannes

#### `Failover.cs` — redéploiement d'un contrôleur

- **Ce qu'il fait** : `AddController`, `ReassignUniverses`, `ReplaceController` —
  réaffecte les univers d'un contrôleur défaillant vers un remplaçant.
- **À quoi il sert** : gérer « un contrôleur tombe, j'en ajoute un et je
  redirige » **sans recompiler**. Point d'archi fort : l'`EntityMap` (adressage
  logique) reste **intacte** ; seule l'association *univers → contrôleur/IP*
  change.

### 3.3 Générateurs de configuration

#### `Wall.cs` — génère le mur LED de test

- **Ce qu'il fait** : `BuildWallConfig(...)` produit programmatiquement la config
  du mur (64 bandes, 128 univers, numérotation par colonnes/quarts) — entièrement
  paramétrable, aucun fichier externe.
- **À quoi il sert** : fournir une cible réaliste pour A, C et le bench de perf.

#### `Shapes.cs` — formes non-2D (segments 3D, araignée)

- **Ce qu'il fait** : modélise une installation comme des **segments** de LED
  placés dans l'espace ; `BuildSpiderConfig()` génère une araignée 3D. Sauvegarde
  les **positions 3D à part** (`*.positions.json`).
- **À quoi il sert** : **prouver** que le mur 2D n'est qu'un cas particulier — le
  même `State`/`RoutingPlan` route une géométrie quelconque. Les positions 3D
  sont hors du contrat de routage (A n'en a pas besoin).

#### `ConfigBuilder.cs` — construit une `Config` depuis des données eHuB

- **Ce qu'il fait** : code **pur** (sans I/O) qui transforme des lignes
  d'adressage (`EhubRow`) en `Config` : chaque IP distincte → un contrôleur ;
  index d'univers global unique tout en conservant l'univers ArtNet local ;
  empilement automatique des canaux si plusieurs bandes partagent un univers.
- **À quoi il sert** : importer l'adressage réel fourni par les intégrateurs.

#### `EhubXlsx.cs` — lecteur `.xlsx` natif

- **Ce qu'il fait** : lit un tableau d'adressage Excel **sans dépendance NuGet**
  (un `.xlsx` = archive ZIP de XML ; lecture de `sharedStrings.xml` + feuille,
  mapping par en-têtes de colonnes).
- **À quoi il sert** : alimenter `ConfigBuilder` depuis les fichiers Excel des
  panneaux, aussi bien en CLI que dans l'UI.
- **Note (post-merge)** : ce fichier a été **déplacé de `Mappa.Cli/` vers le cœur
  `Mappa/`**. C'est cohérent : la construction de config à partir de l'adressage
  fait partie de mon périmètre (B), pas d'un outil CLI. Il est ainsi réutilisable
  par l'UI comme par le CLI.

### 3.4 Protocole eHuB (transport alternatif)

#### `Ehub.cs` — encodage/décodage du protocole eHuB

- **Ce qu'il fait** : encode/décode les messages eHuB (`Config` = plages,
  `Update` = sextuors id+RVBW), avec en-tête `eHuB`, compression **gzip** et
  gestion d'endianness. `ComputeRanges` compacte des IDs contigus en plages.
- **À quoi il sert** : parler le protocole eHuB attendu par certains
  contrôleurs/outils (alternative à l'ArtNet brut).

#### `EhubReceiver.cs` — réception UDP eHuB → `State`

- **Ce qu'il fait** : écoute en UDP sur un thread dédié, décode les updates eHuB,
  et remplit un `State` via `Fill(state)`.
- **À quoi il sert** : recevoir des couleurs depuis une source externe (test,
  interop) et les injecter dans le pipeline standard.
- **Validé par le merge** : le pont **C → A via eHuB** est désormais opérationnel
  (l'authoring Unity/`.Core` émet de l'eHuB, `EhubReceiver` le reçoit et remplit
  le `State`). Couvert par `Mappa.Tests/EhubBridgeTests.cs` (round-trip
  encode → UDP → `Fill`).

### 3.5 Émission de référence & outils d'authoring

#### `ArtNet.cs` — émetteur ArtNet de référence (domaine A, fourni pour débloquer)

- **Ce qu'il fait** : encapsule un univers DMX512 dans un paquet ArtDMX conforme
  et l'envoie en UDP. `SendPlan(config, packets)` route chaque univers vers l'IP
  de son contrôleur, en envoyant le **numéro d'univers ArtNet local**.
- **À quoi il sert** : valider bout-en-bout que le `RoutingPlan` produit les bons
  octets (« allumer 1 LED réelle »). C'est une **référence** : A peut la
  remplacer.

#### `Text.cs` / `ImageArt.cs` / `WallGeometry.cs` — outils d'authoring (côté C)

- **`WallGeometry.cs`** : convertit une position logique `(col, row)` en ID
  d'entité (modèle serpentin paramétrable), et peut **déduire** la géométrie
  d'une `Config` via `Infer(...)`.
- **`Text.cs`** : police bitmap 3×5, dessine du texte dans un `State` (via la
  géométrie). Utile pour les démos/diagnostics.
- **`ImageArt.cs`** : image raster RVB (dessin par code + chargement PPM), blit
  vers le `State` avec redimensionnement.
- **À quoi ils servent** : ce sont des **outils** (démo/diagnostic). Ils ne font
  **pas** partie des contrats : ils écrivent dans le `State` exactement comme le
  ferait C.

### 3.6 Documentation & tests que j'ai fournis

- **`README.md`** — présentation du module, API pour A et C, exemples.
- **`ARCHITECTURE.md`** — support de soutenance (diagrammes Mermaid, décisions,
  scénarios P4).
- **`Mappa.Tests/`** — tests xUnit : `ConfigRoutingTests`, `StateTests`,
  `WallShapesFailoverTests`, `EhubBridgeTests` (pont eHuB C→A). Les tests
  d'authoring (C) vivent à part dans `Mappa.Authoring.Tests/`.
- **`Mappa.Bench`** — banc de perf : `render()` mesuré à **~0,19 ms/frame**
  (~0,7 % du budget 25 ms à 40 Hz, marge ≈ ×130).

## 4. Comment tout s'articule (une frame)

```
C  ── Set(id, r,g,b) ─────────▶  State (buffer RVBW dense)
                                    │
A  ── plan.Render(state) ───────────┘  →  { univers : byte[512] }
A  ── sender.SendPlan(config, packets) ─▶  UDP ArtNet vers IP du contrôleur  ─▶  LED
```

À ~40 Hz, buffers réutilisés, zéro allocation dans la boucle chaude.

## 5. Les 3 scénarios de flexibilité (P4)

1. **Reconfiguration en direct** — `ConfigManager.Reload(...)` bascule d'une
   config à l'autre et notifie A. Échange **atomique**, la boucle ne s'arrête
   jamais.
2. **Panne d'un contrôleur** — `Failover.ReplaceController(...)` redéploie les
   univers. Adressage logique **inchangé**, seule l'IP change.
3. **Géométrie non-2D** — `spider.json` : même code de routage qu'un mur plat.

## 6. Ce que je peux faire pour améliorer

### 6.1 Robustesse & validation

- **Renforcer `Config.Validate()`** : détecter les débordements de canaux dans
  les `EntityMapping` (pas seulement les `Device`), les univers référencés mais
  absents de la liste `Universes`, les IP invalides, les IDs d'entités
  dupliqués entre `EntityMap` et `Device`.
- **Parseur JSON** (`Persistence.cs`) : il est minimal et **suppose un JSON bien
  formé** (pas de gestion d'erreur explicite, pas d'échappement Unicode `\uXXXX`,
  pas de vérification de fin de buffer). À durcir si un humain édite les fichiers
  à la main → messages d'erreur clairs avec ligne/colonne.
- **`Ehub.Decode`** : borne déjà la taille du payload compressé, mais on pourrait
  valider `Count` contre la taille réelle du payload décompressé pour éviter des
  lectures hors limites en cas de message corrompu.

### 6.2 Hot reload « vraiment » automatique

- Aujourd'hui le reload doit être **déclenché** (pas de synchronisation
  automatique). Ajouter un `FileSystemWatcher` optionnel dans `ConfigManager`
  pour recharger dès que le fichier change sur disque (avec debounce).
- **Reload résilient** : si la nouvelle config est invalide, on lève une
  exception et l'ancienne config est perdue côté appelant selon l'usage.
  Garantir un **rollback** propre (conserver l'ancienne config si `Validate()`
  échoue) et remonter l'erreur sans casser la boucle temps réel.

### 6.3 Transition visuelle lors d'une reconfig

- Lors d'un `SetConfig`, le nouveau `State` démarre à **noir** : un « trou » d'une
  frame est possible si C n'a pas encore réécrit. On pourrait **reporter** les
  couleurs des entités communes de l'ancien `State` vers le nouveau pour une
  bascule sans clignotement.

### 6.4 Failover plus complet

- Gérer le failover **au niveau d'une sortie** (pas seulement d'un contrôleur
  entier) : réaffecter un sous-ensemble d'univers d'une sortie défaillante.
- **Redondance active** : envoyer en parallèle vers un contrôleur de secours
  (hot standby) plutôt que de basculer manuellement.
- Détection **automatique** de panne (heartbeat/ArtPoll) pour déclencher le
  `ReplaceController` sans intervention.

### 6.5 Modèle d'adressage plus riche

- Supporter des **ordres de canaux** autres que R,G,B(,W) (ex. GRB, BGR) par
  `EntityMapping` — fréquent selon les rubans.
- Gérer un **channel_start** non nul combiné au débordement (actuellement le
  débordement repart à `channel = 0`, ce qui est correct pour le mur mais
  mériterait un test dédié pour les cas où plusieurs bandes partagent un univers).
- Permettre des **entités RGBW et RGB mélangées** dans une même config sans
  ambiguïté (déjà possible par `EntityMapping`, mais à couvrir par des tests).

### 6.6 Tests & mesures

- Le pont eHuB est déjà couvert par `EhubBridgeTests` (encode → UDP → `Fill`),
  mais il manque encore des tests unitaires ciblés sur `ConfigBuilder`
  (empilement de canaux, index global unique), `EhubXlsx` (Excel minimal en
  ressource de test), `Ehub` (round-trip `EncodeConfig`/`DecodeRanges`,
  endianness `LittleEndian`), et `WallGeometry.Infer`.
- Étendre le bench : mesurer aussi `SetConfig` (coût d'une reconfig à chaud) et
  l'`EhubReceiver` sous charge.
- Mesurer le **jitter** de la boucle 40 Hz (pas seulement la moyenne de
  `render()`), car c'est ce qui compte pour la synchro son/lumière.

### 6.7 Qualité de vie

- Uniformiser la **gestion des accents** dans les commentaires (actuellement sans
  accents par prudence d'encodage) — l'encodage UTF-8 sans BOM est déjà en place.
- Documenter le **format eHuB** dans `ARCHITECTURE.md` (il n'y est pas encore).
- Exposer une petite **API de diagnostic** : « pour l'entité X, donne-moi
  univers/canal/contrôleur/IP » — utile en live pour localiser une LED morte.

## 7. Où est quoi (récapitulatif)

| Bloc | Fichier |
| --- | --- |
| Contrat State | `csharp/Mappa/State.cs` |
| Config + validation + (dé)sérialisation | `csharp/Mappa/Config.cs`, `Persistence.cs` |
| Résolution routage (pour A) | `csharp/Mappa/RoutingPlan.cs` |
| Hot reload | `csharp/Mappa/Persistence.cs` (`ConfigManager`) |
| Failover | `csharp/Mappa/Failover.cs` |
| Générateur mur | `csharp/Mappa/Wall.cs` |
| Formes non-2D | `csharp/Mappa/Shapes.cs` |
| Import eHuB / Excel | `csharp/Mappa/ConfigBuilder.cs`, `EhubXlsx.cs` |
| Protocole eHuB | `csharp/Mappa/Ehub.cs`, `EhubReceiver.cs` |
| Émetteur ArtNet (réf. A) | `csharp/Mappa/ArtNet.cs` |
| Outils authoring (démo) | `csharp/Mappa/Text.cs`, `ImageArt.cs`, `WallGeometry.cs` |
| Doc & tests | `README.md`, `ARCHITECTURE.md`, `csharp/Mappa.Tests/`, `Mappa.Bench/` |

## 8. Trois phrases à retenir pour l'oral

1. « Toute la communication passe par **deux contrats** : le `State` (par frame)
   et la `Config`/`RoutingPlan` (matériel). »
2. « Grâce à ce découplage, création, routage et matériel évoluent **en parallèle
   et se testent isolément**. »
3. « Le mur 2D, la reconfiguration à chaud et l'araignée 3D utilisent
   **exactement le même code** — seule la config change. »
