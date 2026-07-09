# ROUTING_ANSWER — Comment l'UI de routage a été construite (Personne A)

> Journal explicatif de ce qui a été fait, **et pourquoi**, pour que tu puisses le
> comprendre et le défendre à l'oral. Rôle : **Personne A** (routage / émission
> ArtNet). Date : 8 juillet 2026.

---

## 0. La démarche en une phrase

J'ai d'abord **compris** la chaîne (doc LAPS + code existant), puis j'ai **branché
une UI Avalonia** sur les contrats déjà fournis par la Personne B (`State`,
`RoutingPlan`, `ArtNetSender`), en gardant le routage sur un **thread séparé**
(exigence P2). Ensuite j'ai ajouté **l'import `.xlsx`**, et j'ai **audité** le tout
contre les exigences P1–P8.

---

## 1. Comprendre avant de coder

### 1.1 Lire la doc du projet
J'ai lu les 9 pages de `learn.glassworks.tech/led` pour extraire les chiffres qui
contraignent le routage :
- **BC216** : 16 sorties × 1024 canaux = **2 univers DMX/sortie**, 512 canaux/univers.
- **1 LED RVB = 3 canaux** → **170 LED/univers** (la 171ᵉ déborderait sur les
  canaux 511–512, donc on passe à l'univers suivant).
- **Écran cible** : 128×128 = 16 384 LED, 128 univers, 4 contrôleurs (.45→.48).
- **eHuB** = protocole d'entrée UDP (≠ le fichier Excel d'adressage, piège classique).

