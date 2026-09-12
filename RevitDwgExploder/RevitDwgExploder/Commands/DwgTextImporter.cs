using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Header;
using ACadSharp.IO;
using ACadSharp.Types.Units;
using Autodesk.Revit.DB;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Revit no expone el contenido de texto de un import CAD a través de su
    /// API de geometría (no existe una clase "Text" entre los GeometryObject).
    /// Para recrear el texto como TextNote nativo, esta clase abre el .dwg
    /// vinculado directamente con ACadSharp (lector .NET independiente, no
    /// requiere AutoCAD) y lee sus entidades TEXT/MTEXT: cadena, posición,
    /// altura y rotación tal como están en el archivo original.
    ///
    /// Sólo funciona para DWG VINCULADOS (Link CAD): un DWG importado
    /// (embebido) no conserva una referencia al archivo de origen, así que no
    /// hay nada que reabrir.
    /// </summary>
    internal static class DwgTextImporter
    {
        internal class DwgTextEntry
        {
            public string Text;
            public XYZ Position;
            public double HeightFeet;
            public double RotationRadians;
        }

        internal enum ReadStatus
        {
            Ok,
            NotLinked,
            FileNotFound,
            ReadError,
        }

        public static ReadStatus TryReadTexts(
            Document doc,
            ImportInstance importInstance,
            out List<DwgTextEntry> texts)
        {
            texts = new List<DwgTextEntry>();

            if (!importInstance.IsLinked)
            {
                return ReadStatus.NotLinked;
            }

            Element typeElem = doc.GetElement(importInstance.GetTypeId());
            ExternalFileReference extRef = typeElem?.GetExternalFileReference();
            if (extRef == null)
            {
                return ReadStatus.NotLinked;
            }

            string path;
            try
            {
                ModelPath modelPath = extRef.GetAbsolutePath();
                path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
            }
            catch (Exception)
            {
                return ReadStatus.FileNotFound;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return ReadStatus.FileNotFound;
            }

            CadDocument cadDoc;
            try
            {
                using (DwgReader reader = new DwgReader(path))
                {
                    cadDoc = reader.Read();
                }
            }
            catch (Exception)
            {
                return ReadStatus.ReadError;
            }

            double unitScale = GetFeetPerDwgUnit(cadDoc.Header.InsUnits);
            Transform transform = importInstance.GetTransform();
            double basisAngle = Math.Atan2(transform.BasisX.Y, transform.BasisX.X);

            foreach (Entity entity in cadDoc.Entities)
            {
                switch (entity)
                {
                    case TextEntity textEntity:
                        AddEntry(texts, textEntity.Value, textEntity.InsertPoint, textEntity.Height,
                            textEntity.Rotation, unitScale, transform, basisAngle);
                        break;

                    case MText mText:
                        AddEntry(texts, mText.PlainText, mText.InsertPoint, mText.Height,
                            mText.Rotation, unitScale, transform, basisAngle);
                        break;
                }
            }

            return ReadStatus.Ok;
        }

        private static void AddEntry(
            List<DwgTextEntry> texts,
            string value,
            CSMath.XYZ dwgPoint,
            double dwgHeight,
            double dwgRotation,
            double unitScale,
            Transform transform,
            double basisAngle)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            XYZ localPoint = new XYZ(dwgPoint.X * unitScale, dwgPoint.Y * unitScale, dwgPoint.Z * unitScale);
            XYZ worldPoint = transform.OfPoint(localPoint);

            texts.Add(new DwgTextEntry
            {
                Text = value,
                Position = worldPoint,
                HeightFeet = dwgHeight * unitScale * transform.Scale,
                RotationRadians = dwgRotation + basisAngle,
            });
        }

        private static double GetFeetPerDwgUnit(UnitsType units)
        {
            switch (units)
            {
                case UnitsType.Millimeters: return 1.0 / 304.8;
                case UnitsType.Centimeters: return 1.0 / 30.48;
                case UnitsType.Decimeters: return 1.0 / 3.048;
                case UnitsType.Meters: return 1.0 / 0.3048;
                case UnitsType.Kilometers: return 1000.0 / 0.3048;
                case UnitsType.Inches: return 1.0 / 12.0;
                case UnitsType.Feet: return 1.0;
                case UnitsType.Yards: return 3.0;
                case UnitsType.Miles: return 5280.0;
                case UnitsType.USSurveyFeet: return 1.000002;
                case UnitsType.USSurveyInches: return 1.000002 / 12.0;
                case UnitsType.USSurveyYards: return 1.000002 * 3.0;
                case UnitsType.USSurveyMiles: return 1.000002 * 5280.0;
                default:
                    // Unitless u otras unidades poco usuales en planos: se asume
                    // que el DWG ya está en las mismas unidades que el proyecto
                    // (milímetros es, con diferencia, lo más común en la práctica
                    // cuando el archivo no declara unidades).
                    return 1.0 / 304.8;
            }
        }
    }
}
