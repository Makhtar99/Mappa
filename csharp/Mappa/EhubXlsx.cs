using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace Mappa
{
    /// <summary>
    /// Lecteur .xlsx natif (sans dependance NuGet) du tableau d'adressage eHuB.
    ///
    /// Un fichier .xlsx est une archive ZIP de XML. On lit la table des chaines
    /// partagees (xl/sharedStrings.xml) et la feuille voulue, puis on mappe les
    /// colonnes par leur en-tete ("Name", "Entity Start", "Entity End",
    /// "ArtNet IP", "ArtNet Universe"). N'utilise que la BCL (System.IO.Compression
    /// + System.Xml.Linq) : le coeur Mappa reste sans dependance externe. Cohabite
    /// avec Persistence (qui fait deja de l'I/O fichier), et sert aussi bien au CLI
    /// qu'a l'UI (import .xlsx depuis l'interface).
    /// </summary>
    public static class EhubXlsx
    {
        private static readonly XNamespace Main =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PkgRel =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        public static List<EhubRow> Read(string path, string? sheetName = null)
        {
            using var zip = ZipFile.OpenRead(path);

            var shared = ReadSharedStrings(zip);
            string sheetPath = ResolveSheetPath(zip, sheetName);
            var rows = ReadSheetRows(zip, sheetPath, shared);

            if (rows.Count == 0)
                throw new InvalidDataException("Feuille vide ou introuvable.");

            // 1ere ligne = en-tetes : on mappe colonne -> champ.
            var header = rows[0];
            var colToField = new Dictionary<string, string>();
            foreach (var kv in header)
            {
                string h = kv.Value.Trim().ToLowerInvariant();
                string field = h switch
                {
                    "name" => "name",
                    "entity start" => "start",
                    "entity end" => "end",
                    "artnet ip" => "ip",
                    "artnet universe" => "universe",
                    _ => "",
                };
                if (field != "") colToField[kv.Key] = field;
            }

            foreach (var need in new[] { "start", "end", "ip", "universe" })
            {
                if (!colToField.ContainsValue(need))
                    throw new InvalidDataException(
                        $"Colonne manquante dans l'Excel (attendu 'Entity Start/End', 'ArtNet IP', 'ArtNet Universe').");
            }

            var result = new List<EhubRow>();
            for (int i = 1; i < rows.Count; i++)
            {
                var cells = rows[i];
                string Get(string field)
                {
                    foreach (var kv in colToField)
                        if (kv.Value == field && cells.TryGetValue(kv.Key, out string? v)) return v;
                    return "";
                }

                string sStart = Get("start"), sEnd = Get("end"),
                       sIp = Get("ip"), sUni = Get("universe"), sName = Get("name");

                if (!int.TryParse(sStart, out int start) ||
                    !int.TryParse(sEnd, out int end) ||
                    string.IsNullOrWhiteSpace(sIp) ||
                    !int.TryParse(sUni, out int uni))
                {
                    continue; // ligne incomplete -> ignoree
                }

                result.Add(new EhubRow
                {
                    Name = string.IsNullOrWhiteSpace(sName) ? $"strip-{i}" : sName.Trim(),
                    EntityStart = start,
                    EntityEnd = end,
                    Ip = sIp.Trim(),
                    Universe = uni,
                    LedType = LedType.RGB,
                });
            }
            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;
            using var s = entry.Open();
            var doc = XDocument.Load(s);
            foreach (var si in doc.Root!.Elements(Main + "si"))
            {
                // <si><t>texte</t></si> ou <si><r><t>...</t></r>...</si>
                var sb = new System.Text.StringBuilder();
                foreach (var t in si.Descendants(Main + "t")) sb.Append(t.Value);
                list.Add(sb.ToString());
            }
            return list;
        }

        private static string ResolveSheetPath(ZipArchive zip, string? sheetName)
        {
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return "xl/worksheets/sheet1.xml";

            string? targetRid = null;
            using (var s = wbEntry.Open())
            {
                var wb = XDocument.Load(s);
                var sheets = wb.Root!.Element(Main + "sheets")?.Elements(Main + "sheet").ToList()
                             ?? new List<XElement>();
                XElement? chosen = null;
                if (sheetName != null)
                    chosen = sheets.FirstOrDefault(e =>
                        string.Equals((string?)e.Attribute("name"), sheetName, StringComparison.OrdinalIgnoreCase));
                chosen ??= sheets.FirstOrDefault();
                targetRid = (string?)chosen?.Attribute(Rel + "id");
            }
            if (targetRid == null) return "xl/worksheets/sheet1.xml";

            // Resout rId -> chemin via xl/_rels/workbook.xml.rels
            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry == null) return "xl/worksheets/sheet1.xml";
            using (var s = relsEntry.Open())
            {
                var rels = XDocument.Load(s);
                var rel = rels.Root!.Elements(PkgRel + "Relationship")
                    .FirstOrDefault(e => (string?)e.Attribute("Id") == targetRid);
                string target = (string?)rel?.Attribute("Target") ?? "worksheets/sheet1.xml";
                if (target.StartsWith("/")) return target.TrimStart('/');
                return "xl/" + target;
            }
        }

        /// <summary>Lit la feuille : liste de lignes, chaque ligne = {colonne -> texte}.</summary>
        private static List<Dictionary<string, string>> ReadSheetRows(
            ZipArchive zip, string sheetPath, List<string> shared)
        {
            var result = new List<Dictionary<string, string>>();
            var entry = zip.GetEntry(sheetPath);
            if (entry == null) return result;

            using var s = entry.Open();
            var doc = XDocument.Load(s);
            var sheetData = doc.Root!.Element(Main + "sheetData");
            if (sheetData == null) return result;

            foreach (var row in sheetData.Elements(Main + "row"))
            {
                var cells = new Dictionary<string, string>();
                foreach (var c in row.Elements(Main + "c"))
                {
                    string reference = (string?)c.Attribute("r") ?? "";
                    string col = ColumnOf(reference);
                    string type = (string?)c.Attribute("t") ?? "";
                    var vEl = c.Element(Main + "v");
                    string value;
                    if (type == "s")
                    {
                        // valeur = index dans sharedStrings
                        value = (vEl != null && int.TryParse(vEl.Value, out int idx) && idx < shared.Count)
                            ? shared[idx] : "";
                    }
                    else if (type == "inlineStr")
                    {
                        value = c.Element(Main + "is")?.Descendants(Main + "t")
                                    .Aggregate("", (a, t) => a + t.Value) ?? "";
                    }
                    else
                    {
                        value = vEl?.Value ?? "";
                    }
                    if (col != "") cells[col] = value;
                }
                result.Add(cells);
            }
            return result;
        }

        /// <summary>Extrait les lettres de colonne d'une reference de cellule ("B12" -> "B").</summary>
        private static string ColumnOf(string cellRef)
        {
            int i = 0;
            while (i < cellRef.Length && char.IsLetter(cellRef[i])) i++;
            return cellRef.Substring(0, i);
        }
    }
}