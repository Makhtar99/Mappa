using System;
using System.Diagnostics;
using Mappa;

// Banc de perf : valide que render() tient le budget d'une frame a 40 Hz
// (25 ms) sur une installation de la taille du mur de test.
//
// Le juge de paix de P2, c'est la synchro son/lumiere : si render() + envoi ne
// tiennent pas dans 25 ms, le spectacle decroche. Ce bench isole render()
// (projection state -> paquets DMX) qui est le coeur cote routage.

internal static class Program
{
    private const double FrameBudgetMs = 1000.0 / 40.0; // 25 ms

    private static int Main(string[] args)
    {
        // Mur "large" : 128 colonnes de 128 LED = 16 384 entites (cas cible doc).
        var config = Wall.BuildWallConfig(
            name: "bench-16k", columns: 128, ledsPerColumn: 128,
            columnStride: 300, quarters: 8, quarterOffset: 40000);

        int entities = config.EntityIds().Count;
        var plan = new RoutingPlan(config);
        var state = State.FromConfig(config);

        // Remplit le state (comme le ferait l'authoring C).
        var ids = state.EntityIds;
        for (int i = 0; i < ids.Count; i++)
        {
            state.Set(ids[i], (i * 7) & 255, (i * 13) & 255, (i * 29) & 255);
        }

        Console.WriteLine($"Entites      : {entities}");
        Console.WriteLine($"Univers      : {plan.Universes.Count}");
        Console.WriteLine($"Budget frame : {FrameBudgetMs:F2} ms (40 Hz)");

        // Warmup (JIT).
        for (int i = 0; i < 200; i++) plan.Render(state);

        const int frames = 4000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < frames; i++) plan.Render(state);
        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double perFrameMs = totalMs / frames;
        double achievableHz = 1000.0 / perFrameMs;
        double budgetUsedPct = perFrameMs / FrameBudgetMs * 100.0;

        Console.WriteLine();
        Console.WriteLine($"render() moyen : {perFrameMs:F4} ms/frame");
        Console.WriteLine($"debit max      : {achievableHz:F0} Hz");
        Console.WriteLine($"budget utilise : {budgetUsedPct:F1} % du budget 40 Hz");

        bool ok = perFrameMs < FrameBudgetMs;
        Console.WriteLine();
        Console.WriteLine(ok
            ? $"OK : render() tient largement le budget 40 Hz (marge x{FrameBudgetMs / perFrameMs:F0})."
            : "ATTENTION : render() depasse le budget de 25 ms — optimisation requise.");
        return ok ? 0 : 1;
    }
}
