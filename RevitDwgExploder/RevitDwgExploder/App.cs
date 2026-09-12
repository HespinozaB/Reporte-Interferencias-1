using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace RevitDwgExploder
{
    public class App : IExternalApplication
    {
        private const string TabName = "EMASY";
        private const string PanelName = "DWG-Herramientas";

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

            var buttonData = new PushButtonData(
                "ExplodeDwgCommand",
                "EXPLOTAR" + Environment.NewLine + "DWG",
                Assembly.GetExecutingAssembly().Location,
                "RevitDwgExploder.Commands.ExplodeDwgCommand")
            {
                ToolTip = "Explotar un DWG a geometría nativa de Revit",
                LongDescription =
                    "Convierte los DWG de la vista activa en Detail Lines nativas, " +
                    "manteniendo posición, escala y estilo de línea de cada capa.\n\n" +
                    "Recrea los textos del CAD como TextNote ajustados a la escala de la " +
                    "vista, puede convertir los sombreados en regiones rellenas y reconstruye " +
                    "los arcos en vez de trocearlos en segmentos rectos.\n\n" +
                    "El DWG original no se modifica ni se elimina.",
            };

            var button = panel.AddItem(buttonData) as PushButton;
            if (button != null)
            {
                button.LargeImage = CreateIcon(32);
                button.Image = CreateIcon(16);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        /// <summary>
        /// Dibuja el icono en memoria (un plano fragmentándose en piezas) y lo
        /// entrega como imagen de WPF, así el addin no depende de archivos de
        /// recursos sueltos junto al ensamblado.
        /// </summary>
        private static BitmapImage CreateIcon(int size)
        {
            try
            {
                using (var bitmap = new Bitmap(size, size))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);

                        float unit = size / 32f;
                        Color frame = Color.FromArgb(38, 70, 122);
                        Color accent = Color.FromArgb(232, 122, 26);

                        using (var framePen = new Pen(frame, 2f * unit))
                        using (var accentPen = new Pen(accent, 2f * unit))
                        using (var accentBrush = new SolidBrush(accent))
                        {
                            // Hoja de plano
                            g.DrawRectangle(framePen, 3f * unit, 4f * unit, 15f * unit, 20f * unit);
                            g.DrawLine(framePen, 6f * unit, 10f * unit, 15f * unit, 10f * unit);
                            g.DrawLine(framePen, 6f * unit, 14f * unit, 15f * unit, 14f * unit);
                            g.DrawLine(framePen, 6f * unit, 18f * unit, 12f * unit, 18f * unit);

                            // Piezas saliendo: la "explosión" a elementos nativos
                            g.DrawLine(accentPen, 21f * unit, 16f * unit, 28f * unit, 9f * unit);
                            g.FillRectangle(accentBrush, 22f * unit, 20f * unit, 7f * unit, 2f * unit);
                            g.FillRectangle(accentBrush, 25f * unit, 25f * unit, 5f * unit, 2f * unit);
                        }
                    }

                    using (var stream = new MemoryStream())
                    {
                        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                        stream.Position = 0;

                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        return image;
                    }
                }
            }
            catch (Exception)
            {
                // Sin icono el botón sigue funcionando; no vale abortar la carga.
                return null;
            }
        }
    }
}
