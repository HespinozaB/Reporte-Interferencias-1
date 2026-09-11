using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Busca las instancias de DWG (ImportInstance) en la vista activa (o en la
    /// selección actual, si el usuario ya seleccionó algo) y las explota por
    /// completo usando el comando nativo de Revit "Full Explode", que es la
    /// única vía que convierte la geometría CAD a elementos nativos (Line,
    /// TextNote, FilledRegion, etc.) sin alterar escala, tipos de línea ni texto.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ExplodeDwgCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData.Application;
            UIDocument uiDoc = uiApp.ActiveUIDocument;
            Document doc = uiDoc.Document;

            List<ElementId> targetIds = GetSelectedImportInstanceIds(uiDoc);
            List<ElementId> skippedPinned = new List<ElementId>();

            if (targetIds.Count == 0)
            {
                targetIds = GetImportInstanceIdsInView(doc, uiDoc.ActiveView);
            }

            if (targetIds.Count == 0)
            {
                TaskDialog.Show(
                    "Explotar DWGs",
                    "No se encontraron instancias de DWG importado/vinculado en la selección " +
                    "actual ni en la vista activa.");
                return Result.Cancelled;
            }

            // Los elementos anclados (Pinned) no se pueden explotar: se desanclan
            // primero (avisando al usuario en el resumen final).
            using (Transaction t = new Transaction(doc, "Preparar DWGs para explotar"))
            {
                t.Start();
                foreach (ElementId id in targetIds.ToList())
                {
                    Element el = doc.GetElement(id);
                    if (el == null)
                    {
                        continue;
                    }

                    if (el.Pinned)
                    {
                        el.Pinned = false;
                        skippedPinned.Add(id);
                    }
                }
                t.Commit();
            }

            ExplodeQueueProcessor.Start(uiApp, targetIds, skippedPinned.Count);

            return Result.Succeeded;
        }

        private static List<ElementId> GetSelectedImportInstanceIds(UIDocument uiDoc)
        {
            Document doc = uiDoc.Document;
            return uiDoc.Selection.GetElementIds()
                .Select(doc.GetElement)
                .Where(el => el is ImportInstance)
                .Select(el => el.Id)
                .ToList();
        }

        private static List<ElementId> GetImportInstanceIdsInView(Document doc, View view)
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(ImportInstance))
                .WhereElementIsNotElementType()
                .ToElementIds()
                .ToList();
        }
    }
}
