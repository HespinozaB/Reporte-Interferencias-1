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

            int linesCreated = 0;
            int curvesSkipped = 0;
            int importsProcessed = 0;

            using (Transaction t = new Transaction(doc, "Explotar DWGs a Detail Lines"))
            {
                t.Start();

                foreach (ImportInstance importInstance in targets)
                {
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
                        CollectCurves(geometry, segments);
                    }

                    foreach (var segment in segments)
                    {
                        if (segment.Curve == null || !IsUsableCurve(segment.Curve))
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

            TaskDialog.Show(
                "Explotar DWGs — resumen",
                $"DWGs procesados: {importsProcessed}\n" +
                $"Detail Lines creadas: {linesCreated}\n" +
                $"Segmentos omitidos: {curvesSkipped}\n\n" +
                "Los DWG originales no se modificaron ni se eliminaron. Si ya no los " +
                "necesitas, ocúltalos o bórralos manualmente una vez que verifiques el " +
                "resultado.");

            return Result.Succeeded;
        }

        private static void CollectCurves(GeometryElement geometry, List<(Curve, ElementId)> output)
        {
            foreach (GeometryObject geomObj in geometry)
            {
                CollectFromGeometryObject(geomObj, output);
            }
        }

        private static void CollectFromGeometryObject(GeometryObject geomObj, List<(Curve, ElementId)> output)
        {
            switch (geomObj)
            {
                case GeometryInstance instance:
                    // Geometría anidada (p. ej. bloques/inserts del DWG): se toma ya
                    // transformada a coordenadas del proyecto.
                    GeometryElement nested = instance.GetInstanceGeometry();
                    if (nested != null)
                    {
                        CollectCurves(nested, output);
                    }
                    break;

                case Curve curve:
                    output.Add((curve, geomObj.GraphicsStyleId));
                    break;

                case PolyLine polyLine:
                    IList<XYZ> pts = polyLine.GetCoordinates();
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        if (pts[i].DistanceTo(pts[i + 1]) > 1e-6)
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
                        AddMeshEdge(tri.get_Vertex(0), tri.get_Vertex(1), geomObj.GraphicsStyleId, output);
                        AddMeshEdge(tri.get_Vertex(1), tri.get_Vertex(2), geomObj.GraphicsStyleId, output);
                        AddMeshEdge(tri.get_Vertex(2), tri.get_Vertex(0), geomObj.GraphicsStyleId, output);
                    }
                    break;
            }
        }

        private static void AddMeshEdge(XYZ a, XYZ b, ElementId styleId, List<(Curve, ElementId)> output)
        {
            if (a.DistanceTo(b) > 1e-6)
            {
                output.Add((Line.CreateBound(a, b), styleId));
            }
        }

        private static bool IsUsableCurve(Curve curve)
        {
            try
            {
                return curve.IsBound && curve.Length > 1e-6;
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
