using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Mappa;

namespace Mappa.Ui
{
    /// <summary>
    /// Fenetre principale : panneau de controle + visualiseur DMX (P8).
    ///
    /// L'UI est un simple "consommateur" : un DispatcherTimer (~30 img/s) copie le
    /// dernier snapshot du RoutingEngine et le dessine dans un WriteableBitmap.
    /// On ne cree PAS un controle par LED (16 384 controles ecrouleraient le rendu)
    /// : on ecrit directement les pixels d'un seul bitmap -> tient 40 Hz facilement.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int Cell = 4;        // taille d'une LED a l'ecran (px)
        private const int LedsPerRow = 170; // 512 / 3 = 170 LED RVB par univers
        private const int WallCell = 4;    // taille d'une LED dans la vue mur (px)

        private RoutingEngine? _engine;
        private WriteableBitmap? _bmp;
        private byte[] _snap = Array.Empty<byte>();
        private int _bmpUniverseCount = -1;
        private bool _bmpIsWall;

        // Vue mur : pour chaque (col,row), l'offset du canal rouge dans le snapshot
        // DMX a plat, ou -1 si la position ne porte pas de LED (fixation).
        // Precalcule une fois par config : le rendu reste un simple memcpy indexe.
        private int[] _wallOffsets = Array.Empty<int>();
        private int _wallCols, _wallRows;

        // Destinations reellement pilotees par la config courante. Affiche tel quel :
        // c'est la seule facon de distinguer "notre app envoie" d'une source tierce.
        private string _targets = "—";
        private readonly DispatcherTimer _uiTimer;

        public MainWindow()
        {
            InitializeComponent();

            ConfigPath.Text = FindDefaultConfig() ?? "";

            LoadBtn.Click += (_, _) => LoadConfig();
            ImportXlsxBtn.Click += async (_, _) => await ImportXlsxAsync();
            StartBtn.Click += (_, _) => StartRouting();
            StopBtn.Click += (_, _) => StopRouting();
            SendChk.IsCheckedChanged += (_, _) =>
            {
                if (_engine != null) _engine.SendArtNet = SendChk.IsChecked ?? false;
            };
            BlackoutChk.IsCheckedChanged += (_, _) =>
            {
                if (_engine == null) return;
                _engine.Blackout = BlackoutChk.IsChecked ?? false;
                Status(_engine.Blackout
                    ? "Blackout ACTIF — noir émis sur tous les univers."
                    : "Blackout levé — reprise de la source.");
            };
            FakerChk.IsCheckedChanged += (_, _) => UpdateFaker();
            EhubChk.IsCheckedChanged += (_, _) => UpdateFaker();
            // Le bitmap change de dimensions : on le laisse se reconstruire au tick.
            WallViewChk.IsCheckedChanged += (_, _) => _bmpUniverseCount = -1;

            // Reglable a chaud : le faker est lu par le thread de routage.
            BrightnessSlider.PropertyChanged += (_, e) =>
            {
                if (e.Property != Slider.ValueProperty) return;
                _rainbow.Brightness = (float)(BrightnessSlider.Value / 100.0);
                BrightnessText.Text = $"{(int)BrightnessSlider.Value} %";
            };
            _rainbow.Brightness = (float)(BrightnessSlider.Value / 100.0);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _uiTimer.Tick += (_, _) => OnUiTick();
            _uiTimer.Start();
        }

