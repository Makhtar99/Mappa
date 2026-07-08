using System;
using Avalonia;

namespace Mappa.Ui
{
    /// <summary>
    /// Point d'entree de l'UI de routage (Personne A). L'UI ne fait QUE lire un
    /// snapshot publie par le RoutingEngine ; toute la boucle temps reel (rendu
    /// DMX + emission ArtNet a 40 Hz) tourne sur un thread separe (exigence P2 :
    /// routage sur un thread distinct de l'UI).
    /// </summary>
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