### 1.2 Lire le code existant
Avant d'écrire une ligne, j'ai lu les contrats que je dois **consommer** (je ne les
réécris pas, c'est le domaine de B) :

| Fichier | Ce que j'en tire |
| ------- | ---------------- |
| `csharp/Mappa/State.cs` | buffer dense RVBW (4 octets/entité), lu via `.Buffer` / `.Get` |
| `csharp/Mappa/RoutingPlan.cs` | `Render(state)` → `{univers: byte[512]}` déjà encodé DMX |
| `csharp/Mappa/Config.cs` | contrôleurs, univers, IP, `EntityMap` |
| `csharp/Mappa/ArtNet.cs` | `ArtNetSender.SendPlan(config, packets)` : encodage + UDP |
| `csharp/Mappa/Persistence.cs` | `LoadConfig(path)` |

**Point clé** : tout ce dont l'UI a besoin existe déjà. Mon travail = **orchestrer**
ces briques dans une boucle temps réel + les rendre visibles.

---

## 2. Choix de la techno UI : Avalonia

**Pourquoi Avalonia** et pas WPF/WinForms :
- Le livrable demande **Windows + macOS Silicon** → WPF/WinForms sont Windows-only.
  Avalonia est multi-plateforme avec le même modèle XAML/MVVM que WPF.
- Cible `net8.0` qui référence le cœur `Mappa` (`netstandard2.1`) sans souci.

**Le piège de perf que j'ai anticipé** : afficher 16 384 LED. Créer un contrôle par
LED écroule n'importe quel framework. → Je dessine **tout dans un seul
`WriteableBitmap`** (un buffer de pixels que je remplis à la main), ce qui tient
40 Hz facilement.

---

## 3. L'architecture de l'UI (le point à défendre pour P2)

Le routage ne doit **jamais** être bloqué par l'affichage. J'ai donc deux boucles
indépendantes qui communiquent par un **snapshot** (double-buffer) :

```
 Thread "Mappa-Routing" (40 Hz)              Thread UI (~30 img/s)
 ┌───────────────────────────┐              ┌────────────────────┐
 │ Faker → State             │  snapshot    │ CopySnapshot()     │
 │ RoutingPlan.Render()      │ ──copie────► │ dessine 1 bitmap   │
 │ ArtNetSender.SendPlan()   │  sous verrou │ (WriteableBitmap)  │
 └───────────────────────────┘              └────────────────────┘
```

### 3.1 `Mappa.Ui/RoutingEngine.cs` — le cœur temps réel
- Un `Thread` dédié (« Mappa-Routing ») tourne à **40 Hz** avec un cadencement
  auto-corrigé (resync si on décroche).
- Chaque frame : (1) le Faker remplit le `State` ; (2) `RoutingPlan.Render` produit
  les paquets DMX ; (3) si activé, `ArtNetSender.SendPlan` émet en UDP ; (4) je
  **copie** les octets DMX dans un snapshot partagé, sous `lock`.
- **Zéro allocation dans la boucle chaude** : les buffers sont réutilisés (exigence
  mémoire/CPU de P2).
- L'échange de config (hot reload) se fait par un swap atomique sous verrou → on
  peut recharger une config pendant que ça tourne.

### 3.2 `Mappa.Ui/MainWindow.axaml(.cs)` — l'affichage
- Un `DispatcherTimer` (~30 img/s) appelle `CopySnapshot()` puis redessine.
- Le **visualiseur DMX** : 1 ligne = 1 univers, 1 case = 1 LED RVB, dessinée
  **avant** encapsulation ArtNet → c'est exactement le « visualize DMX universes in
  2D grid » de P8.
- Le dessin est en `unsafe` (accès direct aux pixels du bitmap) pour la vitesse.

### 3.3 `Mappa.Ui/Faker.cs` — générateur de test (P8)
- `RainbowFaker` injecte une onde arc-en-ciel dans le `State`, sans dépendre
  d'Unity ni d'un flux eHuB. Ça permet de valider tout le pipeline
  routage → émission → affichage tout de suite.

### 3.4 Fichiers de démarrage Avalonia
`Program.cs` (point d'entrée), `App.axaml(.cs)` (thème Fluent sombre),
`app.manifest` (DPI-aware pour un rendu net). Le projet est ajouté à `Mappa.sln`.

---

## 4. L'import `.xlsx` depuis l'UI

### 4.1 Le problème
Le lecteur Excel existait déjà (`EhubXlsx`) mais était **`internal` dans Mappa.Cli**,
donc invisible pour l'UI.

### 4.2 La décision : le remonter dans le cœur `Mappa`
Je l'ai **déplacé** vers `csharp/Mappa/EhubXlsx.cs`, rendu `public`. Justification :
il n'utilise que la BCL (`System.IO.Compression` + `System.Xml.Linq`), aucune
dépendance NuGet — exactement comme `Persistence` qui fait déjà de l'I/O fichier
dans le cœur. La CLI continue de marcher (elle a déjà `using Mappa;`).

### 4.3 Le pipeline (identique CLI ↔ UI)
```
fichier .xlsx ──EhubXlsx.Read──► List<EhubRow> ──ConfigBuilder.BuildFromEhub──► Config ──ApplyConfig──► RoutingEngine
```
Dans l'UI, un bouton **« Importer un .xlsx (eHuB)… »** ouvre le sélecteur de fichier
natif Avalonia (`StorageProvider`), lit le fichier, construit la `Config`, la valide
et la charge dans le moteur. `.json` et `.xlsx` passent par le **même**
`ApplyConfig()` (pas de duplication).

### 4.4 Comment `.xlsx` devient une `Config`
`EhubXlsx` traite le fichier comme ce qu'il est — un **ZIP de XML** :
1. lit `xl/sharedStrings.xml` (les chaînes partagées) ;
2. résout la 1ʳᵉ feuille via `workbook.xml` + ses `.rels` ;
3. lit les cellules **par référence de colonne** (`A1`, `B2`…), pas par position ;
4. mappe les en-têtes `Entity Start/End`, `ArtNet IP`, `ArtNet Universe`, `Name` ;
5. ignore toute colonne inconnue (ex. `TOTAL PIXELS`, `Start`).

Puis `ConfigBuilder.BuildFromEhub` : chaque IP distincte → un contrôleur ; chaque
univers reçoit un **index global unique** tout en gardant son **numéro ArtNet local**
(0..31, celui envoyé sur le fil).

---

## 5. Comment j'ai vérifié (preuves, pas de confiance aveugle)

1. **Build** : `dotnet build Mappa.sln` → **0 erreur / 0 warning** sur les 5 projets.
2. **Démarrage UI** : lancée puis fermée → la fenêtre s'ouvre sans crash XAML.
3. **Import `.xlsx` de bout en bout** : comme il n'y avait pas de `.xlsx` dans le
   repo, j'ai **généré un vrai fichier** reproduisant ta capture (en-têtes,
   `sharedStrings`, colonnes parasites `Start`/`TOTAL PIXELS`, trou d'entités
   359–399), puis je l'ai passé dans la CLI (`import`), qui utilise le **même code**
   que l'UI. Résultat :
   ```
   Lignes : 3 · Contrôleurs : 1 (192.168.1.45) · Univers : 3 · Entités : 429 · Validation : OK
   ```
   429 = 170 + 89 + 170, avec le trou correctement exclu → parsing correct.

   > Détail technique rencontré : sous Windows PowerShell 5.1,
   > `ZipFile.CreateFromDirectory` écrit les noms d'entrées avec des `\`, ce qui
   > casse `GetEntry("xl/...")`. J'ai construit le ZIP à la main avec des `/`.

---

## 6. Ce qui a été créé / modifié

**Créé** — `csharp/Mappa.Ui/` :
`Mappa.Ui.csproj`, `Program.cs`, `App.axaml(.cs)`, `MainWindow.axaml(.cs)`,
`RoutingEngine.cs`, `Faker.cs`, `app.manifest`, `EXIGENCES.md` (l'audit P1–P8).

**Déplacé** : `Mappa.Cli/EhubXlsx.cs` → `Mappa/EhubXlsx.cs` (public).

**Modifié** : `Mappa.sln` (ajout du projet UI), `Mappa.Cli/Program.cs` (retrait d'un
`using` devenu inutile).

---

## 7. Pour lancer

```powershell
dotnet run --project csharp/Mappa.Ui
```
→ **Charger un .json** ou **Importer un .xlsx** → **Start**. La case *Faker* anime
l'écran ; cocher *Émettre en ArtNet* envoie réellement en UDP aux BC216.

---

## 8. Ce qu'il reste (voir `csharp/Mappa.Ui/EXIGENCES.md`)

1. **P2** — *dirty-tracking* dans `SendPlan` (n'émettre que les univers modifiés).
2. **P8** — sniffer ArtNet (récepteur UDP 6454) branché sur le visualiseur.
3. **P7** — récepteur eHuB temps réel (bonus).
