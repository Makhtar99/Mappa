using System;
using System.Collections.Generic;

namespace Mappa
{
    /// <summary>
    /// Rendu de texte sur le mur LED (outil d'authoring / demo, cote Personne C).
    ///
    /// Ce module ne fait PAS partie du contrat de routage : il se contente
    /// d'ecrire des couleurs dans un <see cref="State"/> via des IDs d'entites,
    /// exactement comme le ferait l'authoring. Il s'appuie sur la geometrie
    /// deterministe du mur genere par <see cref="Wall"/> pour convertir une
    /// position (x, y) en ID d'entite.
    ///
    /// Police bitmap 3x5 integree (chiffres, lettres A-Z et quelques symboles).
    /// Les caracteres inconnus sont rendus comme un espace.
    /// </summary>
    public static class Text
    {
        public const int GlyphWidth = 3;
        public const int GlyphHeight = 5;

        /// <summary>
        /// Convertit une position logique (col, row) en ID d'entite, via la
        /// geometrie fournie (ou <see cref="WallGeometry.Default"/> si null).
        /// row 0 = haut du mur. Retourne -1 si hors du mur / LED de fixation.
        /// </summary>
        public static int WallEntityId(int col, int row, WallGeometry? geo = null)
        {
            return (geo ?? WallGeometry.Default).EntityId(col, row);
        }

        /// <summary>
        /// Ecrit une chaine dans le state a partir du coin (originX, originY),
        /// avec retour a la ligne automatique pour tenir dans la largeur du mur.
        /// Renvoie le nombre de pixels reellement allumes.
        /// </summary>
        public static int DrawString(
            State state, string text,
            int originX = 0, int originY = 0,
            int r = 255, int g = 255, int b = 255,
            WallGeometry? geo = null,
            int letterSpacing = 1, int lineSpacing = 1,
            bool flipX = false, bool flipY = false)
        {
            geo ??= WallGeometry.Default;
            int advance = GlyphWidth + letterSpacing;
            int lineStep = GlyphHeight + lineSpacing;
            int cursorX = originX;
            int cursorY = originY;
            int litPixels = 0;

            foreach (char rawCh in text)
            {
                char ch = char.ToUpperInvariant(rawCh);
                if (ch == '\n')
                {
                    cursorX = originX;
                    cursorY += lineStep;
                    continue;
                }

                // Retour a la ligne auto si le prochain glyphe deborde du mur.
                if (cursorX + GlyphWidth > geo.Columns && cursorX > originX)
                {
                    cursorX = originX;
                    cursorY += lineStep;
                }

                litPixels += DrawGlyph(state, ch, cursorX, cursorY, r, g, b, geo, flipX, flipY);
                cursorX += advance;
            }
            return litPixels;
        }

        /// <summary>Dessine un seul glyphe. Renvoie le nombre de pixels allumes.</summary>
        public static int DrawGlyph(
            State state, char ch, int x, int y,
            int r, int g, int b,
            WallGeometry geo,
            bool flipX, bool flipY)
        {
            if (!Font.TryGetValue(char.ToUpperInvariant(ch), out byte[]? rows)) return 0;
            int lit = 0;
            for (int gy = 0; gy < GlyphHeight; gy++)
            {
                byte lineBits = rows![gy];
                for (int gx = 0; gx < GlyphWidth; gx++)
                {
                    // Bit de poids fort = colonne de gauche.
                    bool on = (lineBits & (1 << (GlyphWidth - 1 - gx))) != 0;
                    if (!on) continue;

                    int px = flipX ? (geo.Columns - 1 - (x + gx)) : (x + gx);
                    int py = flipY ? (geo.Rows - 1 - (y + gy)) : (y + gy);
                    int id = geo.EntityId(px, py);
                    if (id < 0) continue;
                    if (state.Contains(id))
                    {
                        state.SetRgb(id, r, g, b);
                        lit++;
                    }
                }
            }
            return lit;
        }

        /// <summary>Largeur en pixels d'un texte a une echelle donnee (sans retour ligne).</summary>
        public static int MeasureWidth(string text, int scale = 1, int letterSpacing = 1)
        {
            int n = text.Length;
            if (n == 0) return 0;
            return n * GlyphWidth * scale + (n - 1) * letterSpacing * scale;
        }

        /// <summary>Hauteur en pixels d'une ligne a une echelle donnee.</summary>
        public static int MeasureHeight(int scale = 1) => GlyphHeight * scale;

        /// <summary>
        /// Dessine un texte AGRANDI (facteur <paramref name="scale"/>) a partir du
        /// coin (originX, originY), sans retour a la ligne (une seule ligne).
        /// Chaque pixel de la police devient un carre scale x scale. Renvoie le
        /// nombre de LED allumees.
        /// </summary>
        public static int DrawStringScaled(
            State state, string text,
            int originX, int originY,
            int r, int g, int b,
            int scale = 1, int letterSpacing = 1,
            WallGeometry? geo = null,
            bool flipX = false, bool flipY = false)
        {
            geo ??= WallGeometry.Default;
            if (scale < 1) scale = 1;
            int lit = 0;
            int cx = originX;
            foreach (char rawCh in text)
            {
                char ch = char.ToUpperInvariant(rawCh);
                if (Font.TryGetValue(ch, out byte[]? rows))
                {
                    for (int gy = 0; gy < GlyphHeight; gy++)
                    {
                        byte lineBits = rows![gy];
                        for (int gx = 0; gx < GlyphWidth; gx++)
                        {
                            if ((lineBits & (1 << (GlyphWidth - 1 - gx))) == 0) continue;
                            // Chaque pixel devient un bloc scale x scale.
                            for (int sy = 0; sy < scale; sy++)
                            {
                                for (int sx = 0; sx < scale; sx++)
                                {
                                    int x = cx + gx * scale + sx;
                                    int y = originY + gy * scale + sy;
                                    int px = flipX ? (geo.Columns - 1 - x) : x;
                                    int py = flipY ? (geo.Rows - 1 - y) : y;
                                    int id = geo.EntityId(px, py);
                                    if (id < 0 || !state.Contains(id)) continue;
                                    state.SetRgb(id, r, g, b);
                                    lit++;
                                }
                            }
                        }
                    }
                }
                cx += (GlyphWidth + letterSpacing) * scale;
            }
            return lit;
        }

