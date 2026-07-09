using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Mappa;
using Mappa.Authoring.Core;

namespace Mappa.Authoring.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            switch (args[0])
            {
                case "emit": return Emit(args);
                case "monitor": return Monitor(args);
                case "sample-show": return SampleShow(args);
                default:
                    PrintUsage();
                    return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Mappa.Authoring.Cli — outil de creation (Personne C)");
            Console.WriteLine();
            Console.WriteLine("  emit [--host H] [--port P] [--seconds N] [--config PATH|wall] [--show PATH]");
            Console.WriteLine("  monitor [--port P] [--seconds N]");
            Console.WriteLine("  sample-show PATH");
        }

        private static string Opt(string[] args, string name, string def)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return def;
        }

        private static int Emit(string[] args)
        {
            string host = Opt(args, "--host", "127.0.0.1");
            int port = int.Parse(Opt(args, "--port", Ehub.DefaultUdpPort.ToString()));
            double seconds = double.Parse(Opt(args, "--seconds", "10"));
            string configArg = Opt(args, "--config", "wall");
            string showPath = Opt(args, "--show", "");

            Config config = configArg == "wall"
                ? Wall.BuildWallConfig()
                : Persistence.LoadConfig(configArg);

            var layout = EntityLayout.FromGrid(config);
            Show show = showPath.Length > 0 ? ShowFile.Load(showPath) : DemoShows.BuildDemo(configArg);

            Console.WriteLine($"Entites : {layout.Ids.Count}  |  grille {layout.Cols}x{layout.Rows}");
            using var sender = new EhubSender(host, port);
            using var runner = new ShowRunner(show, layout, sender);
            Console.WriteLine($"Chunks eHuB : {runner.Chunks.Count}  ->  {host}:{port} @ {show.Fps} Hz");

            runner.Start();
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                Thread.Sleep(500);
                Console.Write($"\rt={runner.LastFrameTime,6:F2}s  frames={runner.FramesSent}   ");
            }
            runner.Stop();
            Console.WriteLine();
            Console.WriteLine($"Termine : {runner.FramesSent} frames en {sw.Elapsed.TotalSeconds:F1}s " +
                              $"(~{runner.FramesSent / sw.Elapsed.TotalSeconds:F1} fps).");
            return 0;
        }

        private static int Monitor(string[] args)
        {
            int port = int.Parse(Opt(args, "--port", Ehub.DefaultUdpPort.ToString()));
            double seconds = double.Parse(Opt(args, "--seconds", "10"));

            using var udp = new UdpClient(port);
            var remote = new IPEndPoint(IPAddress.Any, 0);
            Console.WriteLine($"Ecoute eHuB sur udp/{port} pendant {seconds}s...");

            var sw = Stopwatch.StartNew();
            int updates = 0, configs = 0;
            var universes = new HashSet<int>();
            udp.Client.ReceiveTimeout = 500;

            while (sw.Elapsed.TotalSeconds < seconds)
            {
                byte[] data;
                try { data = udp.Receive(ref remote); }
                catch (SocketException) { continue; }

                Ehub.Message msg;
                try { msg = Ehub.Decode(data); }
                catch (Exception ex) { Console.WriteLine($"  paquet invalide : {ex.Message}"); continue; }

                universes.Add(msg.Universe);
                if (msg.Type == Ehub.TypeUpdate)
                {
                    updates++;
                    if (updates <= 3 || updates % 40 == 0)
                    {
                        int first = msg.Payload.Length >= Ehub.SextuorSize ? Ehub.ReadU16(msg.Payload, 0) : -1;
                        Console.WriteLine($"  update u={msg.Universe} entites={msg.Count} " +
                                          $"payload={msg.Payload.Length}o  1re entite={first}");
                    }
                }
                else if (msg.Type == Ehub.TypeConfig)
                {
                    configs++;
                    var ranges = Ehub.DecodeRanges(msg);
                    Console.WriteLine($"  config u={msg.Universe} plages={ranges.Count}");
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Recu : {updates} updates, {configs} configs, " +
                              $"univers={string.Join(",", universes)} " +
                              $"(~{updates / sw.Elapsed.TotalSeconds:F1} updates/s).");
            return 0;
        }

        private static int SampleShow(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("usage : sample-show PATH");
                return 1;
            }
            var show = DemoShows.BuildDemo("wall.json");
            ShowFile.Save(show, args[1]);
            Console.WriteLine($"Show de demo ecrit : {args[1]}");
            return 0;
        }
    }
}
