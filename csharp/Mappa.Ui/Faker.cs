using System;
using Mappa;

namespace Mappa.Ui
{
    /// <summary>
    /// Generateur de signal de test (exigence P8 : "fakers"). Permet de valider
    /// tout le pipeline de routage/emission SANS dependre d'Unity ni d'un flux
    /// eHuB : on injecte une animation synthetique directement dans le State.
    /// </summary>
    public interface IFaker
    {
        void Fill(State state);
    }

    /// <summary>
    /// Onde arc-en-ciel qui balaie toutes les entites : chaque entite recoit une
    /// teinte fonction de son rang, decalee dans le temps. Utile pour reperer
    /// visuellement les trous de mapping, le tearing et les couleurs inversees.
    /// </summary>
    public sealed class RainbowFaker : IFaker
    {
        private double _t;

        public void Fill(State state)
        {
            _t += 0.02;
            var ids = state.EntityIds;
            int n = ids.Count;
            if (n == 0) return;

            for (int i = 0; i < n; i++)
            {
                double hue = (i / (double)n + _t) % 1.0;
                HsvToRgb(hue, 1.0, 1.0, out byte r, out byte g, out byte b);
                state.Set(ids[i], r, g, b);
            }
            state.MarkUpdated();
        }

        private static void HsvToRgb(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            double hh = (h % 1.0) * 6.0;
            int i = (int)Math.Floor(hh);
            double f = hh - i;
            double p = v * (1 - s);
            double q = v * (1 - s * f);
            double t = v * (1 - s * (1 - f));
            double rd, gd, bd;
            switch (i % 6)
            {
                case 0: rd = v; gd = t; bd = p; break;
                case 1: rd = q; gd = v; bd = p; break;
                case 2: rd = p; gd = v; bd = t; break;
                case 3: rd = p; gd = q; bd = v; break;
                case 4: rd = t; gd = p; bd = v; break;
                default: rd = v; gd = p; bd = q; break;
            }
            r = (byte)(rd * 255);
            g = (byte)(gd * 255);
            b = (byte)(bd * 255);
        }
    }
}
