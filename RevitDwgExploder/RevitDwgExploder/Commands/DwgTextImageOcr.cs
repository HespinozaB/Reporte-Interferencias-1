using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Tesseract;
using DrawingImage = System.Drawing.Image;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Alternativa a <see cref="DwgTextImporter"/> que tampoco depende del
    /// archivo .dwg de origen. En vez de reconstruir cada carácter a partir
    /// de las curvas explotadas (frágil: son trazos delgados de 1-2 px que
    /// Tesseract reconoce mal), exporta la vista ya recortada al DWG como
    /// imagen — Revit la renderiza igual que se ve en pantalla, con el texto
    /// relleno y nítido — y corre OCR una sola vez sobre esa imagen completa.
    /// Es más rápido (una sola exportación + una sola pasada de OCR por DWG,
    /// en vez de miles de intentos por curva) y más confiable, porque el
    /// reconocimiento se hace sobre texto renderizado normal, que es
    /// exactamente el tipo de imagen con el que Tesseract funciona bien.
    /// </summary>
    internal static class DwgTextImageOcr
    {
        private const int ExportPixelSize = 3000;
        private const float MinConfidencePercent = 55f;

        private static TesseractEngine _engine;

        /// <summary>
        /// Ajusta temporalmente el recorte de la vista al bounding box del
        /// DWG, exporta una imagen y la ocr-ea. Debe llamarse SIN ninguna
        /// transacción abierta: abre y cierra las suyas propias (una para
        /// fijar el recorte, otra para restaurarlo) y exporta la imagen entre
        /// medias, para no depender de si Document.ExportImage admite
        /// ejecutarse dentro de una transacción ajena.
        /// </summary>
        public static List<DwgTextImporter.DwgTextEntry> Recognize(Document doc, View view, ImportInstance importInstance)
        {
            var results = new List<DwgTextImporter.DwgTextEntry>();

            BoundingBoxXYZ bbox = importInstance.get_BoundingBox(view);
            if (bbox == null)
            {
                return results;
            }

            double minX = Math.Min(bbox.Min.X, bbox.Max.X);
            double maxX = Math.Max(bbox.Min.X, bbox.Max.X);
            double minY = Math.Min(bbox.Min.Y, bbox.Max.Y);
            double maxY = Math.Max(bbox.Min.Y, bbox.Max.Y);
            double avgZ = (bbox.Min.Z + bbox.Max.Z) / 2.0;

            double width = maxX - minX;
            double height = maxY - minY;
            if (width < 1e-6 || height < 1e-6)
            {
                return results;
            }

            // Pequeño margen para no recortar el texto que quede justo en el borde.
            double marginX = width * 0.03;
            double marginY = height * 0.03;
            minX -= marginX; maxX += marginX;
            minY -= marginY; maxY += marginY;
            width = maxX - minX;
            height = maxY - minY;

            bool hadCropActive = view.CropBoxActive;
            bool hadCropVisible = view.CropBoxVisible;
            BoundingBoxXYZ originalCropBox = view.CropBox;

            string tempDir = Path.Combine(Path.GetTempPath(), "RevitDwgExploder_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string baseFileName = "ocr";
            string imagePath = null;

            try
            {
                using (Transaction cropTx = new Transaction(doc, "RevitDwgExploder: ajustar recorte temporal"))
                {
                    cropTx.Start();

                    BoundingBoxXYZ newCropBox = new BoundingBoxXYZ
                    {
                        Transform = originalCropBox.Transform,
                        Min = new XYZ(minX, minY, originalCropBox.Min.Z),
                        Max = new XYZ(maxX, maxY, originalCropBox.Max.Z),
                    };

                    view.CropBoxActive = true;
                    view.CropBoxVisible = false;
                    view.CropBox = newCropBox;

                    cropTx.Commit();
                }

                // Exportar SIN transacción abierta.
                ImageExportOptions options = new ImageExportOptions
                {
                    FilePath = Path.Combine(tempDir, baseFileName),
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = ExportPixelSize,
                    ImageResolution = ImageResolution.DPI_300,
                    FitDirection = FitDirectionType.Horizontal,
                    ExportRange = ExportRange.CurrentView,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                };

                doc.ExportImage(options);

                // Revit suele añadir un sufijo (p. ej. "- NombreVista") al
                // nombre de archivo pedido; se busca lo que realmente generó.
                imagePath = Directory.GetFiles(tempDir, baseFileName + "*")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();

                using (Transaction restoreTx = new Transaction(doc, "RevitDwgExploder: restaurar recorte"))
                {
                    restoreTx.Start();
                    view.CropBox = originalCropBox;
                    view.CropBoxActive = hadCropActive;
                    view.CropBoxVisible = hadCropVisible;
                    restoreTx.Commit();
                }

                if (imagePath == null || !File.Exists(imagePath))
                {
                    return results;
                }

                int pixelWidth, pixelHeight;
                using (DrawingImage img = DrawingImage.FromFile(imagePath))
                {
                    pixelWidth = img.Width;
                    pixelHeight = img.Height;
                }

                double feetPerPixel = width / pixelWidth;
                // Si la imagen quedó con letterbox (aspecto no coincide exacto),
                // se usa el eje más ajustado como referencia de escala real.
                double feetPerPixelY = height / pixelHeight;
                if (Math.Abs(feetPerPixelY - feetPerPixel) / feetPerPixel < 0.05)
                {
                    feetPerPixel = (feetPerPixel + feetPerPixelY) / 2.0;
                }

                TesseractEngine engine = GetEngine();
                if (engine == null)
                {
                    return results;
                }

                using (Pix pix = Pix.LoadFromFile(imagePath))
                using (Page page = engine.Process(pix, PageSegMode.SparseText))
                using (ResultIterator iter = page.GetIterator())
                {
                    iter.Begin();
                    do
                    {
                        if (!iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out Rect rect))
                        {
                            continue;
                        }

                        string text = iter.GetText(PageIteratorLevel.TextLine);
                        float confidence = iter.GetConfidence(PageIteratorLevel.TextLine);

                        text = CleanText(text);
                        if (string.IsNullOrEmpty(text) || !Regex.IsMatch(text, "[A-Za-z0-9]"))
                        {
                            continue;
                        }
                        if (confidence < MinConfidencePercent)
                        {
                            continue;
                        }

                        double modelX = minX + rect.X1 * feetPerPixel;
                        double modelYTop = maxY - rect.Y1 * feetPerPixel;
                        double modelYBottom = maxY - rect.Y2 * feetPerPixel;
                        double textHeight = Math.Abs(modelYTop - modelYBottom);
                        if (textHeight < 1e-4)
                        {
                            continue;
                        }

                        results.Add(new DwgTextImporter.DwgTextEntry
                        {
                            Text = text,
                            Position = new XYZ(modelX, Math.Min(modelYTop, modelYBottom), avgZ),
                            HeightFeet = textHeight,
                            RotationRadians = 0.0,
                        });
                    }
                    while (iter.Next(PageIteratorLevel.TextLine));
                }
            }
            catch (Exception)
            {
                // Cualquier fallo en la exportación/OCR de este DWG no debe
                // abortar el resto del comando; simplemente se queda sin texto.
                try
                {
                    // Se reintenta el restaurado incondicionalmente (aunque ya se
                    // haya hecho antes de llegar aquí): es idempotente y más
                    // seguro que confiar en comparar BoundingBoxXYZ por igualdad.
                    using (Transaction fixTx = new Transaction(doc, "RevitDwgExploder: restaurar recorte"))
                    {
                        fixTx.Start();
                        view.CropBox = originalCropBox;
                        view.CropBoxActive = hadCropActive;
                        view.CropBoxVisible = hadCropVisible;
                        fixTx.Commit();
                    }
                }
                catch (Exception)
                {
                    // Ya no hay mucho más que hacer si ni siquiera esto funciona.
                }
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
                    // Limpieza best-effort; un archivo temporal huérfano no es grave.
                }
            }

            return results;
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
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string tessDataPath = Path.Combine(asmDir ?? string.Empty, "tessdata");
                if (!Directory.Exists(tessDataPath))
                {
                    return null;
                }

                _engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                return _engine;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
