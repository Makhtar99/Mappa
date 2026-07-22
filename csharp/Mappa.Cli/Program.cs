using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mappa;

// Point d'entree / CLI du module Configuration & Architecture (Personne B).
//
// Usage :
//   dotnet run --project Mappa.Cli -- generate   # (re)genere les configs d'exemple
//   dotnet run --project Mappa.Cli -- demo       # save/load + reconfig a chaud + routage
//   dotnet run --project Mappa.Cli -- failover   # panne + redeploiement d'un controleur
//   dotnet run --project Mappa.Cli -- show configs/x.json
//   dotnet run --project Mappa.Cli -- send <config.json> [options]  # envoie de l'ArtNet reel
//   dotnet run --project Mappa.Cli -- text <config.json> "TEXTE" [options]  # ecrit du texte sur le mur

internal static class Program
{
    // configs/ est resolu par rapport a la racine du depot (2 niveaux au-dessus
    // de Mappa.Cli/), pour rester coherent avec l'ancien CLI Python.
    private static readonly string ConfigsDir = ResolveConfigsDir();

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            PrintUsage();
            return 1;
        }
        switch (args[0])
        {
            case "generate": GenerateExamples(); return 0;
            case "demo": Demo(); return 0;
            case "failover": FailoverDemo(); return 0;
            case "show" when args.Length >= 2: Show(args[1]); return 0;
            case "send" when args.Length >= 2: return Send(args);
            case "text" when args.Length >= 3: return TextCmd(args);
            case "scan": return Scan(args);
            case "pixel" when args.Length >= 2: return Pixel(args);
            case "anim" when args.Length >= 3: return Anim(args);
            case "image" when args.Length >= 2: return ImageCmd(args);
            case "import" when args.Length >= 2: return Import(args);
            case "ramp" when args.Length >= 2: return Ramp(args);
            case "preset" when args.Length >= 2: return Preset(args);
            default: PrintUsage(); return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: mappa <generate|demo|failover|show <config.json>|send <config.json> [options]>");
        Console.WriteLine();
        Console.WriteLine("send : envoie de l'ArtNet reel vers le(s) controleur(s) de la config.");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --ip <adresse>     force l'IP du controleur (ex: --ip 2.0.0.1),");
        Console.WriteLine("                       ecrase celle de la config (pratique en demo).");
        Console.WriteLine("    --entity <id>      n'allume que cette entite (defaut: toutes).");
        Console.WriteLine("    --color <r,g,b>    couleur 0-255 (defaut: 255,255,255 = blanc).");
        Console.WriteLine("    --hz <n>           frequence d'envoi en boucle (defaut: 40).");
        Console.WriteLine("    --frames <n>       nombre de frames a envoyer puis eteindre (defaut: illimite, Ctrl+C).");
        Console.WriteLine("    --once             envoie une seule frame puis quitte (ne rien eteindre).");
        Console.WriteLine();
        Console.WriteLine("  Exemple 1 LED reelle : mappa send ../configs/mini.json --ip 2.0.0.1 --entity 1 --color 255,0,0");
        Console.WriteLine();
        Console.WriteLine("text : ecrit du texte sur le mur (police 3x5) et l'envoie en ArtNet.");
        Console.WriteLine("  Usage : mappa text <config.json> \"TEXTE\" [options]");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --ip <adresse>     force l'IP du controleur.");
        Console.WriteLine("    --color <r,g,b>    couleur du texte (defaut: 255,255,255).");
        Console.WriteLine("    --brightness <f>   attenue la couleur, 0..1 (defaut: 1 = plein).");
        Console.WriteLine("    --x <n> --y <n>    coin haut-gauche du texte (defaut: 0,0).");
        Console.WriteLine("    (la geometrie du mur est deduite automatiquement de la config)");
        Console.WriteLine("    --flip-x / --flip-y  inverse l'axe (selon l'orientation physique).");
        Console.WriteLine("    --preview          affiche un apercu ASCII sans envoyer.");
        Console.WriteLine("    --once             envoie une seule frame (defaut: boucle 40 Hz).");
        Console.WriteLine();
        Console.WriteLine("  Exemple : mappa text ../configs/wall.json \"FAIT AVEC CLAUDE LE MEILLEUR\" --ip 2.0.0.1 --color 0,200,255");
        Console.WriteLine();
        Console.WriteLine("scan : DIAGNOSTIC. Allume TOUS les canaux de plusieurs univers pour");
        Console.WriteLine("       faire reagir n'importe quelle LED et trouver le bon univers/mode.");
        Console.WriteLine("  Usage : mappa scan --ip <adresse> [options]");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --ip <adresse>     IP du controleur (defaut: 255.255.255.255 broadcast).");
        Console.WriteLine("    --broadcast        envoie aussi en broadcast x.x.x.255 + 255.255.255.255.");
        Console.WriteLine("    --universes <n>    nombre d'univers balayes 0..n-1 (defaut: 16).");
        Console.WriteLine("    --color <r,g,b>    couleur de test (defaut: 255,255,255 = blanc plein).");
        Console.WriteLine("    --hold <s>         duree d'envoi en secondes (defaut: 5).");
        Console.WriteLine("    --step             allume un univers a la fois (pause entre chaque) pour");
        Console.WriteLine("                       identifier lequel controle quelle zone.");
        Console.WriteLine();
        Console.WriteLine("  Exemple : mappa scan --ip 192.168.1.1 --broadcast --universes 32");
        Console.WriteLine();
        Console.WriteLine("pixel : allume UNE position (x,y) du mur, pour calibrer l'orientation.");
        Console.WriteLine("  Usage : mappa pixel <config.json> --x <n> --y <n> [--ip .. --color r,g,b]");
        Console.WriteLine("  Exemple : mappa pixel ../configs/ecran.json --x 0 --y 0 --color 255,0,0");
        Console.WriteLine();
        Console.WriteLine("anim : anime le texte (clignote, haut/bas, grossit au centre, positions aleatoires).");
        Console.WriteLine("  Usage : mappa anim <config.json> \"TEXTE\" [options]");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --ip <adresse>     force l'IP du controleur.");
        Console.WriteLine("    --color <r,g,b>    couleur (defaut: 0,200,255).");
        Console.WriteLine("    --loops <n>        nombre de cycles complets (defaut: illimite, Ctrl+C).");
        Console.WriteLine("    --speed <f>        facteur de vitesse (defaut: 1.0 ; 2.0 = 2x plus rapide).");
        Console.WriteLine();
        Console.WriteLine("  Exemple : mappa anim ../configs/ecran.json \"CLAUDE\" --color 0,200,255");
        Console.WriteLine();
        Console.WriteLine("image : affiche une image (fichier PPM) ou un dessin de demo sur le mur.");
        Console.WriteLine("  Usage : mappa image <config.json> [--file x.ppm | --demo] [options]");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --file <x.ppm>     image a afficher (format PPM P3/P6).");
        Console.WriteLine("    --demo             dessine un motif genere par code (degrade + cercle).");
        Console.WriteLine("    --ip <adresse>     force l'IP du controleur.");
        Console.WriteLine("    --size <n>         cote max de l'image (defaut: 96 ; ratio conserve, centree).");
        Console.WriteLine("    --stretch          remplit tout le mur 128x128 (deforme l'image).");
        Console.WriteLine("    --brightness <f>   intensite 0..1 (defaut: 0.35 ; les LED sont tres vives).");
        Console.WriteLine("    --flip-x/--flip-y  inverse l'axe.");
        Console.WriteLine("    --keep-black       affiche aussi les pixels noirs (defaut: ignores).");
        Console.WriteLine("    --preview          apercu ASCII (niveaux de gris), sans envoi.");
        Console.WriteLine("    --once             envoie une seule frame.");
        Console.WriteLine();
        Console.WriteLine("  Convertir une image en PPM : magick photo.png -resize 128x128 photo.ppm");
        Console.WriteLine("  Exemple : mappa image ../configs/ecran.json --file photo.ppm");
        Console.WriteLine("  Exemple : mappa image ../configs/ecran.json --demo");
        Console.WriteLine();
        Console.WriteLine("import : genere une config JSON depuis un Excel d'adressage (.xlsx eHuB).");
        Console.WriteLine("  Usage : mappa import <panneau.xlsx> [options]");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --out <x.json>     fichier de sortie (defaut: <nom_excel>.json dans configs/).");
        Console.WriteLine("    --name <nom>       nom de la config (defaut: nom du fichier).");
        Console.WriteLine("    --sheet <feuille>  feuille a lire (defaut: la 1ere, ou 'eHuB' si presente).");
        Console.WriteLine();
        Console.WriteLine("  Exemple : mappa import panneau.xlsx --out ../configs/panneau.json");
        Console.WriteLine();
        Console.WriteLine("ramp : envoie une rampe 0->255->0 progressive sur UNE entite (canal DMX).");
        Console.WriteLine("       Sert a identifier pan/tilt d'une lyre : la lyre tourne visiblement");
        Console.WriteLine("       si le canal teste controle un mouvement, sinon rien ne bouge.");
        Console.WriteLine("  Usage : mappa ramp <config.json> --entity <id> [--ip .. --seconds N --hz 40]");
        Console.WriteLine("  Options :");
        Console.WriteLine("    --entity <id>      entite (= canal DMX) a ramper (obligatoire).");
        Console.WriteLine("    --seconds <n>      duree totale de la rampe aller-retour (defaut: 6).");
        Console.WriteLine("    --triangle         rampe 0->255->0 (defaut).");
        Console.WriteLine("    --saw              rampe 0->255 en boucle (dents de scie).");
        Console.WriteLine("    --ip <adresse>     force l'IP du controleur.");
        Console.WriteLine("    --hz <n>           frequence d'envoi (defaut: 40).");
        Console.WriteLine();
        Console.WriteLine("  Exemple pan lyre1 : mappa ramp configs/ecran.json --entity 10 --seconds 8");
        Console.WriteLine("  Puis tilt lyre1   : mappa ramp configs/ecran.json --entity 12 --seconds 8");
    }

    private static string ResolveConfigsDir()
    {
        // Cherche un dossier "configs" en remontant depuis le repertoire courant.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "configs");
            if (Directory.Exists(candidate)) return candidate;
            if (File.Exists(Path.Combine(dir.FullName, "README.md")) &&
                Directory.Exists(Path.Combine(dir.FullName, "csharp")))
            {
                return candidate; // racine du depot : on y creera configs/
            }
            dir = dir.Parent;
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "configs");
    }

    private static void AddShowLights(Config config, int baseUniverse = 33)
    {
        config.Devices.Add(new Device
        {
            Id = "static-1", Type = "static", Universe = baseUniverse,
            ChannelStart = 1, ChannelCount = 4,
        });
        int[] starts = { 10, 30, 50, 70 };
        for (int i = 0; i < starts.Length; i++)
        {
            config.Devices.Add(new Device
            {
                Id = $"lyre-{i + 1}", Type = "lyre", Universe = baseUniverse,
                ChannelStart = starts[i], ChannelCount = 13,
            });
        }
    }

    private static void SpreadOverControllers(Config config, (string Id, string Ip)[] controllers)
    {
        config.Controllers.Clear();
        foreach (var (id, ip) in controllers)
        {
            config.Controllers.Add(new Controller { Id = id, Ip = ip, Port = 6454, Outputs = 16 });
        }
        int n = controllers.Length;
        foreach (var u in config.Universes)
        {
            u.ControllerId = controllers[u.Output % n].Id;
        }
    }

    private static void GenerateExamples()
    {
        Directory.CreateDirectory(ConfigsDir);

        // 1) Config minimale : 2 LED (pour debloquer A).
        var mini = new Config("mini-2-leds");
        mini.Controllers.Add(new Controller { Id = "ctrl-1", Ip = "127.0.0.1", Port = 6454, Outputs = 16 });
        mini.Universes.Add(new Universe { Index = 0, ControllerId = "ctrl-1", Output = 0 });
        mini.Strips.Add(new Strip { Id = "strip-1", LedCount = 2, UniverseStart = 0, LedType = LedType.RGB });
        mini.EntityMap.Add(new EntityMapping { EntityStart = 1, EntityEnd = 2, UniverseStart = 0 });
        Persistence.SaveConfig(mini, Path.Combine(ConfigsDir, "mini.json"));

        // 2) Mur complet + lumieres du spectacle.
        var wall = Wall.BuildWallConfig(name: "wall-128x128");
        AddShowLights(wall);
        Persistence.SaveConfig(wall, Path.Combine(ConfigsDir, "wall.json"));

        // 3) Variante reduite.
        var small = Wall.BuildWallConfig(name: "wall-32x32", columns: 32, ledsPerColumn: 32);
        Persistence.SaveConfig(small, Path.Combine(ConfigsDir, "wall_small.json"));

        // 4) Forme non-2D : araignee.
        var (spider, positions) = Shapes.BuildSpiderConfig();
        Persistence.SaveConfig(spider, Path.Combine(ConfigsDir, "spider.json"));
        Shapes.SavePositions(positions, Path.Combine(ConfigsDir, "spider.positions.json"));

        // 5) Mur reparti sur 2 controleurs (scenario de panne).
        var dual = Wall.BuildWallConfig(name: "wall-dual-controllers");
        SpreadOverControllers(dual, new[] { ("bc216-A", "192.168.1.10"), ("bc216-B", "192.168.1.11") });
        Persistence.SaveConfig(dual, Path.Combine(ConfigsDir, "wall_dual.json"));

        foreach (var name in new[] { "mini.json", "wall.json", "wall_small.json", "spider.json", "wall_dual.json" })
        {
            var cfg = Persistence.LoadConfig(Path.Combine(ConfigsDir, name));
            Console.WriteLine($"[genere] {name,-20} : {cfg.EntityIds().Count,6} entites, " +
                              $"{cfg.Universes.Count,4} univers, {cfg.Devices.Count} appareils");
        }
    }

    private static void Show(string path)
    {
        var cfg = Persistence.LoadConfig(path);
        var plan = new RoutingPlan(cfg);
        var problems = cfg.Validate();
        Console.WriteLine($"Config     : {cfg.Name}");
        Console.WriteLine($"Controleurs: {cfg.Controllers.Count}");
        Console.WriteLine($"Univers    : {cfg.Universes.Count}");
        Console.WriteLine($"Bandes     : {cfg.Strips.Count}");
        Console.WriteLine($"Appareils  : {cfg.Devices.Count}");
        Console.WriteLine($"Entites    : {cfg.EntityIds().Count}");
        Console.WriteLine($"Univers routes : {plan.Universes.Count}");
        Console.WriteLine($"Validation : {(problems.Count == 0 ? "OK" : string.Join("; ", problems))}");
    }

    private static void Demo()
    {
        if (!File.Exists(Path.Combine(ConfigsDir, "wall_small.json"))) GenerateExamples();

        RoutingPlan? plan = null;
        State? state = null;

        var mgr = new ConfigManager(Path.Combine(ConfigsDir, "wall_small.json"));
        mgr.OnReload += cfg =>
        {
            plan = new RoutingPlan(cfg);
            state = State.FromConfig(cfg);
            Console.WriteLine($"  -> reconfiguration : {cfg.Name} " +
                              $"({cfg.EntityIds().Count} entites, {plan.Universes.Count} univers)");
        };

        Console.WriteLine("Chargement config initiale...");
        mgr.Load();

        // L'authoring (C) ecrit dans le state.
        foreach (var eid in state!.EntityIds.Take(5)) state.Set(eid, 255, 0, 0);
        state.MarkUpdated();

        // Le routage (A) projette le state sur les univers DMX.
        var packets = plan!.Render(state);
        int firstU = plan.Universes[0];
        Console.WriteLine($"  premier univers {firstU} -> 6 premiers octets: " +
                          $"[{string.Join(", ", packets[firstU].Take(6))}]");

        Console.WriteLine("Reconfiguration en direct...");
        mgr.Reload(Path.Combine(ConfigsDir, "wall.json"));
        Console.WriteLine("Demo terminee.");
    }

    private static void FailoverDemo()
    {
        if (!File.Exists(Path.Combine(ConfigsDir, "wall_dual.json"))) GenerateExamples();

        var cfg = Persistence.LoadConfig(Path.Combine(ConfigsDir, "wall_dual.json"));
        var planBefore = new RoutingPlan(cfg);

        int sampleUniverse = planBefore.Universes[1];
        var ctrlBefore = Failover.ControllerOfUniverse(cfg, sampleUniverse);

        Console.WriteLine("Avant panne :");
        foreach (var c in cfg.Controllers)
        {
            int owned = cfg.Universes.Count(u => u.ControllerId == c.Id);
            Console.WriteLine($"  {c.Id} ({c.Ip}) pilote {owned} univers");
        }
        Console.WriteLine($"  univers temoin {sampleUniverse} -> {ctrlBefore!.Id} @ {ctrlBefore.Ip}");

        Console.WriteLine("\n>> PANNE de bc216-A. Redeploiement vers bc216-C (192.168.1.12)...");
        var moved = Failover.ReplaceController(cfg, "bc216-A", "bc216-C", "192.168.1.12");
        Console.WriteLine($"   {moved.Count} univers redeployes.");

        var planAfter = new RoutingPlan(cfg);
        var ctrlAfter = Failover.ControllerOfUniverse(cfg, sampleUniverse);

        Console.WriteLine("\nApres redeploiement :");
        foreach (var c in cfg.Controllers)
        {
            int owned = cfg.Universes.Count(u => u.ControllerId == c.Id);
            Console.WriteLine($"  {c.Id} ({c.Ip}) pilote {owned} univers");
        }
        Console.WriteLine($"  univers temoin {sampleUniverse} -> {ctrlAfter!.Id} @ {ctrlAfter.Ip}");

        bool sameAddressing = cfg.EntityIds().All(e =>
        {
            var a = planBefore.AddressOf(e);
            var b = planAfter.AddressOf(e);
            return a.HasValue && b.HasValue &&
                   a.Value.Universe == b.Value.Universe && a.Value.Channel == b.Value.Channel;
        });
        Console.WriteLine($"\nAdressage entite->univers/canal inchange : {sameAddressing}");
        var problems = cfg.Validate();
        Console.WriteLine($"Validation apres redeploiement : {(problems.Count == 0 ? "OK" : string.Join("; ", problems))}");

        Persistence.SaveConfig(cfg, Path.Combine(ConfigsDir, "wall_dual_after_failover.json"));
        Console.WriteLine("Config redeployee sauvegardee -> configs/wall_dual_after_failover.json");
    }

    // ------------------------------------------------------------------ //
    // send : boucle d'envoi ArtNet reel (jalon "allumer 1 LED reelle").
    //
    // Reutilise tout le pipeline de prod : LoadConfig -> RoutingPlan ->
    // State (ecriture couleur) -> Render -> ArtNetSender.SendPlan.
    // ------------------------------------------------------------------ //
    private static int Send(string[] args)
    {
        string configPath = args[1];
        string? overrideIp = null;
        int? onlyEntity = null;
        byte r = 255, g = 255, b = 255;
        int hz = 40;
        int? frames = null;
        bool once = false;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--entity" when i + 1 < args.Length: onlyEntity = int.Parse(args[++i]); break;
                case "--hz" when i + 1 < args.Length: hz = int.Parse(args[++i]); break;
                case "--frames" when i + 1 < args.Length: frames = int.Parse(args[++i]); break;
                case "--once": once = true; break;
                case "--color" when i + 1 < args.Length:
                    if (!TryParseColor(args[++i], out r, out g, out b)) return 1;
                    break;
                default:
                    Console.Error.WriteLine($"Option inconnue : {args[i]}");
                    return 1;
            }
        }

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;
        var problems = config.Validate();
        if (problems.Count > 0)
        {
            Console.Error.WriteLine("Config invalide : " + string.Join("; ", problems));
            return 1;
        }

        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);

        var targets = onlyEntity.HasValue
            ? new List<int> { onlyEntity.Value }
            : new List<int>(state.EntityIds);
        foreach (var eid in targets)
        {
            if (!state.Contains(eid))
            {
                Console.Error.WriteLine($"Entite {eid} absente de la config.");
                return 1;
            }
            state.SetRgb(eid, r, g, b);
        }
        state.MarkUpdated();

        Console.WriteLine($"Config      : {config.Name}");
        foreach (var c in config.Controllers)
        {
            Console.WriteLine($"Controleur  : {c.Id} -> {c.Ip}:{c.Port}");
        }
        Console.WriteLine($"Entites     : {targets.Count} allumee(s) en RVB=({r},{g},{b})");
        Console.WriteLine($"Univers     : {plan.Universes.Count} envoye(s) par frame");

        using var sender = new ArtNetSender();

        if (once || frames == 1)
        {
            var pkts = plan.Render(state);
            sender.SendPlan(config, pkts);
            Console.WriteLine("1 frame envoyee.");
            return 0;
        }

        int periodMs = hz > 0 ? Math.Max(1, 1000 / hz) : 25;
        bool running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };
        Console.WriteLine(frames.HasValue
            ? $"Envoi de {frames} frames a {hz} Hz..."
            : $"Envoi en boucle a {hz} Hz (Ctrl+C pour arreter et eteindre)...");

        int sent = 0;
        while (running && (!frames.HasValue || sent < frames.Value))
        {
            var pkts = plan.Render(state);
            sender.SendPlan(config, pkts);
            sent++;
            System.Threading.Thread.Sleep(periodMs);
        }

        // Extinction propre : on renvoie quelques frames noires.
        state.Clear();
        state.MarkUpdated();
        for (int i = 0; i < 3; i++)
        {
            var pkts = plan.Render(state);
            sender.SendPlan(config, pkts);
            System.Threading.Thread.Sleep(periodMs);
        }
        Console.WriteLine($"\nArrete apres {sent} frames. LEDs eteintes.");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // scan : DIAGNOSTIC. Remplit tous les canaux de plusieurs univers avec une
    // couleur vive, en unicast et/ou broadcast, pour faire reagir n'importe
    // quelle LED quel que soit le mapping/univers attendu par le controleur.
    // ------------------------------------------------------------------ //
    private static int Scan(string[] args)
    {
        string ip = "255.255.255.255";
        bool alsoBroadcast = false;
        int universes = 16;
        int startUniverse = 0;
        byte r = 255, g = 255, b = 255;
        int holdSec = 5;
        bool step = false;
        int port = 6454;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: ip = args[++i]; break;
                case "--broadcast": alsoBroadcast = true; break;
                case "--universes" when i + 1 < args.Length: universes = int.Parse(args[++i]); break;
                case "--start" when i + 1 < args.Length: startUniverse = int.Parse(args[++i]); break;
                case "--only" when i + 1 < args.Length:
                    startUniverse = int.Parse(args[++i]);
                    universes = startUniverse + 1;
                    break;
                case "--hold" when i + 1 < args.Length: holdSec = int.Parse(args[++i]); break;
                case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
                case "--step": step = true; break;
                case "--color" when i + 1 < args.Length:
                    if (!TryParseColor(args[++i], out r, out g, out b)) return 1;
                    break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        // Buffer DMX plein : on repete le motif RGB sur les 512 canaux, comme ca
        // on couvre RGB comme RGBW et n'importe quel offset de canal.
        var dmx = new byte[Config.DmxChannelsPerUniverse];
        for (int c = 0; c + 2 < dmx.Length; c += 3)
        {
            dmx[c] = r; dmx[c + 1] = g; dmx[c + 2] = b;
        }

        var targets = new List<string> { ip };
        if (alsoBroadcast)
        {
            targets.Add("255.255.255.255");
            // broadcast dirige du /24 courant (ex: 192.168.1.255)
            var octets = ip.Split('.');
            if (octets.Length == 4)
            {
                octets[3] = "255";
                targets.Add(string.Join(".", octets));
            }
        }

        Console.WriteLine($"SCAN diagnostic");
        Console.WriteLine($"  Cibles   : {string.Join(", ", targets)} : {port}");
        Console.WriteLine($"  Univers  : {startUniverse}..{universes - 1}");
        Console.WriteLine($"  Couleur  : RVB=({r},{g},{b}) sur tous les canaux");
        Console.WriteLine($"  Mode     : {(step ? "pas-a-pas" : "tous en meme temps")}, {holdSec}s");
        Console.WriteLine();

        using var sender = new ArtNetSender();
        bool running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

        void Blast(int u)
        {
            foreach (var t in targets) sender.Send(t, u, dmx, port);
        }

        if (step)
        {
            for (int u = startUniverse; u < universes && running; u++)
            {
                Console.WriteLine($"  -> univers {u} (Ctrl+C pour arreter)");
                var until = DateTime.UtcNow.AddSeconds(holdSec);
                while (running && DateTime.UtcNow < until)
                {
                    Blast(u);
                    System.Threading.Thread.Sleep(25);
                }
            }
        }
        else
        {
            var until = DateTime.UtcNow.AddSeconds(holdSec);
            int frames = 0;
            while (running && DateTime.UtcNow < until)
            {
                for (int u = startUniverse; u < universes; u++) Blast(u);
                frames++;
                System.Threading.Thread.Sleep(25);
            }
            Console.WriteLine($"  {frames} salves envoyees sur univers {startUniverse}..{universes - 1}.");
        }

        // Extinction.
        var black = new byte[Config.DmxChannelsPerUniverse];
        for (int rep = 0; rep < 3; rep++)
        {
            for (int u = startUniverse; u < universes; u++)
                foreach (var t in targets) sender.Send(t, u, black, port);
            System.Threading.Thread.Sleep(25);
        }
        Console.WriteLine("Termine. LEDs eteintes.");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // text : ecrit du texte (police 3x5) sur le mur puis l'envoie en ArtNet.
    // ------------------------------------------------------------------ //
    private static int TextCmd(string[] args)
    {
        string configPath = args[1];
        string message = args[2];
        string? overrideIp = null;
        byte r = 255, g = 255, b = 255;
        int x = 0, y = 0;
        bool flipX = false, flipY = false;
        bool preview = false, once = false;
        int hz = 40;
        double brightness = 1.0;

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--x" when i + 1 < args.Length: x = int.Parse(args[++i]); break;
                case "--y" when i + 1 < args.Length: y = int.Parse(args[++i]); break;
                case "--hz" when i + 1 < args.Length: hz = int.Parse(args[++i]); break;
                case "--flip-x": flipX = true; break;
                case "--flip-y": flipY = true; break;
                case "--preview": preview = true; break;
                case "--once": once = true; break;
                case "--brightness" when i + 1 < args.Length:
                    brightness = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--color" when i + 1 < args.Length:
                    if (!TryParseColor(args[++i], out r, out g, out b)) return 1;
                    break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        if (brightness < 0) brightness = 0;
        r = (byte)Math.Clamp((int)(r * brightness), 0, 255);
        g = (byte)Math.Clamp((int)(g * brightness), 0, 255);
        b = (byte)Math.Clamp((int)(b * brightness), 0, 255);

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;

        var geo = WallGeometry.Infer(config);
        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);

        int lit = Text.DrawString(state, message, x, y, r, g, b, geo,
            flipX: flipX, flipY: flipY);

        Console.WriteLine($"Texte    : \"{message}\"");
        Console.WriteLine($"Mur      : {geo.Columns} x {geo.Rows}, origine ({x},{y})");
        Console.WriteLine($"Couleur  : ({r},{g},{b}) @ {brightness:P0}");
        Console.WriteLine($"Pixels   : {lit} LED(s) allumee(s)");
        if (lit == 0)
        {
            Console.Error.WriteLine("Aucun pixel allume : verifie que les IDs du mur existent dans la config.");
        }

        if (preview)
        {
            PreviewAscii(state, geo, Math.Min(geo.Rows, y + 6 * CountLines(message, geo.Columns, x)));
            return 0;
        }

        using var sender = new ArtNetSender();
        if (once)
        {
            sender.SendPlan(config, plan.Render(state));
            Console.WriteLine("1 frame envoyee.");
            return 0;
        }

        RunRenderLoop(config, plan, state, sender, hz,
            label: $"Envoi en boucle a {hz} Hz vers {string.Join(", ", config.Controllers.ConvertAll(c => c.Ip))} (Ctrl+C pour arreter)...");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // pixel : allume une seule position logique (x,y) du mur. Sert a calibrer
    // l'orientation sur le vrai panneau (ou est (0,0), sens des axes...).
    // ------------------------------------------------------------------ //
    private static int Pixel(string[] args)
    {
        string configPath = args[1];
        string? overrideIp = null;
        int x = 0, y = 0;
        byte r = 255, g = 255, b = 255;
        int hz = 40;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--x" when i + 1 < args.Length: x = int.Parse(args[++i]); break;
                case "--y" when i + 1 < args.Length: y = int.Parse(args[++i]); break;
                case "--hz" when i + 1 < args.Length: hz = int.Parse(args[++i]); break;
                case "--color" when i + 1 < args.Length:
                    if (!TryParseColor(args[++i], out r, out g, out b)) return 1;
                    break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;

        var geo = WallGeometry.Infer(config);
        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);

        int id = geo.EntityId(x, y);
        Console.WriteLine($"Position ({x},{y}) -> entite {id}");
        if (id < 0 || !state.Contains(id))
        {
            Console.Error.WriteLine("Entite hors config (fixation invisible ou hors mur).");
            return 1;
        }
        state.SetRgb(id, r, g, b);

        using var sender = new ArtNetSender();
        RunRenderLoop(config, plan, state, sender, hz,
            label: $"Envoi pixel en RVB=({r},{g},{b}) (Ctrl+C pour arreter)...");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // anim : sequence d'effets sur le texte, envoyee en ArtNet a ~40 Hz.
    //   1. clignotement en haut
    //   2. apparition en haut fixe
    //   3. apparition en bas fixe
    //   4. grossissement (scale 1->max) centre
    //   5. sauts a des positions aleatoires
    // ------------------------------------------------------------------ //
    private static int Anim(string[] args)
    {
        string configPath = args[1];
        string message = args[2];
        string? overrideIp = null;
        byte r = 0, g = 200, b = 255;
        int? loops = null;
        double speed = 1.0;

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--loops" when i + 1 < args.Length: loops = int.Parse(args[++i]); break;
                case "--speed" when i + 1 < args.Length: speed = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "--color" when i + 1 < args.Length:
                    if (!TryParseColor(args[++i], out r, out g, out b)) return 1;
                    break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;

        var geo = WallGeometry.Infer(config);
        int cols = geo.Columns, rows = geo.Rows;
        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);
        using var sender = new ArtNetSender();

        const int fps = 40;
        if (speed <= 0) speed = 1.0;
        int frameMs = Math.Max(1, (int)(1000.0 / fps));

        bool running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

        var rng = new Random();
        int textW1 = Text.MeasureWidth(message, 1);          // largeur a l'echelle 1
        int textH1 = Text.MeasureHeight(1);
        int centerX = (cols - textW1) / 2;

        // Envoie l'etat courant une fois.
        void Flush() => sender.SendPlan(config, plan.Render(state));

        // Maintient une image fixe pendant "frames" trames.
        bool HoldFrames(int frames)
        {
            for (int f = 0; f < frames && running; f++)
            {
                Flush();
                System.Threading.Thread.Sleep(frameMs);
            }
            return running;
        }

        int F(int n) => Math.Max(1, (int)(n / speed)); // convertit un nb de trames selon la vitesse

        Console.WriteLine($"Animation de \"{message}\" (Ctrl+C pour arreter)...");

        int done = 0;
        while (running && (!loops.HasValue || done < loops.Value))
        {
            // --- Phase 1 : clignotement en haut ---
            for (int blink = 0; blink < 6 && running; blink++)
            {
                state.Clear();
                if (blink % 2 == 0)
                    Text.DrawStringScaled(state, message, centerX, 2, r, g, b, 1, 1, geo);
                HoldFrames(F(6));
            }

            // --- Phase 2 : apparition en haut, fixe ---
            state.Clear();
            Text.DrawStringScaled(state, message, centerX, 2, r, g, b, 1, 1, geo);
            if (!HoldFrames(F(20))) break;

            // --- Phase 3 : apparition en bas, fixe ---
            state.Clear();
            Text.DrawStringScaled(state, message, centerX, rows - textH1 - 2, r, g, b, 1, 1, geo);
            if (!HoldFrames(F(20))) break;

            // --- Phase 4 : grossissement centre (scale 1 -> maxScale) ---
            int maxScale = Math.Max(1, Math.Min(cols / Math.Max(1, textW1), rows / Math.Max(1, textH1)));
            for (int s = 1; s <= maxScale && running; s++)
            {
                int w = Text.MeasureWidth(message, s);
                int h = Text.MeasureHeight(s);
                int ox = (cols - w) / 2;
                int oy = (rows - h) / 2;
                state.Clear();
                Text.DrawStringScaled(state, message, ox, oy, r, g, b, s, 1, geo);
                HoldFrames(F(8));
            }
            // petite pause a la taille max
            HoldFrames(F(10));

            // --- Phase 5 : sauts aleatoires ---
            for (int j = 0; j < 8 && running; j++)
            {
                int scale = 1 + rng.Next(0, Math.Max(1, maxScale));
                int w = Text.MeasureWidth(message, scale);
                int h = Text.MeasureHeight(scale);
                int ox = w >= cols ? 0 : rng.Next(0, cols - w);
                int oy = h >= rows ? 0 : rng.Next(0, rows - h);
                // couleur aleatoire vive a chaque saut
                byte rr = (byte)rng.Next(64, 256), gg = (byte)rng.Next(64, 256), bb = (byte)rng.Next(64, 256);
                state.Clear();
                Text.DrawStringScaled(state, message, ox, oy, rr, gg, bb, scale, 1, geo);
                HoldFrames(F(10));
            }

            done++;
        }

        // Extinction propre.
        state.Clear();
        for (int i = 0; i < 3; i++) { Flush(); System.Threading.Thread.Sleep(frameMs); }
        Console.WriteLine("\nArrete. LEDs eteintes.");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // import : lit un Excel d'adressage (.xlsx eHuB) et genere la config JSON.
    // Permet de reagir dynamiquement a differents panneaux sans coder.
    // ------------------------------------------------------------------ //
    private static int Import(string[] args)
    {
        string xlsxPath = args[1];
        string? outPath = null;
        string? name = null;
        string? sheet = null;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
                case "--name" when i + 1 < args.Length: name = args[++i]; break;
                case "--sheet" when i + 1 < args.Length: sheet = args[++i]; break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        if (!File.Exists(xlsxPath))
        {
            Console.Error.WriteLine($"Excel introuvable : {xlsxPath}");
            return 1;
        }

        name ??= Path.GetFileNameWithoutExtension(xlsxPath);
        outPath ??= Path.Combine(ConfigsDir, name + ".json");

        List<EhubRow> rows;
        try { rows = EhubXlsx.Read(xlsxPath, sheet); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Lecture de l'Excel impossible : {ex.Message}");
            return 1;
        }

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Aucune ligne d'adressage valide trouvee dans l'Excel.");
            return 1;
        }

        var config = ConfigBuilder.BuildFromEhub(rows, name);
        var problems = config.Validate();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        Persistence.SaveConfig(config, outPath);

        var geo = WallGeometry.Infer(config);

        Console.WriteLine($"Importe    : {xlsxPath}");
        Console.WriteLine($"Lignes     : {rows.Count} plages d'entites");
        Console.WriteLine($"Controleurs: {config.Controllers.Count} ({string.Join(", ", config.Controllers.ConvertAll(c => c.Ip))})");
        Console.WriteLine($"Univers    : {config.Universes.Count}");
        Console.WriteLine($"Entites    : {config.EntityIds().Count}");
        Console.WriteLine($"Geometrie  : {geo.Columns} x {geo.Rows} " +
                          $"({(geo.Serpentine ? "serpentin" : "simple")}, {geo.LedsPerBand} LED/bande, " +
                          $"{geo.BandsPerController} bandes/controleur)");
        Console.WriteLine($"Validation : {(problems.Count == 0 ? "OK" : string.Join("; ", problems))}");
        Console.WriteLine($"-> ecrit : {outPath}");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // image : affiche une image PPM (ou un dessin de demo) sur le mur.
    // ------------------------------------------------------------------ //
    private static int ImageCmd(string[] args)
    {
        string configPath = args[1];
        string? file = null;
        string? overrideIp = null;
        bool demo = false, preview = false, once = false, keepBlack = false;
        bool flipX = false, flipY = false;
        bool stretch = false;                 // deformer pour remplir (sinon ratio conserve)
        int box = 96;                         // cote max de la zone d'affichage (defaut < mur -> plus net)
        double brightness = 0.35;             // intensite (les LED sont tres lumineuses)
        int hz = 40;

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--file" when i + 1 < args.Length: file = args[++i]; break;
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--hz" when i + 1 < args.Length: hz = int.Parse(args[++i]); break;
                case "--demo": demo = true; break;
                case "--preview": preview = true; break;
                case "--once": once = true; break;
                case "--keep-black": keepBlack = true; break;
                case "--flip-x": flipX = true; break;
                case "--flip-y": flipY = true; break;
                case "--stretch": stretch = true; break;
                case "--size" when i + 1 < args.Length: box = int.Parse(args[++i]); break;
                case "--brightness" when i + 1 < args.Length:
                    brightness = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        if (file == null && !demo)
        {
            Console.Error.WriteLine("Precise --file <x.ppm> ou --demo.");
            return 1;
        }

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;

        var geo = WallGeometry.Infer(config);
        int cols = geo.Columns, rows = geo.Rows;

        ImageArt img;
        if (demo)
        {
            // Motif genere par code : degrade + cercle + croix.
            img = new ImageArt(cols, rows);
            img.GradientVertical(0, 0, 80, 0, 120, 255);
            img.Circle(cols / 2, rows / 2, 40, 255, 200, 0, filled: true);
            img.Circle(cols / 2, rows / 2, 40, 255, 255, 255, filled: false);
            img.Line(0, 0, cols - 1, rows - 1, 255, 0, 0);
            img.Line(0, rows - 1, cols - 1, 0, 0, 255, 0);
        }
        else
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"Image introuvable : {file}");
                return 1;
            }
            try { img = ImageArt.LoadPpm(file!); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Lecture PPM impossible : {ex.Message}");
                return 1;
            }
        }

        // Calcule la taille cible : par defaut on garde le ratio de l'image et on
        // la fait tenir dans un carre "box" (< mur => image plus petite, plus nette),
        // centree sur le mur. --stretch force le remplissage complet (deforme).
        box = Math.Max(1, Math.Min(box, Math.Min(cols, rows)));
        int fitW, fitH;
        if (stretch)
        {
            fitW = cols; fitH = rows;
        }
        else
        {
            double ar = (double)img.Width / img.Height;
            if (ar >= 1.0) { fitW = box; fitH = Math.Max(1, (int)(box / ar)); }
            else { fitH = box; fitW = Math.Max(1, (int)(box * ar)); }
        }
        int originX = (cols - fitW) / 2;
        int originY = (rows - fitH) / 2;

        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);
        int lit = img.BlitToState(state, originX, originY, fitW, fitH, geo,
            flipX: flipX, flipY: flipY, skipBlack: !keepBlack, brightness: brightness);

        Console.WriteLine($"Image    : {(demo ? "(demo generee)" : file)} {img.Width}x{img.Height} -> {fitW}x{fitH} @ ({originX},{originY})");
        Console.WriteLine($"Intensite: {brightness:P0}");
        Console.WriteLine($"Pixels   : {lit} LED(s) allumee(s)");
        if (lit == 0)
        {
            Console.Error.WriteLine("Aucun pixel allume (image toute noire, ou hors mur).");
        }

        if (preview)
        {
            PreviewAscii(state, geo, rows);
            return 0;
        }

        using var sender = new ArtNetSender();
        if (once)
        {
            sender.SendPlan(config, plan.Render(state));
            Console.WriteLine("1 frame envoyee.");
            return 0;
        }
        RunRenderLoop(config, plan, state, sender, hz,
            label: $"Envoi image en boucle a {hz} Hz (Ctrl+C pour arreter)...");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // ramp : envoie une rampe temporelle 0->255->0 sur UNE entite (=1 canal
    // DMX). Sert a identifier les canaux de mouvement (pan/tilt) sur une
    // lyre : si le canal fait bouger, on voit un mouvement continu au lieu
    // d'un simple "on/off". Pratique pour distinguer pan (mouvement horizontal
    // lisse) de tilt (vertical lisse) de dimmer (variation d'intensite).
    // ------------------------------------------------------------------ //
    private static int Ramp(string[] args)
    {
        string configPath = args[1];
        string? overrideIp = null;
        int? entity = null;
        double seconds = 6.0;
        int hz = 40;
        bool saw = false; // false = triangle 0->255->0, true = dents de scie

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--entity" when i + 1 < args.Length: entity = int.Parse(args[++i]); break;
                case "--seconds" when i + 1 < args.Length:
                    seconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--hz" when i + 1 < args.Length: hz = int.Parse(args[++i]); break;
                case "--triangle": saw = false; break;
                case "--saw": saw = true; break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        if (!entity.HasValue)
        {
            Console.Error.WriteLine("--entity <id> obligatoire (ex: --entity 10 pour le 1er canal de lyre1)");
            return 1;
        }
        if (seconds <= 0) seconds = 6.0;

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;

        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);
        if (!state.Contains(entity.Value))
        {
            Console.Error.WriteLine($"Entite {entity.Value} absente de la config.");
            return 1;
        }

        int periodMs = hz > 0 ? Math.Max(1, 1000 / hz) : 25;
        int totalFrames = (int)(seconds * hz);
        bool running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

        Console.WriteLine($"Ramp entite {entity.Value} : {(saw ? "saw" : "triangle")} sur {seconds:F1}s a {hz} Hz");
        Console.WriteLine("Regarde la lyre : mouvement continu = canal de mouvement (pan/tilt).");
        Console.WriteLine("                  Variation d'intensite = dimmer.");
        Console.WriteLine("                  Rien = canal inactif/inconnu.");

        using var sender = new ArtNetSender();
        for (int f = 0; f < totalFrames && running; f++)
        {
            double t = (double)f / totalFrames; // 0..1
            byte v;
            if (saw)
            {
                v = (byte)(t * 255.0);
            }
            else
            {
                // triangle : 0..1..0
                double tri = t < 0.5 ? t * 2.0 : (1.0 - t) * 2.0;
                v = (byte)(tri * 255.0);
            }
            state.Set(entity.Value, v, 0, 0);
            state.MarkUpdated();
            sender.SendPlan(config, plan.Render(state));
            System.Threading.Thread.Sleep(periodMs);
        }

        // Extinction propre.
        state.Clear();
        for (int i = 0; i < 3; i++)
        {
            sender.SendPlan(config, plan.Render(state));
            System.Threading.Thread.Sleep(periodMs);
        }
        Console.WriteLine("Termine. Canal remis a zero.");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // preset : allume simultanement plusieurs entites avec des valeurs
    // differentes, en boucle a 40 Hz. Utile pour poser une "pose" (lyre
    // dirigee vers le ciel + dimmer + couleur) sans que chaque canal ne
    // s'ecrase mutuellement.
    //
    // Usage :
    //   mappa preset <config.json> --ip <IP> --set <e=v[,e=v,...]> [--hold <sec>]
    //
    // Exemple : Lyre 1 vers le ciel, blanc, dimmer 100% + projo blanc pendant 8s :
    //   mappa preset configs/ecran.json --ip 192.168.1.48 --hold 8 \
    //     --set 1=255,2=255,3=255,\
    //           10=128,11=0,12=255,13=255,14=128,15=255,17=255,18=255,19=255
    // ------------------------------------------------------------------ //
    private static int Preset(string[] args)
    {
        string configPath = args[1];
        string? overrideIp = null;
        double holdSec = 5.0;
        int hz = 40;
        var pairs = new List<(int Entity, byte Value)>();

        for (int i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--ip" when i + 1 < args.Length: overrideIp = args[++i]; break;
                case "--hold" when i + 1 < args.Length:
                    holdSec = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--hz" when i + 1 < args.Length: hz = int.Parse(args[++i]); break;
                case "--set" when i + 1 < args.Length:
                    foreach (var chunk in args[++i].Split(','))
                    {
                        var kv = chunk.Split('=');
                        if (kv.Length != 2 || !int.TryParse(kv[0], out int eid) || !byte.TryParse(kv[1], out byte val))
                        {
                            Console.Error.WriteLine($"--set attend 'e=v,e=v,...' (ex: 15=255,10=128). Erreur sur '{chunk}'");
                            return 1;
                        }
                        pairs.Add((eid, val));
                    }
                    break;
                default: Console.Error.WriteLine($"Option inconnue : {args[i]}"); return 1;
            }
        }

        if (pairs.Count == 0)
        {
            Console.Error.WriteLine("--set <entity=value,...> obligatoire.");
            return 1;
        }

        var config = LoadConfigWithIp(configPath, overrideIp);
        if (config == null) return 1;
        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);

        foreach (var (eid, val) in pairs)
        {
            if (!state.Contains(eid))
            {
                Console.Error.WriteLine($"Entite {eid} absente de la config.");
                return 1;
            }
            state.Set(eid, val, 0, 0);
        }
        state.MarkUpdated();

        Console.WriteLine($"Preset : {pairs.Count} canaux poses simultanement.");
        foreach (var (eid, val) in pairs)
        {
            var addr = plan.AddressOf(eid);
            if (addr.HasValue)
                Console.WriteLine($"  entite {eid,4} = {val,3}  -> univers {addr.Value.Universe}, canal DMX {addr.Value.Channel + 1}");
        }
        Console.WriteLine($"Envoi a {hz} Hz pendant {holdSec:F1}s (Ctrl+C pour arreter).");

        int periodMs = hz > 0 ? Math.Max(1, 1000 / hz) : 25;
        int totalFrames = (int)(holdSec * hz);
        bool running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };

        using var sender = new ArtNetSender();
        for (int f = 0; f < totalFrames && running; f++)
        {
            sender.SendPlan(config, plan.Render(state));
            System.Threading.Thread.Sleep(periodMs);
        }

        // Extinction propre.
        state.Clear();
        state.MarkUpdated();
        for (int i = 0; i < 3; i++)
        {
            sender.SendPlan(config, plan.Render(state));
            System.Threading.Thread.Sleep(periodMs);
        }
        Console.WriteLine("Termine. Canaux remis a zero.");
        return 0;
    }

    // ------------------------------------------------------------------ //
    // Helpers partages par les commandes d'envoi (send/text/pixel/anim/scan).
    // ------------------------------------------------------------------ //

    /// <summary>Parse "r,g,b" (0-255). Affiche une erreur et renvoie false si invalide.</summary>
    private static bool TryParseColor(string value, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var parts = value.Split(',');
        if (parts.Length != 3 ||
            !byte.TryParse(parts[0], out r) ||
            !byte.TryParse(parts[1], out g) ||
            !byte.TryParse(parts[2], out b))
        {
            Console.Error.WriteLine("--color attend r,g,b (ex: 255,0,0)");
            return false;
        }
        return true;
    }

    /// <summary>Verifie l'existence du fichier, charge la config, applique un override d'IP.</summary>
    private static Config? LoadConfigWithIp(string path, string? overrideIp)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Config introuvable : {path}");
            return null;
        }
        var config = Persistence.LoadConfig(path);
        if (overrideIp != null)
            foreach (var c in config.Controllers) c.Ip = overrideIp;
        return config;
    }

    /// <summary>
    /// Boucle d'envoi ArtNet (state fige) a <paramref name="hz"/> Hz jusqu'a
    /// Ctrl+C, puis extinction propre (quelques frames noires).
    /// </summary>
    private static void RunRenderLoop(
        Config config, RoutingPlan plan, State state, ArtNetSender sender,
        int hz, string? label = null)
    {
        int periodMs = hz > 0 ? Math.Max(1, 1000 / hz) : 25;
        bool running = true;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; running = false; };
        if (label != null) Console.WriteLine(label);

        while (running)
        {
            sender.SendPlan(config, plan.Render(state));
            System.Threading.Thread.Sleep(periodMs);
        }

        state.Clear();
        for (int i = 0; i < 3; i++)
        {
            sender.SendPlan(config, plan.Render(state));
            System.Threading.Thread.Sleep(periodMs);
        }
        Console.WriteLine("\nArrete. LEDs eteintes.");
    }

    private static int CountLines(string message, int cols, int originX)
    {
        int advance = Text.GlyphWidth + 1;
        int cursorX = originX;
        int lines = 1;
        foreach (char ch in message)
        {
            if (ch == '\n') { cursorX = originX; lines++; continue; }
            if (cursorX + Text.GlyphWidth > cols && cursorX > originX)
            {
                cursorX = originX;
                lines++;
            }
            cursorX += advance;
        }
        return lines;
    }

    // Apercu ASCII : rampe de luminosite (' ' = eteint -> '@' = tres lumineux).
    // Aide a valider le rendu (texte comme image) avant l'envoi : orientation,
    // cadrage, retour a la ligne...
    private static readonly char[] LumRamp = { ' ', '.', ':', '-', '=', '+', '*', '#', '%', '@' };

    private static void PreviewAscii(State state, WallGeometry geo, int rowsToShow)
    {
        int cols = geo.Columns;
        for (int row = 0; row < rowsToShow; row++)
        {
            var sb = new System.Text.StringBuilder(cols);
            for (int col = 0; col < cols; col++)
            {
                int id = geo.EntityId(col, row);
                int lum = 0;
                if (id >= 0 && state.Contains(id))
                {
                    var c = state.Get(id);
                    // luminance perceptuelle approximative (0-255)
                    lum = (c.R * 30 + c.G * 59 + c.B * 11) / 100;
                }
                int idx = lum * (LumRamp.Length - 1) / 255;
                sb.Append(LumRamp[idx]);
            }
            Console.WriteLine(sb.ToString());
        }
    }
}