        /// <summary>
        /// Police 3x5. Chaque caractere = 5 octets (une ligne du haut vers le bas),
        /// les 3 bits de poids faible codent les colonnes (gauche -> droite).
        /// </summary>
        private static readonly Dictionary<char, byte[]> Font = new Dictionary<char, byte[]>
        {
            [' '] = new byte[] { 0b000, 0b000, 0b000, 0b000, 0b000 },
            ['A'] = new byte[] { 0b010, 0b101, 0b111, 0b101, 0b101 },
            ['B'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b110 },
            ['C'] = new byte[] { 0b011, 0b100, 0b100, 0b100, 0b011 },
            ['D'] = new byte[] { 0b110, 0b101, 0b101, 0b101, 0b110 },
            ['E'] = new byte[] { 0b111, 0b100, 0b110, 0b100, 0b111 },
            ['F'] = new byte[] { 0b111, 0b100, 0b110, 0b100, 0b100 },
            ['G'] = new byte[] { 0b011, 0b100, 0b101, 0b101, 0b011 },
            ['H'] = new byte[] { 0b101, 0b101, 0b111, 0b101, 0b101 },
            ['I'] = new byte[] { 0b111, 0b010, 0b010, 0b010, 0b111 },
            ['J'] = new byte[] { 0b001, 0b001, 0b001, 0b101, 0b010 },
            ['K'] = new byte[] { 0b101, 0b101, 0b110, 0b101, 0b101 },
            ['L'] = new byte[] { 0b100, 0b100, 0b100, 0b100, 0b111 },
            ['M'] = new byte[] { 0b101, 0b111, 0b111, 0b101, 0b101 },
            ['N'] = new byte[] { 0b101, 0b111, 0b111, 0b111, 0b101 },
            ['O'] = new byte[] { 0b010, 0b101, 0b101, 0b101, 0b010 },
            ['P'] = new byte[] { 0b110, 0b101, 0b110, 0b100, 0b100 },
            ['Q'] = new byte[] { 0b010, 0b101, 0b101, 0b110, 0b011 },
            ['R'] = new byte[] { 0b110, 0b101, 0b110, 0b101, 0b101 },
            ['S'] = new byte[] { 0b011, 0b100, 0b010, 0b001, 0b110 },
            ['T'] = new byte[] { 0b111, 0b010, 0b010, 0b010, 0b010 },
            ['U'] = new byte[] { 0b101, 0b101, 0b101, 0b101, 0b011 },
            ['V'] = new byte[] { 0b101, 0b101, 0b101, 0b010, 0b010 },
            ['W'] = new byte[] { 0b101, 0b101, 0b111, 0b111, 0b101 },
            ['X'] = new byte[] { 0b101, 0b101, 0b010, 0b101, 0b101 },
            ['Y'] = new byte[] { 0b101, 0b101, 0b010, 0b010, 0b010 },
            ['Z'] = new byte[] { 0b111, 0b001, 0b010, 0b100, 0b111 },
            ['0'] = new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 },
            ['1'] = new byte[] { 0b010, 0b110, 0b010, 0b010, 0b111 },
            ['2'] = new byte[] { 0b110, 0b001, 0b010, 0b100, 0b111 },
            ['3'] = new byte[] { 0b110, 0b001, 0b010, 0b001, 0b110 },
            ['4'] = new byte[] { 0b101, 0b101, 0b111, 0b001, 0b001 },
            ['5'] = new byte[] { 0b111, 0b100, 0b110, 0b001, 0b110 },
            ['6'] = new byte[] { 0b011, 0b100, 0b110, 0b101, 0b010 },
            ['7'] = new byte[] { 0b111, 0b001, 0b010, 0b010, 0b010 },
            ['8'] = new byte[] { 0b010, 0b101, 0b010, 0b101, 0b010 },
            ['9'] = new byte[] { 0b010, 0b101, 0b011, 0b001, 0b110 },
            ['!'] = new byte[] { 0b010, 0b010, 0b010, 0b000, 0b010 },
            ['.'] = new byte[] { 0b000, 0b000, 0b000, 0b000, 0b010 },
            [','] = new byte[] { 0b000, 0b000, 0b000, 0b010, 0b100 },
            ['-'] = new byte[] { 0b000, 0b000, 0b111, 0b000, 0b000 },
            ['?'] = new byte[] { 0b110, 0b001, 0b010, 0b000, 0b010 },
            [':'] = new byte[] { 0b000, 0b010, 0b000, 0b010, 0b000 },
        };

        /// <summary>Vrai si le caractere a un glyphe dans la police.</summary>
        public static bool Supports(char ch) => Font.ContainsKey(char.ToUpperInvariant(ch));
    }
}
