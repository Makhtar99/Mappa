using System;
using System.IO;
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

        private RoutingEngine? _engine;
        private WriteableBitmap? _bmp;
        private byte[] _snap = Array.Empty<byte>();
        private int _bmpUniverseCount = -1;
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
            FakerChk.IsCheckedChanged += (_, _) => UpdateFaker();
            EhubChk.IsCheckedChanged += (_, _) => UpdateFaker();

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

            StopRouting();
            _engine = new RoutingEngine(cfg) { SendArtNet = SendChk.IsChecked ?? false };
            UpdateFaker();
            _bmp = null;
            _bmpUniverseCount = -1;

            Status($"Config « {cfg.Name} » chargée : {prefix}{cfg.Controllers.Count} contrôleurs, "
                 + $"{_engine.UniverseCount} univers, {_engine.EntityCount} entités.");
        }

        private void StartRouting()
        {
            if (_engine == null) { Status("Charge d'abord une config."); return; }
            _engine.SendArtNet = SendChk.IsChecked ?? false;
            _engine.Start();
            Status(_engine.SendArtNet
                ? "Routage démarré — émission ArtNet ACTIVE (UDP)."
                : "Routage démarré (aperçu seul, aucune émission réseau).");
        }

        private void StopRouting()
        {
            _engine?.Stop();
            if (_engine != null) Status("Routage arrêté.");
        }

        private EhubFaker? _ehubFaker;

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
                _engine.Faker = new RainbowFaker();
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

            EnsureBitmap(uCount);
            RenderInto(_bmp!, _snap, stride, uCount);
            DmxImage.InvalidateVisual();

            StatsText.Text =
                $"Entités       : {_engine.EntityCount}\n" +
                $"Univers       : {uCount}\n" +
                $"FPS routage   : {_engine.Fps:0.0}\n" +
                $"Paquets ArtNet: {_engine.PacketsSent}";
        }

        private void EnsureBitmap(int uCount)
        {
            if (_bmp != null && _bmpUniverseCount == uCount) return;

            int w = LedsPerRow * Cell;
            int h = Math.Max(1, uCount) * Cell;
            _bmp = new WriteableBitmap(
                new PixelSize(w, h), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            _bmpUniverseCount = uCount;
            DmxImage.Source = _bmp;
            DmxImage.Width = w;
            DmxImage.Height = h;
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
