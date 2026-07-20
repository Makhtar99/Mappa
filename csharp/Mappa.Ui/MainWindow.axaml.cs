using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // Couleur du quadrillage des moniteurs (contour de chaque case).
        private static readonly Avalonia.Media.IBrush GridBrush =
            new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x55, 0x55, 0x60));
        private static readonly Thickness GridLine = new Thickness(1);

        private RoutingEngine? _engine;
        private Config? _config;            // config chargée (pour résoudre univers → IP au nœud ④)
        private WriteableBitmap? _bmp;
        private byte[] _snap = Array.Empty<byte>();
        private int _bmpUniverseCount = -1;
        private readonly DispatcherTimer _uiTimer;
        private readonly ObservableCollection<EhubReceiver.PacketSnapshot> _packetItems = new ObservableCollection<EhubReceiver.PacketSnapshot>();

        // Débogage : récepteur eHuB du nœud ①. Les moniteurs affichent ce qu'il
        // reçoit (rien tant qu'aucun eHuB n'arrive).
        private EhubReceiver? _debugRx;
        private long _lastRxPainted = -1;
        private bool _debugDirty = true;
        private string? _selectedPacketKey;
        private string? _lastUniverseFilterText;
        private byte[]? _lastDmx;           // dernière trame DMX affichée (pour « Envoyer la trame »)

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
            ListenBtn.Click += (_, _) => ToggleListen();
            SimulateUnityBtn.Click += (_, _) => SimulateUnity();
            PacketListBox.ItemsSource = _packetItems;
            PacketListBox.SelectionChanged += (_, _) =>
            {
                _selectedPacketKey = (PacketListBox.SelectedItem as EhubReceiver.PacketSnapshot)?.Key;
                _debugDirty = true;
            };
            FormatBox.SelectionChanged += (_, _) => _debugDirty = true;
            FilterStartBox.TextChanged += (_, _) => _debugDirty = true;
            FilterEndBox.TextChanged += (_, _) => _debugDirty = true;
            UniverseFilterBox.TextChanged += (_, _) => _debugDirty = true;
            TestUniverseBox.TextChanged += (_, _) => RefreshNode4Ip();
            SendFrameBtn.Click += (_, _) => SendArtNet(_lastDmx ?? FullFrame(0));
            TestWhiteBtn.Click += (_, _) => SendArtNet(FullFrame(255));
            TestBlackBtn.Click += (_, _) => SendArtNet(FullFrame(0));

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _uiTimer.Tick += (_, _) => OnUiTick();
            _uiTimer.Start();

            RefreshNode4Ip(); // affiche « charge une config » au démarrage
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
            _config = cfg;                       // dispo pour le nœud ④ (résolution univers → IP)
            _engine = new RoutingEngine(cfg) { SendArtNet = SendChk.IsChecked ?? false };
            UpdateFaker();
            RefreshNode4Ip();                    // met à jour « univers → contrôleur » du débogage
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

        /// <summary>
        /// Nœud ① : démarre/arrête l'écoute eHuB sur le port saisi. Tant qu'on
        /// n'écoute pas (ou que rien n'arrive), les moniteurs restent vides.
        /// </summary>
        private void ToggleListen()
        {
            if (_debugRx == null)
            {
                int port = int.TryParse(InPortBox.Text, out var p) ? p : Ehub.DefaultUdpPort;
                try { _debugRx = new EhubReceiver(port); }
                catch (Exception ex) { Status("Écoute eHuB impossible : " + ex.Message); return; }
                ListenBtn.Content = "■ Stop";
                RxInfo.Text = "à l'écoute…";
                _packetItems.Clear();
                PacketCountText.Text = "0";
            }
            else
            {
                _debugRx.Dispose();
                _debugRx = null;
                ListenBtn.Content = "▶ Écouter";
                RxInfo.Text = "arrêté";
                _packetItems.Clear();
                PacketCountText.Text = "0";
            }
            _debugDirty = true;
        }

        /// <summary>
        /// Simulation minimale : envoie une frame eHuB locale avec la plage et la couleur saisies.
        /// Elle sert juste à générer du trafic de test, sans modifier le monitor de réception.
        /// </summary>
        private void SimulateUnity()
        {
            if (!int.TryParse(SimStartBox.Text, out int start) ||
                !int.TryParse(SimEndBox.Text, out int end) || end < start)
            {
                Status("Plage invalide : Start ID doit être ≤ End ID.");
                return;
            }

            if (!byte.TryParse(SimUniverseBox.Text, out byte ehubUniverse))
            {
                Status("Univers eHuB invalide : entre 0 et 255.");
                return;
            }

            var parts = (SimColorBox.Text ?? "").Split(',');
            if (parts.Length != 3 ||
                !byte.TryParse(parts[0].Trim(), out byte r) ||
                !byte.TryParse(parts[1].Trim(), out byte g) ||
                !byte.TryParse(parts[2].Trim(), out byte b))
            {
                Status("Couleur invalide : format R,V,B (ex : 255,255,0).");
                return;
            }

            int port = int.TryParse(SimPortBox.Text, out var p) ? p : Ehub.DefaultUdpPort;
            var ids = new List<int>();
            for (int id = start; id <= end; id++) ids.Add(id);
            var st = new State(ids);
            foreach (int id in ids) st.Set(id, r, g, b);

            try
            {
                byte[] packet = Ehub.EncodeUpdate(ehubUniverse, ids, st);
                using var udp = new System.Net.Sockets.UdpClient();
                udp.Send(packet, packet.Length,
                    new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
                Status("Frame eHuB simulée envoyée.");
            }
            catch (Exception ex)
            {
                Status("Envoi eHuB simulé impossible : " + ex.Message);
            }
        }

        /// <summary>
        /// Appelé à chaque tick : rafraîchit les moniteurs ②③④ avec ce qui a été
        /// REÇU en eHuB. Rien reçu (ou pas à l'écoute) => moniteurs vides.
        /// </summary>
        private void UpdateDebugMonitors()
        {
            long rx = _debugRx?.PacketsReceived ?? 0;
            if (!_debugDirty && rx == _lastRxPainted) return;
            _debugDirty = false;
            _lastRxPainted = rx;

            // Rien reçu -> on n'affiche rien.
            if (_debugRx == null || rx == 0)
            {
                _packetItems.Clear();
                _selectedPacketKey = null;
                PacketCountText.Text = "0";
                LedMonitor.Children.Clear();
                DmxMonitor.Children.Clear();
                ArtNetMonitor.Children.Clear();
                return;
            }

            var packets = _debugRx.SnapshotPackets();
            string universeFilterText = (UniverseFilterBox.Text ?? string.Empty).Trim();
            byte? universeFilter = TryParseByte(universeFilterText);
            var visiblePackets = new List<EhubReceiver.PacketSnapshot>(packets.Count);
            for (int i = 0; i < packets.Count; i++)
            {
                var packet = packets[i];
                if (universeFilter.HasValue && packet.Universe != universeFilter.Value) continue;
                visiblePackets.Add(packet);
            }

            SyncPacketList(visiblePackets, universeFilterText);
            PacketCountText.Text = visiblePackets.Count.ToString();

            var selectedPacket = GetSelectedPacket(visiblePackets) ?? GetLastPacket(visiblePackets);
            if (selectedPacket == null)
            {
                LedMonitor.Children.Clear();
                DmxMonitor.Children.Clear();
                ArtNetMonitor.Children.Clear();
                RxInfo.Text = $"reçu : {rx} paquet(s)";
                return;
            }

            if (!ReferenceEquals(PacketListBox.SelectedItem, selectedPacket))
                PacketListBox.SelectedItem = selectedPacket;

            if (!int.TryParse(FilterStartBox.Text, out int start) ||
                !int.TryParse(FilterEndBox.Text, out int end) || end < start)
                return;

            PaintPipeline(selectedPacket.ToState(), start, end);
            RxInfo.Text = $"reçu : {rx} paquet(s) - {selectedPacket.RemoteEndPoint.Address} u{selectedPacket.Universe}";
        }

        private void SyncPacketList(IReadOnlyList<EhubReceiver.PacketSnapshot> packets, string universeFilterText)
        {
            bool filterChanged = !string.Equals(_lastUniverseFilterText, universeFilterText, StringComparison.Ordinal);
            _lastUniverseFilterText = universeFilterText;

            if (filterChanged || packets.Count < _packetItems.Count)
            {
                string? selectedKey = _selectedPacketKey;
                _packetItems.Clear();
                for (int i = 0; i < packets.Count; i++)
                {
                    _packetItems.Add(packets[i]);
                }

                if (selectedKey == null) return;

                for (int i = 0; i < _packetItems.Count; i++)
                {
                    if (_packetItems[i].Key == selectedKey)
                    {
                        PacketListBox.SelectedIndex = i;
                        return;
                    }
                }

                _selectedPacketKey = null;
                return;
            }

            for (int i = _packetItems.Count; i < packets.Count; i++)
            {
                _packetItems.Add(packets[i]);
            }
        }

        private EhubReceiver.PacketSnapshot? GetSelectedPacket(IReadOnlyList<EhubReceiver.PacketSnapshot> packets)
        {
            if (string.IsNullOrEmpty(_selectedPacketKey)) return null;

            for (int i = 0; i < packets.Count; i++)
            {
                if (packets[i].Key == _selectedPacketKey)
                    return packets[i];
            }

            return null;
        }

        private static EhubReceiver.PacketSnapshot? GetLastPacket(IReadOnlyList<EhubReceiver.PacketSnapshot> packets)
            => packets.Count > 0 ? packets[packets.Count - 1] : null;

        private static byte? TryParseByte(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return byte.TryParse(text.Trim(), out byte value) ? value : null;
        }

        /// <summary>
        /// Dessine les moniteurs ② (1 case = 1 LED), ③ (canaux selon le format)
        /// et ④ (trame DMX) à partir des couleurs REÇUES dans <paramref name="state"/>.
        /// </summary>
        private void PaintPipeline(State state, int start, int end)
        {
            LedMonitor.Children.Clear();
            DmxMonitor.Children.Clear();

            var dmx = new byte[Config.DmxChannelsPerUniverse];
            int used = 0;
            for (int id = start; id <= end; id++)
            {
                var c = state.Get(id); // couleur reçue (noir si cette entité n'a rien reçu)
                LedMonitor.Children.Add(LedCell(new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.FromRgb(c.R, c.G, c.B))));

                byte[] chans = ChannelsFor(c.R, c.G, c.B);
                for (int k = 0; k < chans.Length; k++)
                {
                    DmxMonitor.Children.Add(DmxSquare(chans[k], k == chans.Length - 1 ? 5 : 0));
                    if (used < dmx.Length) dmx[used++] = chans[k];
                }
            }
            PaintArtNetMonitor(dmx, used);
            _lastDmx = dmx;   // mémorisée pour « Envoyer la trame »
        }

        /// <summary>Une trame DMX de 512 canaux, tous à <paramref name="level"/> (255 = blanc, 0 = noir).</summary>
        private static byte[] FullFrame(byte level)
        {
            var dmx = new byte[Config.DmxChannelsPerUniverse];
            if (level != 0) Array.Fill(dmx, level);
            return dmx;
        }

        /// <summary>
        /// Nœud ④ : envoie VRAIMENT une trame DMX en ArtNet vers l'IP + univers
        /// saisis. C'est la fonction d'émission du débogage (validée par le prof).
        /// </summary>
        private void SendArtNet(byte[] dmx)
        {
            string ip = TestIpBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(ip))
            {
                Status("Renseigne une IP dans le nœud ④ avant d'envoyer.");
                return;
            }
            int universe = int.TryParse(TestUniverseBox.Text, out var u) ? u : 0;
            try
            {
                using var sender = new ArtNetSender();
                // 3 trames d'affilée : certains contrôleurs ignorent un paquet isolé.
                for (int i = 0; i < 3; i++) sender.Send(ip, universe, dmx);
                Status($"ArtNet envoyé à {ip}, univers {universe}.");
            }
            catch (Exception ex)
            {
                Status("Envoi ArtNet impossible : " + ex.Message);
            }
        }

        /// <summary>
        /// Canaux DMX d'une LED selon son type (nœud ③) : certaines LED n'ont
        /// qu'un seul canal (mono), d'autres 3 (RGB), parfois dans l'ordre GRB.
        /// </summary>
        private byte[] ChannelsFor(byte r, byte g, byte b)
        {
            string fmt = (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "RGB";
            return fmt switch
            {
                "GRB" => new byte[] { g, r, b },
                "Mono (R)" => new byte[] { r },
                _ => new byte[] { r, g, b },   // RGB
            };
        }

        /// <summary>
        /// Nœud ④ : affiche vers quelle IP (contrôleur) partirait l'univers saisi,
        /// résolu DEPUIS LA CONFIG (univers → contrôleur → IP). Lecture seule :
        /// c'est un vérificateur. Il signale les univers « non routés » (aucun
        /// contrôleur ne les prend), un bug de config classique à débusquer.
        /// </summary>
        private void RefreshNode4Ip()
        {
            if (!int.TryParse(TestUniverseBox.Text, out int uni) || _config == null)
            {
                Node4Hint.Text = _config == null ? "(charge une config pour l'IP auto)" : "";
                return;
            }
            var res = ResolveController(uni);
            if (res.HasValue)
            {
                TestIpBox.Text = res.Value.ip;                       // pré-remplit (modifiable)
                Node4Hint.Text = $"→ {res.Value.id} (depuis la config)";
            }
            else
            {
                Node4Hint.Text = "⚠ univers non routé dans la config";
            }
        }

        /// <summary>Cherche dans la config le contrôleur (IP + id) d'un univers donné.</summary>
        private (string ip, string id)? ResolveController(int universe)
        {
            if (_config == null) return null;
            foreach (var u in _config.Universes)
            {
                if (u.Index == universe || u.ArtNetUniverse == universe)
                {
                    foreach (var c in _config.Controllers)
                        if (c.Id == u.ControllerId) return (c.Ip, c.Id);
                }
            }
            return null;
        }

        /// <summary>
        /// Moniteur du nœud ④ : dessine les <paramref name="count"/> premiers
        /// canaux de la trame DMX qui part (ou partirait) en ArtNet.
        /// 1 carré = 1 canal, niveau de gris = intensité (0 éteint → 255 blanc).
        /// </summary>
        private void PaintArtNetMonitor(byte[] dmx, int count)
        {
            ArtNetMonitor.Children.Clear();
            count = Math.Min(count, dmx.Length);
            for (int i = 0; i < count; i++)
            {
                byte v = dmx[i];
                ArtNetMonitor.Children.Add(new Border
                {
                    Width = 12,
                    Height = 12,
                    Margin = new Thickness(1),
                    Background = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.FromRgb(v, v, v)),
                    BorderBrush = GridBrush,
                    BorderThickness = GridLine,
                });
            }
        }

        /// <summary>Une case LED (16x16) de la couleur donnée, avec contour de grille.</summary>
        private static Border LedCell(Avalonia.Media.IBrush brush) => new Border
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(1),
            Background = brush,
            BorderBrush = GridBrush,
            BorderThickness = GridLine,
        };

        /// <summary>Un carré DMX : luminosité (gris) = valeur du canal (0 éteint, 255 blanc).</summary>
        private static Border DmxSquare(byte value, double rightMargin) => new Border
        {
            Width = 8,
            Height = 14,
            Margin = new Thickness(1, 1, rightMargin, 1),
            Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.FromRgb(value, value, value)),
            BorderBrush = GridBrush,
            BorderThickness = GridLine,
        };

        // ------------------------------------------------------------------ //
        // Boucle d'affichage (thread UI)
        // ------------------------------------------------------------------ //
        private void OnUiTick()
        {
            UpdateDebugMonitors(); // onglet Débogage : marche même sans config chargée

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
            _debugRx?.Dispose();
            base.OnClosed(e);
        }
    }
}
