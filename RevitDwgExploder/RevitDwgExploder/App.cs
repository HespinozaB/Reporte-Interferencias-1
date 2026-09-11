using System;
using System.Reflection;
using Autodesk.Revit.UI;

namespace RevitDwgExploder
{
    public class App : IExternalApplication
    {
        private const string TabName = "DWG Tools";
        private const string PanelName = "Explotar";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Exception)
            {
                // La pestaña ya existe (otro addin la creó primero, o Revit la recarga).
            }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var buttonData = new PushButtonData(
                "ExplodeDwgCommand",
                "Explotar" + Environment.NewLine + "DWGs",
                assemblyPath,
                "RevitDwgExploder.Commands.ExplodeDwgCommand")
            {
                ToolTip = "Explota por completo (Full Explode) los DWG importados/vinculados " +
                          "de la vista activa, preservando escala, tipos de línea y texto.",
            };

            var button = panel.AddItem(buttonData) as PushButton;
            if (button != null)
            {
                button.Enabled = true;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }
}
