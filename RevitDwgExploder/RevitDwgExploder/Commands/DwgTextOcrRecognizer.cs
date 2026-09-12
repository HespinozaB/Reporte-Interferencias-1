using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Tesseract;
using Bitmap = System.Drawing.Bitmap;
using Graphics = System.Drawing.Graphics;
using DrawingColor = System.Drawing.Color;
using Pen = System.Drawing.Pen;
using PointF = System.Drawing.PointF;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Alternativa que NO depende del archivo .dwg de origen (ni vinculado ni
    /// localizable): agrupa los trazos ya explotados (curvas de la propia
    /// geometría de Revit, en sus coordenadas reales y correctas) en
    /// "manchas de tinta" cercanas entre sí — el patrón típico de los
    /// caracteres de un texto — las dibuja en una imagen pequeña y les aplica
    /// reconocimiento óptico (Tesseract, embebido, sin servicios externos)
    /// para recuperar la cadena de texto. La posición y el tamaño del
    /// TextNote resultante salen directamente de la geometría real (no de
    /// unidades ni escalas del DWG), así que no puede desalinearse como el
    /// método basado en releer el archivo.
    ///
    /// Es un método aproximado: puede fallar con texto muy pequeño, fuentes
    /// poco comunes, o cuando otras curvas cercanas (tramas, símbolos) se
    /// confunden con el trazo de un carácter. Por eso sólo se usa como
    /// respaldo cuando no se pudo leer el .dwg real.
    /// </summary>
    internal static class DwgTextOcrRecognizer
    {
        private const double ClusterGapFeet = 0.035; // ~10-11 mm: separa caracteres/palabras de otra geometría
        private const double MinClusterHeightFeet = 0.02;  // ~6 mm
        private const double MaxClusterDimensionFeet = 3.0; // descarta símbolos/tramas grandes
        private const int MaxCurvesPerCluster = 300;
        private const float MinConfidence = 0.35f;

        private static TesseractEngine _engine;

        internal static List<DwgTextImporter.DwgTextEntry> Recognize(
            List<(Curve Curve, ElementId StyleId)> segments,
            HashSet<int> consumedIndices)
        {
            var results = new List<DwgTextImporter.DwgTextEntry>();
            if (segments.Count == 0)
            {
                return results;
            }

            var strokes = new List<(IList<XYZ> Points, double MinX, double MinY, double MaxX, double MaxY, double Z)>();
            for (int i = 0; i < segments.Count; i++)
            {
                Curve curve = segments[i].Curve;
                IList<XYZ> pts;
                try
                {
                    pts = curve.Tessellate();
                }
                catch (Exception)
                {
                    strokes.Add((null, 0, 0, 0, 0, 0));
                    continue;
                }

                if (pts == null || pts.Count < 2)
                {
                    strokes.Add((null, 0, 0, 0, 0, 0));
                    continue;
                }

                double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                double z = pts.Average(p => p.Z);
                strokes.Add((pts, minX, minY, maxX, maxY, z));
            }

            List<List<int>> clusters = ClusterByProximity(strokes, ClusterGapFeet);

            TesseractEngine engine = GetEngine();
            if (engine == null)
            {
                return results;
            }

            foreach (List<int> cluster in clusters)
            {
                if (cluster.Count == 0 || cluster.Count > MaxCurvesPerCluster)
                {
                    continue;
                }

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                double zSum = 0;
                int zCount = 0;

                foreach (int idx in cluster)
                {
                    var s = strokes[idx];
                    if (s.Points == null) continue;
                    minX = Math.Min(minX, s.MinX);
                    minY = Math.Min(minY, s.MinY);
                    maxX = Math.Max(maxX, s.MaxX);
                    maxY = Math.Max(maxY, s.MaxY);
                    zSum += s.Z;
                    zCount++;
                }

                if (zCount == 0)
                {
                    continue;
                }

                double width = maxX - minX;
                double height = maxY - minY;
                double avgZ = zSum / zCount;

                if (Math.Max(width, height) > MaxClusterDimensionFeet)
                {
                    continue;
                }
                if (Math.Min(width, height) < MinClusterHeightFeet)
                {
                    continue;
                }

                bool vertical = height > width * 1.3;

                using (Bitmap bitmap = Rasterize(strokes, cluster, minX, minY, maxX, maxY, vertical))
                {
                    if (bitmap == null)
                    {
                        continue;
                    }

                    string text;
                    float confidence;
                    if (!TryOcr(engine, bitmap, out text, out confidence))
                    {
                        continue;
                    }

                    if (confidence < MinConfidence)
                    {
                        continue;
                    }

                    text = CleanText(text);
                    if (string.IsNullOrEmpty(text) || !Regex.IsMatch(text, "[A-Za-z0-9]"))
                    {
                        continue;
                    }

                    double textHeightFeet = vertical ? width : height;

                    results.Add(new DwgTextImporter.DwgTextEntry
                    {
                        Text = text,
                        Position = new XYZ(minX, minY, avgZ),
                        HeightFeet = textHeightFeet,
                        RotationRadians = vertical ? Math.PI / 2.0 : 0.0,
                    });

                    foreach (int idx in cluster)
                    {
                        consumedIndices.Add(idx);
                    }
                }
            }

            return results;
        }

        private static List<List<int>> ClusterByProximity(
            List<(IList<XYZ> Points, double MinX, double MinY, double MaxX, double MaxY, double Z)> strokes,
            double gap)
        {
            int n = strokes.Count;
            int[] parent = Enumerable.Range(0, n).ToArray();

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[ra] = rb;
            }

            // Bucketiza por celda de rejilla (tamaño = gap) para no comparar
            // cada curva contra todas las demás en dibujos con miles de líneas.
            var cellBuckets = new Dictionary<(long, long), List<int>>();
            for (int i = 0; i < n; i++)
            {
                var s = strokes[i];
                if (s.Points == null) continue;

                long cxMin = (long)Math.Floor((s.MinX - gap) / gap);
                long cxMax = (long)Math.Floor((s.MaxX + gap) / gap);
                long cyMin = (long)Math.Floor((s.MinY - gap) / gap);
                long cyMax = (long)Math.Floor((s.MaxY + gap) / gap);

                for (long cx = cxMin; cx <= cxMax; cx++)
                {
                    for (long cy = cyMin; cy <= cyMax; cy++)
                    {
                        var key = (cx, cy);
                        if (!cellBuckets.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>();
                            cellBuckets[key] = list;
                        }
                        foreach (int other in list)
                        {
                            Union(i, other);
                        }
                        list.Add(i);
                    }
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (strokes[i].Points == null) continue;
                int root = Find(i);
                if (!groups.TryGetValue(root, out List<int> list))
                {
                    list = new List<int>();
                    groups[root] = list;
                }
                list.Add(i);
            }

            return groups.Values.ToList();
        }

        private static Bitmap Rasterize(
            List<(IList<XYZ> Points, double MinX, double MinY, double MaxX, double MaxY, double Z)> strokes,
            List<int> cluster,
            double minX, double minY, double maxX, double maxY,
            bool vertical)
        {
            double width = Math.Max(maxX - minX, 1e-6);
            double height = Math.Max(maxY - minY, 1e-6);

            const int targetShort = 48;
            const int padding = 8;

            int pxWidth, pxHeight;
            if (!vertical)
            {
                pxHeight = targetShort;
                pxWidth = (int)Math.Round(targetShort * (width / height));
            }
            else
            {
                pxWidth = targetShort;
                pxHeight = (int)Math.Round(targetShort * (height / width));
            }
            pxWidth = Math.Max(16, Math.Min(800, pxWidth));
            pxHeight = Math.Max(16, Math.Min(800, pxHeight));

            var bitmap = new Bitmap(pxWidth + padding * 2, pxHeight + padding * 2);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(DrawingColor.White);
                using (Pen pen = new Pen(DrawingColor.Black, 2f))
                {
                    foreach (int idx in cluster)
                    {
                        IList<XYZ> pts = strokes[idx].Points;
                        if (pts == null || pts.Count < 2) continue;

                        var pixelPts = new PointF[pts.Count];
                        for (int i = 0; i < pts.Count; i++)
                        {
                            double u = (pts[i].X - minX) / width;
                            double v = (pts[i].Y - minY) / height;

                            double px, py;
                            if (!vertical)
                            {
                                px = u * pxWidth;
                                py = (1.0 - v) * pxHeight;
                            }
                            else
                            {
                                // Gira 90° para que el texto vertical quede horizontal al leerlo.
                                px = v * pxWidth;
                                py = u * pxHeight;
                            }

                            pixelPts[i] = new PointF((float)px + padding, (float)py + padding);
                        }

                        if (pixelPts.Length == 2)
                        {
                            g.DrawLine(pen, pixelPts[0], pixelPts[1]);
                        }
                        else
                        {
                            g.DrawLines(pen, pixelPts);
                        }
                    }
                }
            }

            return bitmap;
        }

        private static bool TryOcr(TesseractEngine engine, Bitmap bitmap, out string text, out float confidence)
        {
            text = null;
            confidence = 0f;

            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;

                try
                {
                    using (Pix pix = Pix.LoadFromMemory(stream.ToArray()))
                    using (Page page = engine.Process(pix, PageSegMode.SingleLine))
                    {
                        text = page.GetText();
                        confidence = page.GetMeanConfidence();
                        return true;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        private static string CleanText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static TesseractEngine GetEngine()
        {
            if (_engine != null)
            {
                return _engine;
            }

            try
            {
                string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string tessDataPath = Path.Combine(asmDir ?? string.Empty, "tessdata");
                if (!Directory.Exists(tessDataPath))
                {
                    return null;
                }

                _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                _engine.DefaultPageSegMode = PageSegMode.SingleLine;
                return _engine;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
