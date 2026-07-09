using System;
using Mappa;

namespace Mappa.Authoring.Core
{
    public readonly struct ColorF
    {
        public readonly float R, G, B, W;

        public ColorF(float r, float g, float b, float w = 0f)
        {
            R = r; G = g; B = b; W = w;
        }

        public static readonly ColorF Black = new ColorF(0, 0, 0, 0);
        public static readonly ColorF White = new ColorF(255, 255, 255, 0);

        public ColorF Scale(float k) => new ColorF(R * k, G * k, B * k, W * k);

        public static ColorF Lerp(ColorF a, ColorF b, float t)
        {
            return new ColorF(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t,
                a.W + (b.W - a.W) * t);
        }

        private static byte Clamp8(float v)
        {
            if (v <= 0f) return 0;
            if (v >= 255f) return (byte)255;
            return (byte)(v + 0.5f);
        }

        public Color ToColor() => new Color(Clamp8(R), Clamp8(G), Clamp8(B), Clamp8(W));

        public static ColorF FromHsv(float h, float s, float v)
        {
            h = h - (float)Math.Floor(h);
            float r = 0, g = 0, b = 0;
            int i = (int)(h * 6f);
            float f = h * 6f - i;
            float p = v * (1f - s);
            float q = v * (1f - f * s);
            float t = v * (1f - (1f - f) * s);
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
            }
            return new ColorF(r * 255f, g * 255f, b * 255f);
        }
    }
}
