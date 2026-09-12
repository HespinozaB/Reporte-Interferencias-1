using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using DialogResult = System.Windows.Forms.DialogResult;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Revit no expone ningún método de API para "explotar" una instancia CAD
    /// (ni Document.Explode ni un PostableCommand equivalente existen para
    /// ImportInstance; esa operación sólo está disponible desde el menú
    /// Modificar → Explotar). Este comando logra el mismo resultado visual de
    /// forma soportada por la API: recorre la geometría real del DWG
    /// (curvas, polilíneas y aristas de sólidos/mallas) y crea Detail Lines
    /// nativas en la vista activa, en las mismas coordenadas y con el mismo
    /// Line Style (capa) que ya trae el DWG — sin reescalar ni desplazar nada.
    ///
    /// Limitación conocida de la API: el texto del DWG no se expone como una
    /// cadena editable (no existe una clase "Text" entre los GeometryObject
    /// de Revit), así que el texto no se puede recrear como TextNote; cuando
    /// el DWG dibuja el texto con líneas (fuentes SHX) su contorno sí queda
    /// representado por las Detail Lines resultantes.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExplodeDwgCommand : IExternalCommand
    {
        // Tope de cordura para el "Tamaño de texto" (medida de papel, en pies):
        // sólo recorta valores absurdos, para no alterar títulos legítimamente
        // grandes. Revit rechaza tamaños por debajo de ~1/64".
        private const double MinTextSizeFeet = 1.0 / 64.0 / 12.0;
        private const double MaxTextSizeFeet = 1.0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            View activeView = uiDoc.ActiveView;

            List<ImportInstance> targets = GetSelectedImportInstances(uiDoc);
            if (targets.Count == 0)
            {
                targets = GetImportInstancesInView(doc, activeView);
            }

            if (targets.Count == 0)
            {
                TaskDialog.Show(
                    "Explotar DWGs",
                    "No se encontraron instancias de DWG importado/vinculado en la selección " +
                    "actual ni en la vista activa.");
                return Result.Cancelled;
            }

            ExplodeOptions options;
            using (var optionsDialog = new ExplodeOptionsDialog(targets.Count))
            {
                if (optionsDialog.ShowDialog() != DialogResult.OK)
                {
                    return Result.Cancelled;
                }
                options = optionsDialog.Options;
            }

            // Revit exige que ninguna curva sea más corta que esta tolerancia
            // (normalmente ~1/32"); por debajo de eso NewDetailCurve lanza
            // "Curve length is too small for Revit's tolerance".
            double minLength = commandData.Application.Application.ShortCurveTolerance * 1.01;

            // Un DWG "Importar" (embebido) no conserva la ruta al archivo de
            // origen, así que si el usuario quiere su texto hay que pedirle
            // que localice el .dwg manualmente (fuera de la transacción).
            Dictionary<ElementId, string> manualDwgPaths = options.RecreateText
                ? AskForManualDwgPaths(doc, targets)
                : new Dictionary<ElementId, string>();

            int notLinkedCount = 0;
            int textReadErrors = 0;

            // El texto se resuelve ANTES de abrir la transacción principal:
            // el respaldo por OCR exporta una imagen de la vista (ajustando su
            // recorte temporalmente) y eso no debe hacerse con una transacción
            // ajena abierta. DwgTextImageOcr administra sus propias
            // transacciones cortas (fijar recorte / restaurarlo).
            var textsByImport = new Dictionary<ElementId, (List<DwgTextImporter.DwgTextEntry> Texts, bool FromOcr)>();
            foreach (ImportInstance importInstance in targets)
            {
                if (!options.RecreateText)
                {
                    textsByImport[importInstance.Id] = (new List<DwgTextImporter.DwgTextEntry>(), false);
                    continue;
                }

                manualDwgPaths.TryGetValue(importInstance.Id, out string manualPath);
                DwgTextImporter.ReadStatus textStatus = DwgTextImporter.TryReadTexts(
                    doc, importInstance, out List<DwgTextImporter.DwgTextEntry> dwgTexts, manualPath);

                switch (textStatus)
                {
                    case DwgTextImporter.ReadStatus.NotLinked:
                        notLinkedCount++;
                        break;
                    case DwgTextImporter.ReadStatus.FileNotFound:
                    case DwgTextImporter.ReadStatus.ReadError:
                        textReadErrors++;
                        break;
                }

                if (textStatus == DwgTextImporter.ReadStatus.Ok && dwgTexts.Count > 0)
                {
                    textsByImport[importInstance.Id] = (dwgTexts, false);
                    continue;
                }

                // Sin el .dwg de origen: Revit sí sabe internamente que esas
                // entidades son texto (su comando "Consulta" lo muestra), y ese
                // dato sobrevive al exportar la vista de vuelta a DWG. Esa vía
                // da texto exacto, así que se intenta antes que el OCR.
                List<DwgTextImporter.DwgTextEntry> roundTripTexts =
                    DwgRoundTripTextExtractor.Extract(doc, activeView, importInstance);

                if (roundTripTexts.Count > 0)
                {
                    textsByImport[importInstance.Id] = (roundTripTexts, false);
                    continue;
                }

                List<DwgTextImporter.DwgTextEntry> ocrTexts =
                    DwgTextImageOcr.Recognize(doc, activeView, importInstance);
                textsByImport[importInstance.Id] = (ocrTexts, true);
            }

            int linesCreated = 0;
            int curvesSkipped = 0;
            int importsProcessed = 0;
            int textsFromDwgCreated = 0;
            int textsFromOcrCreated = 0;
            int hatchesCreated = 0;
            int linesSkippedAsText = 0;
            Dictionary<int, ElementId> textTypesByHeight = new Dictionary<int, ElementId>();
            ElementId filledRegionTypeId = options.ConvertHatches
                ? GetFilledRegionTypeId(doc)
                : ElementId.InvalidElementId;

            using (Transaction t = new Transaction(doc, "Explotar DWGs a Detail Lines"))
            {
                t.Start();

                foreach (ImportInstance importInstance in targets)
                {
                    var segments = new List<(Curve Curve, ElementId StyleId)>();
                    var hatchSolids = new List<Solid>();
                    Options geomOptions = new Options
                    {
                        View = activeView,
                        ComputeReferences = false,
                        IncludeNonVisibleObjects = false,
                    };

                    GeometryElement geometry = importInstance.get_Geometry(geomOptions);
                    if (geometry != null)
                    {
                        CollectCurves(geometry, segments, minLength, options, hatchSolids);
                    }

                    (List<DwgTextImporter.DwgTextEntry> textsToCreate, bool fromOcr) =
                        textsByImport[importInstance.Id];

                    // Capas cuyo texto ya se recreó: sus líneas no se vuelven a
                    // dibujar, para que el texto no quede duplicado debajo.
                    var textLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (options.SkipRecreatedTextLines)
                    {
                        foreach (DwgTextImporter.DwgTextEntry entry in textsToCreate)
                        {
                            if (!string.IsNullOrWhiteSpace(entry.Layer))
                            {
                                textLayers.Add(entry.Layer);
                            }
                        }
                    }

                    foreach (DwgTextImporter.DwgTextEntry entry in textsToCreate)
                    {
                        ElementId typeId = GetOrCreateTextNoteType(
                            doc, entry.HeightFeet, activeView.Scale, textTypesByHeight);
                        TextNote note = TextNote.Create(doc, activeView.Id, entry.Position, entry.Text, typeId);

                        try
                        {
                            // En el DWG el punto de inserción del texto es su
                            // esquina inferior izquierda (línea base), no la superior.
                            note.HorizontalAlignment = HorizontalTextAlignment.Left;
                            note.VerticalAlignment = VerticalTextAlignment.Bottom;
                        }
                        catch (Exception)
                        {
                            // La alineación es un ajuste fino; si el tipo no la admite
                            // se deja la que traiga por defecto.
                        }

                        if (Math.Abs(entry.RotationRadians) > 1e-9)
                        {
                            Line axis = Line.CreateBound(entry.Position, entry.Position + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, note.Id, axis, entry.RotationRadians);
                        }

                        if (fromOcr) textsFromOcrCreated++;
                        else textsFromDwgCreated++;
                    }

                    if (options.ConvertHatches && filledRegionTypeId != ElementId.InvalidElementId)
                    {
                        hatchesCreated += CreateFilledRegions(
                            doc, activeView, hatchSolids, filledRegionTypeId, minLength);
                    }

                    foreach (var segment in segments)
                    {
                        Curve curve = segment.Curve;
                        if (curve == null || !IsUsableCurve(curve, minLength))
                        {
                            curvesSkipped++;
                            continue;
                        }

                        GraphicsStyle style = segment.StyleId != ElementId.InvalidElementId
                            ? doc.GetElement(segment.StyleId) as GraphicsStyle
                            : null;

                        if (textLayers.Count > 0 && IsInTextLayer(style, textLayers))
                        {
                            linesSkippedAsText++;
                            continue;
                        }

                        try
                        {
                            DetailCurve detailCurve = doc.Create.NewDetailCurve(activeView, curve);

                            if (style != null)
                            {
                                try
                                {
                                    detailCurve.LineStyle = style;
                                }
                                catch (Autodesk.Revit.Exceptions.ArgumentException)
                                {
                                    // El estilo de línea del DWG no es compatible como
                                    // Line Style (raro, pero no debe abortar el resto).
                                }
                            }

                            linesCreated++;
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException)
                        {
                            curvesSkipped++;
                        }
                    }

                    importsProcessed++;
                }

                t.Commit();
            }

            string textNote = notLinkedCount > 0
                ? $"\n{notLinkedCount} DWG estaban importados (no vinculados) o sin archivo " +
                  "indicado: para esos el texto se obtuvo reexportando la vista a DWG, o por " +
                  "OCR si eso tampoco dio resultado."
                : string.Empty;

            string errorNote = textReadErrors > 0
                ? $"\n{textReadErrors} DWG no se pudieron releer desde su archivo original " +
                  "(movido/no encontrado, o formato no soportado)."
                : string.Empty;

            string ocrWarning = textsFromOcrCreated > 0
                ? "\n\nOjo: hubo textos creados por OCR (reconocimiento aproximado). " +
                  "Conviene verificarlos."
                : string.Empty;

            string hatchLine = options.ConvertHatches
                ? $"\nRegiones rellenas (hatch) creadas: {hatchesCreated}"
                : string.Empty;

            string skippedTextLine = linesSkippedAsText > 0
                ? $"\nLíneas omitidas por ser el texto ya recreado: {linesSkippedAsText}"
                : string.Empty;

            TaskDialog.Show(
                "EMASY — Explotar DWG: resumen",
                $"DWGs procesados: {importsProcessed}\n" +
                $"Detail Lines creadas: {linesCreated}{hatchLine}{skippedTextLine}\n" +
                $"Segmentos omitidos: {curvesSkipped}\n" +
                $"TextNotes con texto exacto: {textsFromDwgCreated}\n" +
                $"TextNotes por OCR (aproximados): {textsFromOcrCreated}" +
                $"{textNote}{errorNote}\n\n" +
                "Los DWG originales no se modificaron ni se eliminaron." +
                ocrWarning);

            return Result.Succeeded;
        }

        private static Dictionary<ElementId, string> AskForManualDwgPaths(Document doc, List<ImportInstance> targets)
        {
            var result = new Dictionary<ElementId, string>();

            List<ImportInstance> notLinked = targets
                .Where(i => !HasResolvableLink(doc, i))
                .ToList();

            if (notLinked.Count == 0)
            {
                return result;
            }

            TaskDialog dialog = new TaskDialog("Explotar DWGs")
            {
                MainInstruction = "Algunos DWG están importados (no vinculados)",
                MainContent =
                    $"{notLinked.Count} de {targets.Count} instancia(s) de CAD están " +
                    "importadas (embebidas), no vinculadas. Revit no conserva la ruta al " +
                    "archivo original para esos casos.\n\n" +
                    "Si todavía tienes el/los archivo(s) .dwg originales, puedes localizarlos " +
                    "ahora para recuperar el texto exacto. Si no los tienes (o prefieres " +
                    "saltarlo), el addin igual intentará reconocer el texto por OCR " +
                    "directamente sobre la geometría — es aproximado, pero no necesita el " +
                    "archivo original.",
                CommonButtons = TaskDialogCommonButtons.None,
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Buscar los archivos .dwg originales (texto exacto)");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Continuar sin buscarlos (usar OCR aproximado)");

            TaskDialogResult choice = dialog.Show();
            if (choice != TaskDialogResult.CommandLink1)
            {
                return result;
            }

            foreach (ImportInstance importInstance in notLinked)
            {
                using (var openDialog = new OpenFileDialog
                {
                    Title = $"Selecciona el DWG original para el elemento Id {importInstance.Id.Value}",
                    Filter = "Archivos DWG (*.dwg)|*.dwg|Todos los archivos (*.*)|*.*",
                    CheckFileExists = true,
                })
                {
                    if (openDialog.ShowDialog() == DialogResult.OK)
                    {
                        result[importInstance.Id] = openDialog.FileName;
                    }
                }
            }

            return result;
        }

        private static bool HasResolvableLink(Document doc, ImportInstance importInstance)
        {
            if (!importInstance.IsLinked)
            {
                return false;
            }

            Element typeElem = doc.GetElement(importInstance.GetTypeId());
            return typeElem?.GetExternalFileReference() != null;
        }

        /// <param name="modelHeightFeet">Altura que ocupa el texto en el modelo,
        /// tal como viene del DWG.</param>
        /// <param name="viewScale">Denominador de la escala de la vista (100 para
        /// 1:100). El parámetro "Tamaño de texto" de Revit es una medida DE PAPEL:
        /// Revit lo multiplica por la escala al dibujarlo, así que hay que dividir
        /// la altura de modelo entre la escala para que el texto salga del mismo
        /// tamaño que tenía en el DWG.</param>
        private static ElementId GetOrCreateTextNoteType(
            Document doc, double modelHeightFeet, int viewScale, Dictionary<int, ElementId> cache)
        {
            double paperHeightFeet = modelHeightFeet / Math.Max(viewScale, 1);

            // Revit rechaza tamaños de texto fuera de su rango admitido.
            paperHeightFeet = Math.Max(MinTextSizeFeet, Math.Min(MaxTextSizeFeet, paperHeightFeet));

            double paperHeightMm = paperHeightFeet * 304.8;

            // Se agrupan alturas casi iguales (a 0.1 mm) para no crear un
            // TextNoteType nuevo por cada mínima diferencia de precisión.
            int key = (int)Math.Round(paperHeightMm * 10.0);
            if (cache.TryGetValue(key, out ElementId cachedId))
            {
                return cachedId;
            }

            string typeName = $"DWG {paperHeightMm:0.##} mm";

            // El nombre puede ya existir de una corrida anterior del addin en
            // este mismo documento (Duplicate lanza si el nombre está repetido).
            TextNoteType existing = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault(tnt => tnt.Name == typeName);

            if (existing != null)
            {
                cache[key] = existing.Id;
                return existing.Id;
            }

            ElementId baseTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            TextNoteType baseType = doc.GetElement(baseTypeId) as TextNoteType;

            TextNoteType newType;
            try
            {
                newType = baseType?.Duplicate(typeName) as TextNoteType;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Otro tipo con ese nombre se coló entre la búsqueda y el duplicado
                // (o el nombre choca por alguna otra razón): usar el tipo base tal cual.
                cache[key] = baseTypeId;
                return baseTypeId;
            }

            if (newType == null)
            {
                cache[key] = baseTypeId;
                return baseTypeId;
            }

            try
            {
                newType.get_Parameter(BuiltInParameter.TEXT_SIZE)?.Set(paperHeightFeet);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Tamaño fuera del rango admitido por Revit: el tipo se queda
                // con el del tipo base en vez de abortar todo el comando.
            }

            cache[key] = newType.Id;
            return newType.Id;
        }

        private static void CollectCurves(
            GeometryElement geometry,
            List<(Curve, ElementId)> output,
            double minLength,
            ExplodeOptions options,
            List<Solid> hatchSolids)
        {
            foreach (GeometryObject geomObj in geometry)
            {
                CollectFromGeometryObject(geomObj, output, minLength, options, hatchSolids);
            }
        }

        private static void CollectFromGeometryObject(
            GeometryObject geomObj,
            List<(Curve, ElementId)> output,
            double minLength,
            ExplodeOptions options,
            List<Solid> hatchSolids)
        {
            ElementId styleId = geomObj.GraphicsStyleId;

            switch (geomObj)
            {
                case GeometryInstance instance:
                    // Geometría anidada (p. ej. bloques/inserts del DWG): se toma ya
                    // transformada a coordenadas del proyecto.
                    GeometryElement nested = instance.GetInstanceGeometry();
                    if (nested != null)
                    {
                        CollectCurves(nested, output, minLength, options, hatchSolids);
                    }
                    break;

                case Curve curve:
                    // Arcos, elipses y splines del DWG pasan tal cual: no se
                    // trocean en segmentos rectos.
                    output.Add((curve, styleId));
                    break;

                case PolyLine polyLine:
                    IList<XYZ> pts = polyLine.GetCoordinates();
                    if (options.SimplifyGeometry)
                    {
                        foreach (Curve simplified in DwgGeometrySimplifier.Simplify(pts, minLength, minLength))
                        {
                            output.Add((simplified, styleId));
                        }
                    }
                    else
                    {
                        for (int i = 0; i < pts.Count - 1; i++)
                        {
                            if (pts[i].DistanceTo(pts[i + 1]) > minLength)
                            {
                                output.Add((Line.CreateBound(pts[i], pts[i + 1]), styleId));
                            }
                        }
                    }
                    break;

                case Solid solid:
                    if (options.ConvertHatches)
                    {
                        // Se reserva para crear una región rellena en vez de
                        // dibujar sólo su contorno como líneas.
                        hatchSolids.Add(solid);
                        break;
                    }

                    foreach (Edge edge in solid.Edges)
                    {
                        output.Add((edge.AsCurve(), styleId));
                    }
                    break;

                case Mesh mesh:
                    // Sólo el contorno: las aristas interiores de la malla no son
                    // parte del dibujo y multiplican la cantidad de líneas.
                    List<Curve> meshCurves = options.SimplifyGeometry
                        ? DwgGeometrySimplifier.MeshBoundary(mesh, minLength)
                        : AllMeshEdges(mesh, minLength);

                    foreach (Curve meshCurve in meshCurves)
                    {
                        output.Add((meshCurve, styleId));
                    }
                    break;
            }
        }

        private static List<Curve> AllMeshEdges(Mesh mesh, double minLength)
        {
            var result = new List<Curve>();
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle tri = mesh.get_Triangle(i);
                AddMeshEdge(tri.get_Vertex(0), tri.get_Vertex(1), result, minLength);
                AddMeshEdge(tri.get_Vertex(1), tri.get_Vertex(2), result, minLength);
                AddMeshEdge(tri.get_Vertex(2), tri.get_Vertex(0), result, minLength);
            }
            return result;
        }

        private static void AddMeshEdge(XYZ a, XYZ b, List<Curve> output, double minLength)
        {
            if (a.DistanceTo(b) > minLength)
            {
                output.Add(Line.CreateBound(a, b));
            }
        }

        private static bool IsInTextLayer(GraphicsStyle style, HashSet<string> textLayers)
        {
            string layerName = style?.GraphicsStyleCategory?.Name;
            return !string.IsNullOrEmpty(layerName) && textLayers.Contains(layerName);
        }

        private static ElementId GetFilledRegionTypeId(Document doc)
        {
            FilledRegionType type = new FilteredElementCollector(doc)
                .OfClass(typeof(FilledRegionType))
                .Cast<FilledRegionType>()
                .FirstOrDefault();

            return type?.Id ?? ElementId.InvalidElementId;
        }

        /// <summary>
        /// Crea una región rellena por cada cara horizontal de los sólidos del
        /// DWG (que es como llegan los sombreados macizos). Las curvas se
        /// aplanan a una Z común porque la región debe ser plana en la vista.
        /// </summary>
        private static int CreateFilledRegions(
            Document doc, View view, List<Solid> solids, ElementId typeId, double minLength)
        {
            int created = 0;

            foreach (Solid solid in solids)
            {
                foreach (Face face in solid.Faces)
                {
                    if (!(face is PlanarFace planar) || Math.Abs(planar.FaceNormal.Z) < 0.9)
                    {
                        continue;
                    }

                    try
                    {
                        var loops = new List<CurveLoop>();
                        foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
                        {
                            CurveLoop flat = FlattenLoop(loop, planar.Origin.Z, minLength);
                            if (flat != null)
                            {
                                loops.Add(flat);
                            }
                        }

                        if (loops.Count == 0)
                        {
                            continue;
                        }

                        FilledRegion.Create(doc, typeId, view.Id, loops);
                        created++;
                    }
                    catch (Exception)
                    {
                        // Caras que Revit no acepta como contorno de región
                        // (abiertas, auto-intersecadas…): se omiten.
                    }
                }
            }

            return created;
        }

        private static CurveLoop FlattenLoop(CurveLoop loop, double z, double minLength)
        {
            var curves = new List<Curve>();

            foreach (Curve curve in loop)
            {
                XYZ start = curve.GetEndPoint(0);
                XYZ end = curve.GetEndPoint(1);
                XYZ flatStart = new XYZ(start.X, start.Y, z);
                XYZ flatEnd = new XYZ(end.X, end.Y, z);

                if (flatStart.DistanceTo(flatEnd) <= minLength)
                {
                    continue;
                }

                curves.Add(Line.CreateBound(flatStart, flatEnd));
            }

            if (curves.Count < 3)
            {
                return null;
            }

            try
            {
                return CurveLoop.Create(curves);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsUsableCurve(Curve curve, double minLength)
        {
            try
            {
                return (curve.IsBound || curve.IsCyclic) && curve.Length > minLength;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<ImportInstance> GetSelectedImportInstances(UIDocument uiDoc)
        {
            Document doc = uiDoc.Document;
            return uiDoc.Selection.GetElementIds()
                .Select(doc.GetElement)
                .OfType<ImportInstance>()
                .ToList();
        }

        private static List<ImportInstance> GetImportInstancesInView(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .Cast<ImportInstance>()
                .ToList();
        }
    }
}