        // ------------------------------------------------------------------ //
        // Actions du panneau
        // ------------------------------------------------------------------ //
        private void LoadConfig()
        {
            try
            {
                var path = ConfigPath.Text?.Trim();
                if (string.IsNullOrEmpty(path)) { Status("Chemin de config vide."); return; }

                var cfg = Persistence.LoadConfig(path);
                ApplyConfig(cfg, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                Status("Erreur de chargement : " + ex.Message);
            }
        }

        /// <summary>
        /// Importe un tableau d'adressage eHuB au format .xlsx (celui fourni par
        /// LAPS) et le transforme en Config via le meme pipeline que le CLI
        /// (EhubXlsx.Read -> ConfigBuilder.BuildFromEhub). Aucune dependance
        /// externe : le lecteur .xlsx vit dans le coeur Mappa.
        /// </summary>
        private async System.Threading.Tasks.Task ImportXlsxAsync()
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Importer un tableau d'adressage eHuB (.xlsx)",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Classeur Excel") { Patterns = new[] { "*.xlsx" } },
                        FilePickerFileTypes.All,
                    },
                });

                if (files.Count == 0) { Status("Import annulé."); return; }

                var path = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) { Status("Fichier .xlsx inaccessible localement."); return; }

                var rows = EhubXlsx.Read(path);
                if (rows.Count == 0) { Status("Aucune ligne d'adressage valide dans l'Excel."); return; }

                var name = Path.GetFileNameWithoutExtension(path);
                var cfg = ConfigBuilder.BuildFromEhub(rows, name);
                ConfigPath.Text = path;
                ApplyConfig(cfg, Path.GetFileName(path), $"{rows.Count} plages importées, ");
            }
            catch (Exception ex)
            {
                Status("Import .xlsx impossible : " + ex.Message);
            }
        }

        /// <summary>Valide une Config et la charge dans le moteur (point commun .json / .xlsx).</summary>
        private void ApplyConfig(Config cfg, string source, string prefix = "")
        {
            var problems = cfg.Validate();
            if (problems.Count > 0)
            {
                Status($"Config « {source} » invalide : " + string.Join(" ; ", problems));
                return;
            }

            // Recharger a chaud ne doit pas arreter le show : on repart si on tournait.
            // Le Stop() de l'ancien moteur eteint les univers de l'ANCIENNE config —
            // indispensable ici, car une config reduite ne pilote plus certains
            // controleurs, et leurs LED resteraient figees sur la derniere frame.
            bool wasRunning = _engine?.IsRunning ?? false;

            StopRouting();
            BuildWallOffsets(cfg);
            BuildTargetSummary(cfg);
            _engine = new RoutingEngine(cfg)
            {
                SendArtNet = SendChk.IsChecked ?? false,
                Blackout = BlackoutChk.IsChecked ?? false,
            };
            UpdateFaker();
            _bmp = null;
            _bmpUniverseCount = -1;

            if (wasRunning) _engine.Start();

            Status($"Config « {cfg.Name} » chargée : {prefix}{cfg.Controllers.Count} contrôleurs, "
                 + $"{_engine.UniverseCount} univers, {_engine.EntityCount} entités"
                 + (wasRunning ? " — routage relancé." : ". Clique ▶ Start."));
        }

        private void StartRouting()
        {
            if (_engine == null) { Status("Charge d'abord une config."); return; }
            _engine.SendArtNet = SendChk.IsChecked ?? false;
            _engine.Blackout = BlackoutChk.IsChecked ?? false;
            _engine.Start();
            Status(_engine.SendArtNet
                ? "Routage démarré — émission ArtNet ACTIVE (UDP)."
                : "Routage démarré (aperçu seul, aucune émission réseau).");
        }

        private void StopRouting()
        {
            if (_engine == null) return;
            _engine.Stop();
            Status("Routage arrêté — LED éteintes.");
        }

        private EhubFaker? _ehubFaker;
        private readonly RainbowFaker _rainbow = new RainbowFaker();

        private void UpdateFaker()
        {
            if (_engine == null) return;

            if (!(EhubChk.IsChecked ?? false) && _ehubFaker != null)
            {
                _ehubFaker.Dispose();
                _ehubFaker = null;
            }

            if (EhubChk.IsChecked ?? false)
            {
                if (_ehubFaker == null)
                {
                    int port = int.TryParse(EhubPortBox.Text, out var p) ? p : Ehub.DefaultUdpPort;
                    try
                    {
                        _ehubFaker = new EhubFaker(port);
                    }
                    catch (Exception ex)
                    {
                        Status("Réception eHuB impossible : " + ex.Message);
                        EhubChk.IsChecked = false;
                        return;
                    }
                }
                _engine.Faker = _ehubFaker;
            }
            else if (FakerChk.IsChecked ?? false)
            {
                // Instance conservee : recreer le faker perdrait le reglage d'intensite.
                _rainbow.Brightness = (float)(BrightnessSlider.Value / 100.0);
                _engine.Faker = _rainbow;
            }
            else
            {
                _engine.Faker = null;
            }
        }

        // ------------------------------------------------------------------ //
        // Boucle d'affichage (thread UI)
        // ------------------------------------------------------------------ //
        private void OnUiTick()
        {
            if (_engine == null) return;

            _engine.CopySnapshot(ref _snap, out int stride, out var universes);
            int uCount = universes.Length;
            if (uCount == 0) return;

            bool wall = (WallViewChk.IsChecked ?? false) && _wallOffsets.Length > 0;
            EnsureBitmap(uCount, wall);
            if (wall) RenderWall(_bmp!, _snap, _wallOffsets, _wallCols, _wallRows);
            else RenderInto(_bmp!, _snap, stride, uCount);
            DmxImage.InvalidateVisual();

            StatsText.Text =
                $"Entités       : {_engine.EntityCount}\n" +
                $"Univers       : {uCount}\n" +
                $"FPS routage   : {_engine.Fps:0.0}\n" +
                $"Paquets ArtNet: {_engine.PacketsSent}\n" +
                $"Émission vers : {(_engine.SendArtNet ? _targets : "(désactivée)")}";
        }

        /// <summary>
        /// Table (col,row) -> offset du canal rouge dans le snapshot DMX a plat.
        /// On passe par la geometrie reelle du mur (serpentin, fixations) puis par
        /// le plan de routage : c'est exactement le chemin qu'empruntent les octets
        /// jusqu'aux controleurs, donc la vue montre ce que le mur affiche.
        /// </summary>
        private void BuildWallOffsets(Config cfg)
        {
            _wallOffsets = Array.Empty<int>();
            _wallCols = _wallRows = 0;

            var geo = WallGeometry.Infer(cfg);
            var plan = new RoutingPlan(cfg);

            // Le snapshot empile les univers dans l'ordre de plan.Universes.
            var slotOf = new System.Collections.Generic.Dictionary<int, int>();
            for (int i = 0; i < plan.Universes.Count; i++) slotOf[plan.Universes[i]] = i;

            var offsets = new int[geo.Columns * geo.Rows];
            for (int row = 0; row < geo.Rows; row++)
            {
                for (int col = 0; col < geo.Columns; col++)
                {
                    int idx = row * geo.Columns + col;
                    offsets[idx] = -1;

                    int id = geo.EntityId(col, row);
                    if (id < 0) continue;
                    var addr = plan.AddressOf(id);
                    if (addr == null) continue;
                    if (!slotOf.TryGetValue(addr.Value.Universe, out int slot)) continue;

                    offsets[idx] = slot * Config.DmxChannelsPerUniverse + addr.Value.Channel;
                }
            }

            _wallOffsets = offsets;
            _wallCols = geo.Columns;
            _wallRows = geo.Rows;
        }

        /// <summary>
        /// Compte les univers par IP de destination, en suivant le meme chemin que
        /// ArtNetSender.SendPlan (univers -> controleur -> IP). Une IP absente de
        /// cette liste ne recoit AUCUN paquet de notre part : ses LED gardent leur
        /// derniere valeur, ou sont pilotees par une autre source.
        /// </summary>
        private void BuildTargetSummary(Config cfg)
        {
            var perIp = new System.Collections.Generic.SortedDictionary<string, int>();
            foreach (var u in cfg.Universes)
            {
                var ctrl = cfg.Controllers.Find(c => c.Id == u.ControllerId);
                if (ctrl == null) continue;
                perIp[ctrl.Ip] = perIp.TryGetValue(ctrl.Ip, out int n) ? n + 1 : 1;
            }

            _targets = perIp.Count == 0
                ? "aucune"
                : string.Join("\n                ", perIp.Select(kv => $"{kv.Key} ({kv.Value} univ.)"));
        }

        private void EnsureBitmap(int uCount, bool wall)
        {
            if (_bmp != null && _bmpUniverseCount == uCount && _bmpIsWall == wall) return;

            int w = wall ? _wallCols * WallCell : LedsPerRow * Cell;
            int h = wall ? _wallRows * WallCell : Math.Max(1, uCount) * Cell;
            _bmp = new WriteableBitmap(
                new PixelSize(Math.Max(1, w), Math.Max(1, h)), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _bmpUniverseCount = uCount;
            _bmpIsWall = wall;
            DmxImage.Source = _bmp;
            DmxImage.Width = w;
            DmxImage.Height = h;

            LegendText.Text = wall
                ? $"Vue mur : géométrie réelle {_wallCols}×{_wallRows}, 1 case = 1 LED visible. "
                + "Serpentin résolu ; les LED de fixation et les lyres ne sont pas affichées."
                : "Visualiseur DMX : 1 ligne = 1 univers, 1 case = 1 LED (RVB), "
                + "avant encapsulation ArtNet.";
        }

        /// <summary>Dessine le mur dans sa geometrie physique, depuis les octets DMX.</summary>
        private static unsafe void RenderWall(WriteableBitmap bmp, byte[] snap, int[] offsets, int cols, int rows)
        {
            using var fb = bmp.Lock();
            byte* basePtr = (byte*)fb.Address;
            int rowBytes = fb.RowBytes;
            int bw = fb.Size.Width;
            int bh = fb.Size.Height;

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int off = offsets[row * cols + col];
                    byte r = 0, g = 0, b = 0;
                    if (off >= 0 && off + 2 < snap.Length)
                    {
                        r = snap[off]; g = snap[off + 1]; b = snap[off + 2];
                    }

                    int x0 = col * WallCell;
                    int y0 = row * WallCell;
                    for (int yy = 0; yy < WallCell; yy++)
                    {
                        int py = y0 + yy;
                        if (py >= bh) break;
                        byte* dst = basePtr + py * rowBytes + x0 * 4;
                        for (int xx = 0; xx < WallCell; xx++)
                        {
                            if (x0 + xx >= bw) break;
                            dst[0] = b;   // Bgra8888
                            dst[1] = g;
                            dst[2] = r;
                            dst[3] = 255;
                            dst += 4;
                        }
                    }
                }
            }
        }

        private static unsafe void RenderInto(WriteableBitmap bmp, byte[] snap, int stride, int uCount)
        {
            using var fb = bmp.Lock();
            byte* basePtr = (byte*)fb.Address;
            int rowBytes = fb.RowBytes;
            int bw = fb.Size.Width;
            int bh = fb.Size.Height;

            for (int u = 0; u < uCount; u++)
            {
                int uOff = u * stride;
                for (int led = 0; led < LedsPerRow; led++)
                {
                    int ci = uOff + led * 3;
                    byte r = 0, g = 0, b = 0;
                    if (ci + 2 < snap.Length) { r = snap[ci]; g = snap[ci + 1]; b = snap[ci + 2]; }

                    int x0 = led * Cell;
                    int y0 = u * Cell;
                    for (int yy = 0; yy < Cell; yy++)
                    {
                        int py = y0 + yy;
                        if (py >= bh) break;
                        byte* row = basePtr + py * rowBytes + x0 * 4;
                        for (int xx = 0; xx < Cell; xx++)
                        {
                            if (x0 + xx >= bw) break;
                            row[0] = b;   // Bgra8888
                            row[1] = g;
                            row[2] = r;
                            row[3] = 255;
                            row += 4;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ //
        // Divers
        // ------------------------------------------------------------------ //
        private void Status(string message) => StatusText.Text = message;

        /// <summary>Cherche configs/ecran.json en remontant depuis le dossier de l'exe.</summary>
        private static string? FindDefaultConfig()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "configs", "ecran.json");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        protected override void OnClosed(EventArgs e)
        {
            _uiTimer.Stop();
            _engine?.Dispose();
            _ehubFaker?.Dispose();
            base.OnClosed(e);
        }
    }
}
