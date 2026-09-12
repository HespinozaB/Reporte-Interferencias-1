using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using Autodesk.Revit.DB;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Extrae el texto de un CAD importado SIN necesitar el archivo .dwg de
    /// origen y SIN reconocimiento óptico: Revit conserva internamente que esas
    /// entidades son texto (el comando "Consulta" del propio Revit muestra
    /// "Tipo: Texto" y la capa original), y ese dato sobrevive al exportar la
    /// vista de vuelta a DWG. Así que se exporta la vista a un DWG temporal,
    /// se lee con ACadSharp y se recogen sus entidades TEXT/MTEXT reales, con
    /// su cadena, posición, altura y rotación exactas.
    ///
    /// Es exacto (no adivina caracteres como el OCR) y rápido (una exportación
    /// y una lectura por vista, sin recorrer curva por curva).
    /// </summary>
    internal static class DwgRoundTripTextExtractor
    {
        private const int MaxBlockDepth = 3;

        /// <summary>
        /// Debe llamarse SIN ninguna transacción abierta: Document.Export no
        /// admite ejecutarse dentro de una.
        /// </summary>
        public static List<DwgTextImporter.DwgTextEntry> Extract(Document doc, View view, ImportInstance importInstance)
        {
            var results = new List<DwgTextImporter.DwgTextEntry>();

            BoundingBoxXYZ bbox = importInstance.get_BoundingBox(view);
            if (bbox == null)
            {
                return results;
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "RevitDwgExploder_" + Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(tempDir);

                CadDocument cadDoc = ExportAndRead(doc, view, tempDir, TextTreatment.Approximate);
                List<RawText> rawTexts = cadDoc != null ? CollectTexts(cadDoc) : new List<RawText>();

                if (rawTexts.Count == 0)
                {
                    // "Approximate" mantiene el texto como entidades de texto;
                    // si aun así no salió ninguna, se reintenta con "Exact".
                    cadDoc = ExportAndRead(doc, view, tempDir, TextTreatment.Exact);
                    rawTexts = cadDoc != null ? CollectTexts(cadDoc) : new List<RawText>();
                }

                if (rawTexts.Count == 0)
                {
                    return results;
                }

                double scale = ResolveScale(cadDoc, rawTexts, bbox);
                if (scale <= 0)
                {
                    return results;
                }

                double midZ = (bbox.Min.Z + bbox.Max.Z) / 2.0;
                List<XYZ> existingTextPositions = GetExistingTextNotePositions(doc, view);

                foreach (RawText raw in rawTexts)
                {
                    XYZ position = new XYZ(raw.X * scale, raw.Y * scale, midZ);
                    if (!IsInside(position, bbox))
                    {
                        continue;
                    }

                    // No duplicar anotaciones de Revit que ya existían en la vista
                    // (la exportación incluye todo lo visible, no sólo el CAD).
                    if (existingTextPositions.Any(p => p.DistanceTo(position) < 0.05))
                    {
                        continue;
                    }

                    double height = raw.Height * scale;
                    if (height < 1e-4)
                    {
                        continue;
                    }

                    results.Add(new DwgTextImporter.DwgTextEntry
                    {
                        Text = raw.Text,
                        Position = position,
                        HeightFeet = height,
                        RotationRadians = raw.Rotation,
                    });
                }
            }
            catch (Exception)
            {
                // Un fallo aquí no debe abortar el resto del comando: se
                // continúa sin texto por esta vía (queda el respaldo por OCR).
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception)
                {
                    // Limpieza best-effort.
                }
            }

            return results;
        }

        private static CadDocument ExportAndRead(Document doc, View view, string tempDir, TextTreatment textTreatment)
        {
            string subDir = Path.Combine(tempDir, textTreatment.ToString());
            Directory.CreateDirectory(subDir);

            DWGExportOptions options = new DWGExportOptions
            {
                MergedViews = true,
                // Pedir pies hace que las coordenadas exportadas coincidan con
                // las unidades internas de Revit (la escala se verifica luego).
                TargetUnit = ExportUnit.Foot,
                SharedCoords = false,
                TextTreatment = textTreatment,
                FileVersion = ACADVersion.R2013,
            };

            doc.Export(subDir, "roundtrip", new List<ElementId> { view.Id }, options);

            string dwgPath = Directory.GetFiles(subDir, "*.dwg")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (dwgPath == null)
            {
                return null;
            }

            try
            {
                using (DwgReader reader = new DwgReader(dwgPath))
                {
                    return reader.Read();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private class RawText
        {
            public string Text;
            public double X;
            public double Y;
            public double Height;
            public double Rotation;
        }

        private static List<RawText> CollectTexts(CadDocument cadDoc)
        {
            var texts = new List<RawText>();
            CollectFrom(cadDoc.Entities, texts, 0, 0, 0, 1.0, 0.0);
            return texts;
        }

        private static void CollectFrom(
            IEnumerable<Entity> entities,
            List<RawText> output,
            int depth,
            double offsetX,
            double offsetY,
            double scale,
            double rotation)
        {
            foreach (Entity entity in entities)
            {
                switch (entity)
                {
                    case TextEntity textEntity:
                        Add(output, textEntity.Value, textEntity.InsertPoint.X, textEntity.InsertPoint.Y,
                            textEntity.Height, textEntity.Rotation, offsetX, offsetY, scale, rotation);
                        break;

                    case MText mText:
                        Add(output, mText.PlainText, mText.InsertPoint.X, mText.InsertPoint.Y,
                            mText.Height, mText.Rotation, offsetX, offsetY, scale, rotation);
                        break;

                    case Insert insert when depth < MaxBlockDepth && insert.Block != null:
                        // Revit suele exportar geometría dentro de bloques; hay
                        // que entrar en ellos aplicando su inserción.
                        double insertScale = scale * Math.Abs(insert.XScale > 1e-9 ? insert.XScale : 1.0);
                        double insertRotation = rotation + insert.Rotation;
                        double cos = Math.Cos(rotation);
                        double sin = Math.Sin(rotation);
                        double ix = offsetX + (insert.InsertPoint.X * cos - insert.InsertPoint.Y * sin) * scale;
                        double iy = offsetY + (insert.InsertPoint.X * sin + insert.InsertPoint.Y * cos) * scale;

                        CollectFrom(insert.Block.Entities, output, depth + 1, ix, iy, insertScale, insertRotation);
                        break;
                }
            }
        }

        private static void Add(
            List<RawText> output,
            string value,
            double localX,
            double localY,
            double height,
            double localRotation,
            double offsetX,
            double offsetY,
            double scale,
            double rotation)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);

            output.Add(new RawText
            {
                Text = value.Replace("\r", " ").Replace("\n", " ").Trim(),
                X = offsetX + (localX * cos - localY * sin) * scale,
                Y = offsetY + (localX * sin + localY * cos) * scale,
                Height = height * scale,
                Rotation = rotation + localRotation,
            });
        }

        /// <summary>
        /// Determina cuántos pies vale una unidad del DWG exportado. Se parte
        /// de lo que declara el propio archivo y de que se pidió exportar en
        /// pies, pero se verifica contra el bounding box real del CAD en Revit:
        /// se elige el factor que sitúa más textos dentro de ese área.
        /// </summary>
        private static double ResolveScale(CadDocument cadDoc, List<RawText> texts, BoundingBoxXYZ bbox)
        {
            var candidates = new List<double> { 1.0, 1.0 / 12.0, 1.0 / 304.8, 1.0 / 30.48, 1.0 / 0.3048 };

            if (cadDoc?.Header != null)
            {
                double declared = DwgTextImporter.GetFeetPerDwgUnit(cadDoc.Header.InsUnits);
                if (declared > 0 && !candidates.Any(c => Math.Abs(c - declared) < 1e-9))
                {
                    candidates.Insert(0, declared);
                }
            }

            double bestScale = 0;
            int bestScore = 0;

            foreach (double candidate in candidates)
            {
                int score = texts.Count(t => IsInside(new XYZ(t.X * candidate, t.Y * candidate, 0), bbox));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestScale = candidate;
                }
            }

            return bestScore > 0 ? bestScale : 0;
        }

        /// <summary>Comprueba sólo X/Y: la vista es de planta y la Z del texto
        /// se fija aparte al plano del CAD.</summary>
        private static bool IsInside(XYZ point, BoundingBoxXYZ bbox)
        {
            double minX = Math.Min(bbox.Min.X, bbox.Max.X);
            double maxX = Math.Max(bbox.Min.X, bbox.Max.X);
            double minY = Math.Min(bbox.Min.Y, bbox.Max.Y);
            double maxY = Math.Max(bbox.Min.Y, bbox.Max.Y);

            double marginX = (maxX - minX) * 0.1;
            double marginY = (maxY - minY) * 0.1;

            return point.X >= minX - marginX && point.X <= maxX + marginX
                && point.Y >= minY - marginY && point.Y <= maxY + marginY;
        }

        private static List<XYZ> GetExistingTextNotePositions(Document doc, View view)
        {
            try
            {
                return new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .Cast<TextNote>()
                    .Select(t => t.Coord)
                    .Where(c => c != null)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<XYZ>();
            }
        }
    }
}
