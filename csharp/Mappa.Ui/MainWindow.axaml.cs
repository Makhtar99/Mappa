using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private const int MaxMonitoredLeds = 170; // plafond d'affichage du moniteur ② (1 univers RVB)
        private const int WallCell = 4;    // taille d'une LED dans la vue mur (px)

        // Couleur du quadrillage des moniteurs (contour de chaque case).
        private static readonly Avalonia.Media.IBrush GridBrush =
            new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x55, 0x55, 0x60));
        private static readonly Thickness GridLine = new Thickness(1);

        private RoutingEngine? _engine;
        private Config? _config;            // config chargée (pour résoudre univers → IP au nœud ④)
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

        // Débogage : récepteur eHuB du nœud ①. Les moniteurs affichent ce qu'il
        // reçoit (rien tant qu'aucun eHuB n'arrive).
        private EhubReceiver? _debugRx;
        private long _lastRxPainted = -1;
        private bool _debugDirty = true;
        private byte[]? _lastDmx;           // dernière trame DMX affichée (pour « Envoyer la trame »)
        private bool _fakerAnimating;       // faker : animation en cours
        private int _fakerFrame;            // indice de frame d'animation
        private int _fakerDelayMs = 120;    // cadence du faker animé / rafale
        private long _lastFakerTickMs;      // horodatage du dernier envoi faker
        private bool _fakerBurstRunning;    // évite les rafales concurrentes
        private System.Net.Sockets.UdpClient? _simUdp; // socket du faux Unity (réutilisé)
        private ArtNetSender? _artSender;   // émetteur ArtNet du nœud ④ (réutilisé)
        private bool _artStreaming;         // ④ : émission en continu (hardware)
        private bool _artStreamLoopRunning; // boucle d'émission continue en cours
        private byte[] _artStreamFrame = FullFrame(255);

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

            ListenBtn.Click += (_, _) => ToggleListen();
            SimulateUnityBtn.Click += (_, _) => SimulateUnity();
            BurstBtn.Click += async (_, _) => await SendFakerBurstAsync();
            FakerDelayBox.TextChanged += (_, _) => UpdateFakerDelay();
            FormatBox.SelectionChanged += (_, _) => _debugDirty = true;
            FilterStartBox.TextChanged += (_, _) => _debugDirty = true;
            FilterEndBox.TextChanged += (_, _) => _debugDirty = true;
            UniverseFilterBox.TextChanged += (_, _) => _debugDirty = true;
            TestUniverseBox.TextChanged += (_, _) => RefreshNode4Ip();
            SendFrameBtn.Click += (_, _) => SendArtNet(_lastDmx ?? FullFrame(0));
            TestWhiteBtn.Click += (_, _) => SendArtNet(FullFrame(255));
            TestBlackBtn.Click += (_, _) => SendArtNet(FullFrame(0));
            AnimateBtn.Click += (_, _) => ToggleAnimate();
            StreamBtn.Click += (_, _) => ToggleStream();
            ModeObserveRadio.IsCheckedChanged += (_, _) => ApplyDebugMode();
            ModeTestRadio.IsCheckedChanged += (_, _) => ApplyDebugMode();

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _uiTimer.Tick += (_, _) => OnUiTick();
            _uiTimer.Start();

            RefreshNode4Ip();  // affiche « charge une config » au démarrage
            ApplyDebugMode();  // démarre en Observation : émission désactivée
        }

        private void UpdateFakerDelay()
        {
            if (int.TryParse(FakerDelayBox.Text, out int delay) && delay >= 0)
                _fakerDelayMs = delay;
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
            _config = cfg;                       // dispo pour le nœud ④ (résolution univers → IP)
            BuildWallOffsets(cfg);
            BuildTargetSummary(cfg);
            _engine = new RoutingEngine(cfg)
            {
                SendArtNet = SendChk.IsChecked ?? false,
                Blackout = BlackoutChk.IsChecked ?? false,
            };
            UpdateFaker();
            RefreshNode4Ip();                    // met à jour « univers → contrôleur » du débogage
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
            }
            else
            {
                _debugRx.Dispose();
                _debugRx = null;
                ListenBtn.Content = "▶ Écouter";
                RxInfo.Text = "arrêté";
            }
            _debugDirty = true;
        }

        /// <summary>
        /// Simulation minimale : envoie une frame eHuB locale avec la plage et la couleur saisies.
        /// Elle sert juste à générer du trafic de test, sans modifier le monitor de réception.
        /// </summary>
        private void SimulateUnity() => SendFakerFrame(0);   // envoi ponctuel (image figée)

        /// <summary>Envoie une petite rafale de frames eHuB pour tester les motifs animés.</summary>
        private async Task SendFakerBurstAsync()
        {
            if (_fakerBurstRunning) return;
            if (!int.TryParse(FakerBurstBox.Text, out int count) || count <= 0)
            {
                Status("Nombre de frames invalide pour la rafale.");
                return;
            }

            _fakerBurstRunning = true;
            BurstBtn.IsEnabled = false;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (!SendFakerFrame(i, announce: false)) return;
                    await Task.Delay(_fakerDelayMs);
                }

                Status($"Rafale de {count} frame(s) envoyée.");
            }
            finally
            {
                BurstBtn.IsEnabled = true;
                _fakerBurstRunning = false;
            }
        }

        /// <summary>
        /// Envoie UNE frame eHuB du faker : plage + couleur + motif choisis, pour
        /// l'indice d'animation <paramref name="frame"/> (chenillard / arc-en-ciel).
        /// </summary>
        private bool SendFakerFrame(int frame, bool announce = true)
        {
            if (!int.TryParse(SimStartBox.Text, out int start) ||
                !int.TryParse(SimEndBox.Text, out int end) || end < start)
            {
                Status("Plage invalide : Start ID doit être ≤ End ID.");
                _fakerAnimating = false;
                return false;
            }
            if (!byte.TryParse(SimUniverseBox.Text, out byte ehubUniverse))
            {
                Status("Univers eHuB invalide : entre 0 et 255.");
                _fakerAnimating = false;
                return false;
            }
            var parts = (SimColorBox.Text ?? "").Split(',');
            if (parts.Length != 3 ||
                !byte.TryParse(parts[0].Trim(), out byte r) ||
                !byte.TryParse(parts[1].Trim(), out byte g) ||
                !byte.TryParse(parts[2].Trim(), out byte b))
            {
                Status("Couleur invalide : format R,V,B (ex : 255,255,0).");
                _fakerAnimating = false;
                return false;
            }

            int port = int.TryParse(SimPortBox.Text, out var p) ? p : Ehub.DefaultUdpPort;
            var ids = new List<int>();
            for (int id = start; id <= end; id++) ids.Add(id);
            var st = new State(ids);
            string motif = (FakerMotifBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Couleur unie";
            FillMotif(st, ids, motif, r, g, b, frame);

            try
            {
                byte[] packet = Ehub.EncodeUpdate(ehubUniverse, ids, st);
                // Socket réutilisé : en animation on envoie ~30 frames/s, ouvrir
                // et fermer un UdpClient à chaque fois épuiserait les ports éphémères.
                _simUdp ??= new System.Net.Sockets.UdpClient();
                _simUdp.Send(packet, packet.Length,
                    new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
                if (announce && !_fakerAnimating) Status($"Frame eHuB « {motif} » envoyée.");
                return true;
            }
            catch (Exception ex)
            {
                Status("Envoi eHuB simulé impossible : " + ex.Message);
                _fakerAnimating = false;
                return false;
            }
        }

        /// <summary>Remplit le State selon le motif (pour l'animation <paramref name="frame"/>).</summary>
        private static void FillMotif(State st, IReadOnlyList<int> ids, string motif,
                                      byte r, byte g, byte b, int frame)
        {
            int n = ids.Count;
            if (n == 0) return;
            st.Clear();
            switch (motif)
            {
                case "Chenillard":
                    st.Set(ids[frame % n], r, g, b);            // 1 LED allumée, qui se déplace
                    break;
                case "Arc-en-ciel":
                    for (int i = 0; i < n; i++)
                    {
                        HsvToRgb(((double)i / n + frame * 0.02) % 1.0,
                                 out byte rr, out byte gg, out byte bb);
                        st.Set(ids[i], rr, gg, bb);
                    }
                    break;
                case "Tout blanc":
                    foreach (int id in ids) st.Set(id, 255, 255, 255);
                    break;
                case "Clignotement":
                    if ((frame / 10) % 2 == 0)
                        foreach (int id in ids) st.Set(id, r, g, b);
                    break;
                default:                                        // Couleur unie
                    foreach (int id in ids) st.Set(id, r, g, b);
                    break;
            }
        }

        /// <summary>Bascule l'animation du faker (une frame envoyée à chaque tick).</summary>
        private void ToggleAnimate()
        {
            _fakerAnimating = !_fakerAnimating;
            _fakerFrame = 0;
            _lastFakerTickMs = 0;
            AnimateBtn.Content = _fakerAnimating ? "■ Stop" : "▶ Animer";
            Status(_fakerAnimating ? "Animation du faker en cours…" : "Animation arrêtée.");
        }

        /// <summary>Teinte HSV (S=V=1) → RVB, pour le motif arc-en-ciel.</summary>
        private static void HsvToRgb(double h, out byte r, out byte g, out byte b)
        {
            double hh = (h % 1.0) * 6.0;
            int i = (int)Math.Floor(hh);
            double f = hh - i, q = 1 - f, t = f;
            double rd, gd, bd;
            switch (((i % 6) + 6) % 6)
            {
                case 0: rd = 1; gd = t; bd = 0; break;
                case 1: rd = q; gd = 1; bd = 0; break;
                case 2: rd = 0; gd = 1; bd = t; break;
                case 3: rd = 0; gd = q; bd = 1; break;
                case 4: rd = t; gd = 0; bd = 1; break;
                default: rd = 1; gd = 0; bd = q; break;
            }
            r = (byte)(rd * 255); g = (byte)(gd * 255); b = (byte)(bd * 255);
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
                LedMonitor.Children.Clear();
                DmxMonitor.Children.Clear();
                ArtNetMonitor.Children.Clear();
                return;
            }

            // On inspecte le DERNIER paquet reçu (filtré par univers si demandé) :
            // c'est l'image courante du système.
            var received = _debugRx.SnapshotPackets();
            byte? universeFilter = TryParseByte(UniverseFilterBox.Text);
            var selectedPacket = LastPacket(received, universeFilter);
            if (selectedPacket == null)
            {
                LedMonitor.Children.Clear();
                DmxMonitor.Children.Clear();
                ArtNetMonitor.Children.Clear();
                // On annonce les univers RÉELLEMENT reçus : sans ça, un filtre qui
                // ne matche rien donne un écran vide sans dire quoi saisir.
                RxInfo.Text = $"reçu : {rx} paquet(s) — rien sur l'univers "
                            + $"{universeFilter}. Univers reçus : {UniversesSeen(received)}";
                return;
            }

            if (!int.TryParse(FilterStartBox.Text, out int start) ||
                !int.TryParse(FilterEndBox.Text, out int end) || end < start)
            {
                RxInfo.Text = "plage ② invalide (Start ≤ End)";
                return;
            }

            PaintPipeline(selectedPacket.ToState(), start, end);
            ShowUniversesFedBy(selectedPacket);
            RxInfo.Text = $"reçu : {rx} paquet(s) · {selectedPacket.RemoteEndPoint.Address} · "
                        + $"univers affiché {selectedPacket.Universe} (reçus : {UniversesSeen(received)})";
        }

        /// <summary>Liste triée des univers eHuB présents dans les paquets reçus.</summary>
        private static string UniversesSeen(IReadOnlyList<EhubReceiver.PacketSnapshot> packets)
        {
            var seen = new SortedSet<byte>();
            for (int i = 0; i < packets.Count; i++) seen.Add(packets[i].Universe);
            return seen.Count == 0 ? "aucun" : string.Join(", ", seen);
        }

        /// <summary>
        /// Dernier paquet reçu, éventuellement restreint à un univers eHuB.
        /// Retourne null si aucun paquet ne correspond au filtre.
        /// </summary>
        private static EhubReceiver.PacketSnapshot? LastPacket(
            IReadOnlyList<EhubReceiver.PacketSnapshot> packets, byte? universeFilter)
        {
            for (int i = packets.Count - 1; i >= 0; i--)
            {
                if (!universeFilter.HasValue || packets[i].Universe == universeFilter.Value)
                    return packets[i];
            }
            return null;
        }

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

            // Garde-fou : un moniteur, c'est un échantillon, pas un mur. Une plage
            // trop large créerait des dizaines de milliers de contrôles et figerait
            // la fenêtre — et de toute façon un univers DMX ne porte pas plus de
            // 170 LED RVB.
            if (end - start + 1 > MaxMonitoredLeds) end = start + MaxMonitoredLeds - 1;

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
            RefreshArtHeader();
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
            _artStreamFrame = dmx;   // trame que le mode « Continu » répétera
            if (SendOnce(dmx, out string ip, out int universe))
            {
                Status(_artStreaming
                    ? $"Émission continue vers {ip}, univers {universe}."
                    : $"ArtNet envoyé à {ip}, univers {universe}.");
            }
        }

        /// <summary>
        /// Envoie UNE trame ArtNet vers l'IP/univers du nœud ④ (émetteur réutilisé).
        /// Le numéro saisi peut être un index global de config ; on émet toujours
        /// l'univers ArtNet LOCAL du contrôleur (0..31), comme le fait le routage
        /// réel (ArtNetSender.SendPlan). Sans cette traduction, un index global
        /// partirait tel quel et le contrôleur ignorerait la trame en silence.
        /// </summary>
        private bool SendOnce(byte[] dmx, out string ip, out int universe)
        {
            ip = TestIpBox.Text?.Trim() ?? "";
            universe = int.TryParse(TestUniverseBox.Text, out var u) ? u : 0;
            var routed = ResolveController(universe);
            if (routed.HasValue) universe = routed.Value.local;
            if (string.IsNullOrEmpty(ip))
            {
                Status("Renseigne une IP dans le nœud ④ avant d'envoyer.");
                _artStreaming = false;
                return false;
            }
            try
            {
                int port = int.TryParse(TestPortBox.Text, out var p) ? p : 6454;
                _artSender ??= new ArtNetSender();
                _artSender.Send(ip, universe, dmx, port);
                return true;
            }
            catch (Exception ex)
            {
                Status("Envoi ArtNet impossible : " + ex.Message);
                _artStreaming = false;
                return false;
            }
        }

        /// <summary>
        /// Applique le mode de la page débogage.
        ///  - Observation (défaut) : écoute seule, TOUTE émission est désactivée
        ///    → on ne peut pas perturber le système en production.
        ///  - Test : les boutons d'émission sont actifs, avec un bandeau d'alerte.
        /// Repasser en Observation coupe immédiatement les émissions en cours.
        /// </summary>
        private void ApplyDebugMode()
        {
            bool test = ModeTestRadio.IsChecked ?? false;

            SimulateUnityBtn.IsEnabled = test;
            AnimateBtn.IsEnabled = test;
            BurstBtn.IsEnabled = test;
            SendFrameBtn.IsEnabled = test;
            TestWhiteBtn.IsEnabled = test;
            TestBlackBtn.IsEnabled = test;
            StreamBtn.IsEnabled = test;
            EmitWarning.IsVisible = test;

            // En Observation, la carte Simulation disparaît : le pipeline commence
            // alors à ① et n'affiche QUE du signal réellement reçu.
            SimulationCard.IsVisible = test;
            SimulationArrow.IsVisible = test;

            // L'IP du nœud ④ change de nature selon le mode :
            //  - Observation : information LUE dans la config (« cet univers part là »),
            //    donc non modifiable — on n'oriente pas un flux qu'on se contente de regarder.
            //  - Test : commande d'émission, donc modifiable (cibler un contrôleur précis).
            TestIpBox.IsReadOnly = !test;
            TestIpBox.Opacity = test ? 1.0 : 0.6;

            // L'univers reste éditable dans les deux modes : il paramètre l'APERÇU
            // du paquet (en-tête + IP résolue). En Observation rien ne part, donc
            // le modifier est sans effet sur le système observé.
            Node4Role.Text = test
                ? "Univers + IP + port = destination réelle, à saisir toi-même. Un envoi allume la sortie correspondante."
                : "Aperçu seul : change l'univers pour voir le paquet qui partirait. Rien n'est émis.";

            if (!test)
            {
                // Sécurité : on coupe tout ce qui émet en repassant en observation.
                if (_fakerAnimating) { _fakerAnimating = false; AnimateBtn.Content = "▶ Animer"; }
                if (_artStreaming) { _artStreaming = false; StreamBtn.Content = "▶ Continu"; }
                Status("Mode Observation : écoute seule, aucune émission.");
            }
            else
            {
                Status("Mode Test : l'outil peut ÉMETTRE sur le réseau.");
            }
        }

        /// <summary>
        /// Bascule l'émission ArtNet EN CONTINU. Nécessaire pour le vrai matériel :
        /// les contrôleurs oublient un paquet isolé, ils ont besoin d'un flux.
        /// </summary>
        private void ToggleStream()
        {
            _artStreaming = !_artStreaming;
            StreamBtn.Content = _artStreaming ? "■ Stop" : "▶ Continu";
            if (_artStreaming)
            {
                _ = RunArtStreamLoopAsync();
            }
            Status(_artStreaming
                ? "Émission ArtNet EN CONTINU — les LEDs restent allumées."
                : "Émission continue arrêtée.");
        }

        /// <summary>
        /// Boucle d'émission ArtNet dédiée au mode continu. Elle n'utilise pas le
        /// timer UI, donc le flux reste actif même si l'interface ralentit.
        /// </summary>
        private async Task RunArtStreamLoopAsync()
        {
            if (_artStreamLoopRunning) return;
            _artStreamLoopRunning = true;
            try
            {
                while (_artStreaming)
                {
                    SendOnce(GetLiveStreamFrame(), out _, out _);
                    await Task.Delay(33);
                }
            }
            finally
            {
                _artStreamLoopRunning = false;
            }
        }

        /// <summary>
        /// Retourne la trame la plus récente à émettre en continu : la dernière
        /// trame DMX affichée si elle existe, sinon la trame mémorisée par le bouton.
        /// </summary>
        private byte[] GetLiveStreamFrame() => _lastDmx ?? _artStreamFrame;

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
        /// résolu DEPUIS LA CONFIG (univers → contrôleur → IP), ainsi que le
        /// numéro d'univers LOCAL réellement écrit dans l'en-tête ArtNet.
        /// Il signale les univers « non routés » (aucun contrôleur ne les prend),
        /// un bug de config classique à débusquer.
        /// </summary>
        private void RefreshNode4Ip()
        {
            if (!int.TryParse(TestUniverseBox.Text, out int uni) || _config == null)
            {
                Node4Hint.Text = _config == null ? "(charge une config pour l'info de routage)" : "";
                RefreshArtHeader();
                return;
            }
            var res = ResolveController(uni);
            if (res.HasValue)
            {
                // Information seule : on n'écrit RIEN dans les champs IP et Port.
                // C'est l'utilisateur qui choisit sa cible ; la config se contente
                // de lui dire ce qu'elle sait de cet univers.
                string head = res.Value.local == uni
                    ? $"→ {res.Value.id} · {res.Value.ip} · univers ArtNet {res.Value.local}"
                    : $"→ {res.Value.id} · {res.Value.ip} · index global {uni} → univers ArtNet {res.Value.local}";
                var range = EntityRangeFor(uni);
                Node4Hint.Text = range.HasValue
                    ? $"{head}\nporte les entités {range.Value.first}–{range.Value.last}"
                    : $"{head}\naucune entité mappée sur cet univers";
            }
            else
            {
                Node4Hint.Text = "⚠ univers non routé dans la config";
            }
            RefreshArtHeader();
        }

        /// <summary>
        /// Nœud ④ : affiche l'en-tête ArtNet RÉEL (les 18 premiers octets), tel
        /// que <see cref="ArtNetSender.BuildPacket"/> le fabriquerait. C'est ce
        /// qui distingue ④ de ③ : ③ montre les canaux, ④ montre l'enveloppe qui
        /// les transporte — et donc l'effet visible du numéro d'univers.
        /// </summary>
        private void RefreshArtHeader()
        {
            int typed = int.TryParse(TestUniverseBox.Text, out var u) ? u : 0;
            var routed = ResolveController(typed);
            int universe = routed?.local ?? typed;

            byte[] pkt = ArtNetSender.BuildPacket(universe, _lastDmx ?? Array.Empty<byte>());
            int dataLen = (pkt[16] << 8) | pkt[17];

            ArtHeaderText.Text =
                $"\"Art-Net\\0\"   {Hex(pkt, 0, 8)}\n" +
                $"OpCode ArtDMX {Hex(pkt, 8, 2)}\n" +
                $"Version 14    {Hex(pkt, 10, 2)}\n" +
                $"Seq / Phys    {Hex(pkt, 12, 2)}\n" +
                $"Univers {universe,-5} {Hex(pkt, 14, 2)}\n" +
                $"Longueur {dataLen,-4} {Hex(pkt, 16, 2)}";
        }

        /// <summary>Formate <paramref name="count"/> octets en hexadécimal.</summary>
        private static string Hex(byte[] data, int offset, int count)
        {
            var sb = new System.Text.StringBuilder(count * 3);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[offset + i].ToString("X2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Univers ArtNet sur lequel la config pose cette entité (null si non mappée).
        /// C'est la question inverse de <see cref="EntityRangeFor"/>, et c'est la
        /// vraie règle du routage : l'univers se déduit des ENTITÉS transportées,
        /// jamais du numéro d'univers eHuB (les deux numérotations sont distinctes).
        /// </summary>
        private int? UniverseForEntity(int entityId)
        {
            if (_config == null) return null;
            foreach (var m in _config.EntityMap)
                if (entityId >= m.EntityStart && entityId <= m.EntityEnd)
                    return m.UniverseStart;
            return null;
        }

        /// <summary>
        /// Fait le lien entre ce qu'on REÇOIT (①) et ce qui PARTIRAIT (④) : les
        /// entités du paquet eHuB observé sont converties, via la config, en
        /// univers ArtNet. Un même paquet peut en alimenter plusieurs — c'est
        /// justement ce qu'aucune correspondance de numéros ne pourrait deviner.
        /// Purement informatif : on ne force pas le champ, l'utilisateur reste maître.
        /// </summary>
        private void ShowUniversesFedBy(EhubReceiver.PacketSnapshot packet)
        {
            if (_config == null)
            {
                Node4Feeds.Text = "";
                return;
            }

            var fed = new SortedSet<int>();
            int unmapped = 0;
            foreach (int id in packet.EntityIds)
            {
                int? u = UniverseForEntity(id);
                if (u.HasValue) fed.Add(u.Value); else unmapped++;
            }

            if (fed.Count == 0)
            {
                Node4Feeds.Text = "⚠ aucune entité de ce paquet n'est mappée dans la config";
                return;
            }
            Node4Feeds.Text = $"ce paquet alimente les univers ArtNet : {string.Join(", ", fed)}"
                            + (unmapped > 0 ? $"  ({unmapped} entité(s) non mappée(s))" : "");
        }

        /// <summary>
        /// Plage d'entités que la config pose sur cet univers (null si aucune).
        /// C'est ce que le vrai routage utilise pour décider quelle entité part où.
        /// </summary>
        private (int first, int last)? EntityRangeFor(int universe)
        {
            if (_config == null) return null;
            int first = int.MaxValue, last = int.MinValue;
            foreach (var m in _config.EntityMap)
            {
                if (m.UniverseStart != universe) continue;
                if (m.EntityStart < first) first = m.EntityStart;
                if (m.EntityEnd > last) last = m.EntityEnd;
            }
            return first <= last ? (first, last) : null;
        }

        /// <summary>
        /// Cherche dans la config le contrôleur d'un univers donné. Accepte les
        /// deux numérotations (index global ou univers ArtNet local) et retourne
        /// aussi le numéro LOCAL (0..31), seul compris par le contrôleur.
        /// </summary>
        private (string ip, string id, int local, int port)? ResolveController(int universe)
        {
            if (_config == null) return null;
            foreach (var u in _config.Universes)
            {
                if (u.Index == universe || u.ArtNetUniverse == universe)
                {
                    foreach (var c in _config.Controllers)
                        if (c.Id == u.ControllerId) return (c.Ip, c.Id, u.EffectiveArtNetUniverse, c.Port);
                }
            }
            return null;
        }

        /// <summary>
        /// Moniteur du nœud ④ : dessine les <paramref name="count"/> premiers
        /// canaux de la trame DMX qui part (ou partirait) en ArtNet.
        /// 1 carré = 1 canal, niveau de gris = intensité (0 éteint → 255 blanc).
        /// </summary>
        private void PaintArtNetMonitor(byte[] dmx, int count) => PaintDmxGrid(ArtNetMonitor, dmx, count);

        /// <summary>Dessine une grille de canaux DMX (1 carré = 1 canal, gris = intensité) dans un panneau.</summary>
        private static void PaintDmxGrid(WrapPanel target, byte[] dmx, int count)
        {
            target.Children.Clear();
            count = Math.Min(count, dmx.Length);
            for (int i = 0; i < count; i++)
            {
                byte v = dmx[i];
                target.Children.Add(new Border
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
            if (_fakerAnimating)
            {
                long nowMs = Environment.TickCount64;
                if (_lastFakerTickMs == 0 || nowMs - _lastFakerTickMs >= _fakerDelayMs)
                {
                    _lastFakerTickMs = nowMs;
                    SendFakerFrame(_fakerFrame++); // faker animé
                }
            }
            UpdateDebugMonitors(); // onglet Débogage : marche même sans config chargée

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
            _fakerAnimating = false;   // arrête la boucle du faker
            _artStreaming = false;     // arrête l'émission ArtNet continue
            _engine?.Dispose();
            _ehubFaker?.Dispose();
            _debugRx?.Dispose();
            _artSender?.Dispose();
            _simUdp?.Dispose();
            base.OnClosed(e);
        }
    }
}
