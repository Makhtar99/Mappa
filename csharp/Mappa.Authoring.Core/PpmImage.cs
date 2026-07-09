using System;
using System.IO;
using System.Text;

namespace Mappa.Authoring.Core
{
    public sealed class PpmImage
    {
        public int Width { get; }
        public int Height { get; }
        private readonly byte[] _rgb;

        public PpmImage(int width, int height, byte[] rgb)
        {
            Width = width;
            Height = height;
            _rgb = rgb;
        }

        public (byte R, byte G, byte B) SampleNormalized(float u, float v)
        {
            if (Width == 0 || Height == 0) return (0, 0, 0);
            int x = (int)(Clamp01(u) * (Width - 1) + 0.5f);
            int y = (int)(Clamp01(v) * (Height - 1) + 0.5f);
            int i = (y * Width + x) * 3;
            return (_rgb[i], _rgb[i + 1], _rgb[i + 2]);
        }

        private static float Clamp01(float t) => t < 0 ? 0 : (t > 1 ? 1 : t);

        public static PpmImage Load(string path)
        {
            using var fs = File.OpenRead(path);
            return Load(fs);
        }

        public static PpmImage Load(Stream stream)
        {
            var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            string magic = ReadToken(br);
            if (magic != "P6" && magic != "P3")
                throw new InvalidDataException($"Format PPM non supporte : {magic}");

            int width = int.Parse(ReadToken(br));
            int height = int.Parse(ReadToken(br));
            int maxVal = int.Parse(ReadToken(br));
            if (maxVal <= 0 || maxVal > 255)
                throw new InvalidDataException($"maxval PPM non supporte : {maxVal}");

            var rgb = new byte[width * height * 3];
            if (magic == "P6")
            {
                int read = 0;
                while (read < rgb.Length)
                {
                    int n = br.Read(rgb, read, rgb.Length - read);
                    if (n <= 0) throw new EndOfStreamException("PPM P6 tronque.");
                    read += n;
                }
            }
            else
            {
                for (int i = 0; i < rgb.Length; i++)
                    rgb[i] = (byte)int.Parse(ReadToken(br));
            }
            return new PpmImage(width, height, rgb);
        }

        private static string ReadToken(BinaryReader br)
        {
            var sb = new StringBuilder();
            int c;
            while (true)
            {
                c = br.Read();
                if (c < 0) throw new EndOfStreamException("En-tete PPM incomplet.");
                if (c == '#')
                {
                    while (c != '\n' && c >= 0) c = br.Read();
                    continue;
                }
                if (!char.IsWhiteSpace((char)c)) break;
            }
            do
            {
                sb.Append((char)c);
                c = br.Read();
            } while (c >= 0 && !char.IsWhiteSpace((char)c) && c != '#');
            return sb.ToString();
        }
    }
}
