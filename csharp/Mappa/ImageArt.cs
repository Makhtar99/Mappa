using System;
using System.IO;

namespace Mappa
{
    /// <summary>
    /// Image raster RVB simple (outil d'authoring / demo, cote Personne C).
    ///
    /// Comme <see cref="Text"/>, ce module ne fait PAS partie du contrat de
    /// routage : il produit une grille de pixels RVB, puis la "blitte" dans un
    /// <see cref="State"/> via <see cref="Text.WallEntityId"/> (position -> entite).
    ///
    /// Deux usages :
    ///  - charger un fichier PPM (P3/P6) — format bitmap trivial, sans dependance
    ///    externe (compatible Unity) ;
    ///  - dessiner par code (SetPixel, ligne, rect, cercle, degrade...).
    /// </summary>
    public sealed class ImageArt
    {
        public int Width { get; }
        public int Height { get; }
        private readonly byte[] _rgb; // 3 octets/pixel, ligne par ligne (y*W + x)

        public ImageArt(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException("Dimensions invalides.");
            Width = width;
            Height = height;
            _rgb = new byte[width * height * 3];
        }

        // -------------------------------------------------------------- //
        // Dessin par code
        // -------------------------------------------------------------- //
        public void SetPixel(int x, int y, int r, int g, int b)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return;
            int i = (y * Width + x) * 3;
            _rgb[i] = Clamp(r); _rgb[i + 1] = Clamp(g); _rgb[i + 2] = Clamp(b);
        }

        public (byte R, byte G, byte B) GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) return (0, 0, 0);
            int i = (y * Width + x) * 3;
            return (_rgb[i], _rgb[i + 1], _rgb[i + 2]);
        }

        public void Fill(int r, int g, int b)
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    SetPixel(x, y, r, g, b);
        }

        public void FillRect(int x0, int y0, int w, int h, int r, int g, int b)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                    SetPixel(x, y, r, g, b);
        }

        /// <summary>Ligne (algorithme de Bresenham).</summary>
        public void Line(int x0, int y0, int x1, int y1, int r, int g, int b)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                SetPixel(x0, y0, r, g, b);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Cercle (rempli ou contour).</summary>
        public void Circle(int cx, int cy, int radius, int r, int g, int b, bool filled = true)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    int d2 = x * x + y * y;
                    bool on = filled ? d2 <= radius * radius
                                     : Math.Abs(d2 - radius * radius) <= radius;
                    if (on) SetPixel(cx + x, cy + y, r, g, b);
                }
            }
        }

        /// <summary>Degrade vertical (haut -> bas) entre deux couleurs.</summary>
        public void GradientVertical(int r0, int g0, int b0, int r1, int g1, int b1)
        {
            for (int y = 0; y < Height; y++)
            {
                double t = Height > 1 ? (double)y / (Height - 1) : 0.0;
                int r = (int)(r0 + (r1 - r0) * t);
                int g = (int)(g0 + (g1 - g0) * t);
                int b = (int)(b0 + (b1 - b0) * t);
                for (int x = 0; x < Width; x++) SetPixel(x, y, r, g, b);
            }
        }

        // -------------------------------------------------------------- //
        // Blit vers le State (affichage sur le mur)
        // -------------------------------------------------------------- //
        /// <summary>
        /// Dessine l'image dans le state, redimensionnee (nearest-neighbor) vers
        /// <paramref name="targetW"/> x <paramref name="targetH"/> (defaut = taille
        /// du mur), a partir du coin (originX, originY). Renvoie le nb de LED allumees.
        /// Les pixels noirs (0,0,0) sont ignores par defaut (fond transparent).
        /// </summary>
        public int BlitToState(
            State state,
            int originX = 0, int originY = 0,
            int targetW = 128, int targetH = 128,
            int wallCols = 128, int wallRows = 128,
            bool flipX = false, bool flipY = false,
            bool skipBlack = true,
            double brightness = 1.0)
        {
            if (brightness < 0) brightness = 0;
            int lit = 0;
            for (int ty = 0; ty < targetH; ty++)
            {
                for (int tx = 0; tx < targetW; tx++)
                {
                    // nearest-neighbor : pixel source correspondant
                    int sx = targetW > 1 ? tx * Width / targetW : 0;
                    int sy = targetH > 1 ? ty * Height / targetH : 0;
                    var (r, g, b) = GetPixel(sx, sy);
                    if (skipBlack && r == 0 && g == 0 && b == 0) continue;

                    // Attenuation d'intensite (les LED sont tres lumineuses).
                    int rr = (int)(r * brightness);
                    int gg = (int)(g * brightness);
                    int bb = (int)(b * brightness);

                    int wx = originX + tx;
                    int wy = originY + ty;
                    int px = flipX ? (wallCols - 1 - wx) : wx;
                    int py = flipY ? (wallRows - 1 - wy) : wy;
                    int id = Text.WallEntityId(px, py, wallCols, wallRows);
                    if (id < 0 || !state.Contains(id)) continue;
                    state.SetRgb(id, rr, gg, bb);
                    lit++;
                }
            }
            return lit;
        }

        private static byte Clamp(int v) => v < 0 ? (byte)0 : v > 255 ? (byte)255 : (byte)v;

        // -------------------------------------------------------------- //
        // Chargement PPM (P3 texte, P6 binaire)
        // -------------------------------------------------------------- //
        public static ImageArt LoadPpm(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int pos = 0;

            string Token()
            {
                // saute espaces + commentaires (#... jusqu'a fin de ligne)
                while (pos < bytes.Length)
                {
                    char c = (char)bytes[pos];
                    if (c == '#') { while (pos < bytes.Length && bytes[pos] != '\n') pos++; }
                    else if (char.IsWhiteSpace(c)) pos++;
                    else break;
                }
                int start = pos;
                while (pos < bytes.Length && !char.IsWhiteSpace((char)bytes[pos])) pos++;
                return System.Text.Encoding.ASCII.GetString(bytes, start, pos - start);
            }

            string magic = Token();
            if (magic != "P3" && magic != "P6")
                throw new InvalidDataException($"Format PPM non supporte : '{magic}' (attendu P3 ou P6).");
            int w = int.Parse(Token());
            int h = int.Parse(Token());
            int maxval = int.Parse(Token());
            if (maxval <= 0) maxval = 255;

            var img = new ImageArt(w, h);

            if (magic == "P3")
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int r = int.Parse(Token()) * 255 / maxval;
                        int g = int.Parse(Token()) * 255 / maxval;
                        int b = int.Parse(Token()) * 255 / maxval;
                        img.SetPixel(x, y, r, g, b);
                    }
            }
            else // P6 : un seul octet de separation apres maxval, puis donnees binaires
            {
                pos++; // saute l'unique whitespace apres maxval
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int r = bytes[pos++] * 255 / maxval;
                        int g = bytes[pos++] * 255 / maxval;
                        int b = bytes[pos++] * 255 / maxval;
                        img.SetPixel(x, y, r, g, b);
                    }
            }
            return img;
        }
    }
}
