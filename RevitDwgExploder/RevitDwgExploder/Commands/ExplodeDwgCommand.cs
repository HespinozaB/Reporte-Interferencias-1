using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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

            // Revit exige que ninguna curva sea más corta que esta tolerancia
            // (normalmente ~1/32"); por debajo de eso NewDetailCurve lanza
            // "Curve length is too small for Revit's tolerance".
            double minLength = commandData.Application.Application.ShortCurveTolerance * 1.01;

            int linesCreated = 0;
            int curvesSkipped = 0;
            int importsProcessed = 0;
            int textsCreated = 0;
            int notLinkedCount = 0;
            int textReadErrors = 0;
            Dictionary<int, ElementId> textTypesByHeight = new Dictionary<int, ElementId>();

            using (Transaction t = new Transaction(doc, "Explotar DWGs a Detail Lines"))
            {
                t.Start();

                foreach (ImportInstance importInstance in targets)
                {
                    DwgTextImporter.ReadStatus textStatus = DwgTextImporter.TryReadTexts(
                        doc, importInstance, out List<DwgTextImporter.DwgTextEntry> dwgTexts);

                    switch (textStatus)
                    {
                        case DwgTextImporter.ReadStatus.NotLinked:
                            notLinkedCount++;
                            break;
                        case DwgTextImporter.ReadStatus.FileNotFound:
                        case DwgTextImporter.ReadStatus.ReadError:
                            textReadErrors++;
                            break;
                        case DwgTextImporter.ReadStatus.Ok:
                            foreach (DwgTextImporter.DwgTextEntry entry in dwgTexts)
                            {
                                ElementId typeId = GetOrCreateTextNoteType(doc, entry.HeightFeet, textTypesByHeight);
                                TextNote note = TextNote.Create(doc, activeView.Id, entry.Position, entry.Text, typeId);

                                if (Math.Abs(entry.RotationRadians) > 1e-9)
                                {
                                    Line axis = Line.CreateBound(entry.Position, entry.Position + XYZ.BasisZ);
                                    ElementTransformUtils.RotateElement(doc, note.Id, axis, entry.RotationRadians);
                                }

                                textsCreated++;
                            }
                            break;
                    }

                    var segments = new List<(Curve Curve, ElementId StyleId)>();
                    Options options = new Options
                    {
                        View = activeView,
                        ComputeReferences = false,
                        IncludeNonVisibleObjects = false,
                    };

                    GeometryElement geometry = importInstance.get_Geometry(options);
                    if (geometry != null)
                    {
                        CollectCurves(geometry, segments, minLength);
                    }

                    foreach (var segment in segments)
                    {
                        if (segment.Curve == null || !IsUsableCurve(segment.Curve, minLength))
                        {
                            curvesSkipped++;
                            continue;
                        }

                        try
                        {
                            DetailCurve detailCurve = doc.Create.NewDetailCurve(activeView, segment.Curve);

                            GraphicsStyle style = segment.StyleId != ElementId.InvalidElementId
                                ? doc.GetElement(segment.StyleId) as GraphicsStyle
                                : null;

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
                ? $"\n{notLinkedCount} DWG estaban importados (no vinculados): su texto no se " +
                  "pudo recrear porque no queda un archivo .dwg que releer (sólo funciona con " +
                  "DWG vinculados)."
                : string.Empty;

            string errorNote = textReadErrors > 0
                ? $"\n{textReadErrors} DWG vinculados no se pudieron releer (archivo movido/no " +
                  "encontrado, o formato no soportado por el lector)."
                : string.Empty;

            TaskDialog.Show(
                "Explotar DWGs — resumen",
                $"DWGs procesados: {importsProcessed}\n" +
                $"Detail Lines creadas: {linesCreated}\n" +
                $"Segmentos omitidos: {curvesSkipped}\n" +
                $"TextNotes creados: {textsCreated}{textNote}{errorNote}\n\n" +
                "Los DWG originales no se modificaron ni se eliminaron. Si ya no los " +
                "necesitas, ocúltalos o bórralos manualmente una vez que verifiques el " +
                "resultado.");

            return Result.Succeeded;
        }

        private static ElementId GetOrCreateTextNoteType(Document doc, double heightFeet, Dictionary<int, ElementId> cache)
        {
            // Se agrupan alturas casi iguales (redondeadas a 1/100 de pie) para no
            // crear un TextNoteType nuevo por cada mínima diferencia de precisión.
            int key = (int)Math.Round(heightFeet * 100.0);
            if (cache.TryGetValue(key, out ElementId cachedId))
            {
                return cachedId;
            }

            ElementId baseTypeId = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            TextNoteType baseType = doc.GetElement(baseTypeId) as TextNoteType;

            TextNoteType newType = baseType?.Duplicate($"DWG {heightFeet * 12.0:0.###}\"") as TextNoteType;
            if (newType == null)
            {
                cache[key] = baseTypeId;
                return baseTypeId;
            }

            Parameter sizeParam = newType.get_Parameter(BuiltInParameter.TEXT_SIZE);
            sizeParam?.Set(heightFeet);

            cache[key] = newType.Id;
            return newType.Id;
        }

        private static void CollectCurves(GeometryElement geometry, List<(Curve, ElementId)> output, double minLength)
        {
            foreach (GeometryObject geomObj in geometry)
            {
                CollectFromGeometryObject(geomObj, output, minLength);
            }
        }

        private static void CollectFromGeometryObject(GeometryObject geomObj, List<(Curve, ElementId)> output, double minLength)
        {
            switch (geomObj)
            {
                case GeometryInstance instance:
                    // Geometría anidada (p. ej. bloques/inserts del DWG): se toma ya
                    // transformada a coordenadas del proyecto.
                    GeometryElement nested = instance.GetInstanceGeometry();
                    if (nested != null)
                    {
                        CollectCurves(nested, output, minLength);
                    }
                    break;

                case Curve curve:
                    output.Add((curve, geomObj.GraphicsStyleId));
                    break;

                case PolyLine polyLine:
                    IList<XYZ> pts = polyLine.GetCoordinates();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        if (pts[i].DistanceTo(pts[i + 1]) > minLength)
                        {
                            output.Add((Line.CreateBound(pts[i], pts[i + 1]), geomObj.GraphicsStyleId));
                        }
                    }
                    break;

                case Solid solid:
                    foreach (Edge edge in solid.Edges)
                    {
                        output.Add((edge.AsCurve(), geomObj.GraphicsStyleId));
                    }
                    break;

                case Mesh mesh:
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        MeshTriangle tri = mesh.get_Triangle(i);
                        AddMeshEdge(tri.get_Vertex(0), tri.get_Vertex(1), geomObj.GraphicsStyleId, output, minLength);
                        AddMeshEdge(tri.get_Vertex(1), tri.get_Vertex(2), geomObj.GraphicsStyleId, output, minLength);
                        AddMeshEdge(tri.get_Vertex(2), tri.get_Vertex(0), geomObj.GraphicsStyleId, output, minLength);
                    }
                    break;
            }
        }

        private static void AddMeshEdge(XYZ a, XYZ b, ElementId styleId, List<(Curve, ElementId)> output, double minLength)
        {
            if (a.DistanceTo(b) > minLength)
            {
                output.Add((Line.CreateBound(a, b), styleId));
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
