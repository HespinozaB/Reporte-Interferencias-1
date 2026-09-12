using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitDwgExploder.Commands
{
    /// <summary>
    /// Reduce la cantidad de elementos que genera la explosión sin cambiar el
    /// dibujo: las polilíneas del DWG llegan troceadas en muchos segmentos
    /// rectos, así que aquí se vuelven a unir los tramos alineados en una sola
    /// línea y los que describen una circunferencia se reconstruyen como Arc.
    /// De las mallas sólo se conserva el contorno (las aristas interiores,
    /// compartidas por dos triángulos, no se dibujan).
    /// </summary>
    internal static class DwgGeometrySimplifier
    {
        // Tope de puntos por tramo: la comprobación de alineación es cuadrática
        // respecto al largo del tramo, así que conviene acotarla.
        private const int MaxRunLength = 200;

        /// <summary>
        /// Convierte una polilínea en el menor número de líneas y arcos que la
        /// representan dentro de <paramref name="tolerance"/>.
        /// </summary>
        public static List<Curve> Simplify(IList<XYZ> points, double tolerance, double minLength)
        {
            var result = new List<Curve>();
            List<XYZ> pts = RemoveDuplicates(points, minLength * 0.5);
            if (pts.Count < 2)
            {
                return result;
            }

            int i = 0;
            while (i < pts.Count - 1)
            {
                int lineEnd = FurthestCollinear(pts, i, tolerance);
                int arcEnd = FurthestOnArc(pts, i, tolerance);

                // Se prefiere el arco sólo si abarca más puntos que la recta:
                // así un tramo verdaderamente recto nunca se curva.
                if (arcEnd > lineEnd && arcEnd >= i + 2)
                {
                    Arc arc = TryCreateArc(pts[i], pts[(i + arcEnd) / 2], pts[arcEnd]);
                    if (arc != null && arc.Length > minLength)
                    {
                        result.Add(arc);
                        i = arcEnd;
                        continue;
                    }
                }

                if (pts[i].DistanceTo(pts[lineEnd]) > minLength)
                {
                    result.Add(Line.CreateBound(pts[i], pts[lineEnd]));
                }

                i = lineEnd;
            }

            return result;
        }

        /// <summary>
        /// Devuelve sólo las aristas del contorno de la malla: las compartidas
        /// por dos triángulos son interiores y no se dibujan.
        /// </summary>
        public static List<Curve> MeshBoundary(Mesh mesh, double minLength)
        {
            var edges = new Dictionary<string, (XYZ A, XYZ B, int Count)>();

            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                MeshTriangle tri = mesh.get_Triangle(i);
                AddEdge(edges, tri.get_Vertex(0), tri.get_Vertex(1));
                AddEdge(edges, tri.get_Vertex(1), tri.get_Vertex(2));
                AddEdge(edges, tri.get_Vertex(2), tri.get_Vertex(0));
            }

            var result = new List<Curve>();
            foreach (var entry in edges.Values)
            {
                if (entry.Count == 1 && entry.A.DistanceTo(entry.B) > minLength)
                {
                    result.Add(Line.CreateBound(entry.A, entry.B));
                }
            }

            return result;
        }

        private static void AddEdge(Dictionary<string, (XYZ, XYZ, int)> edges, XYZ a, XYZ b)
        {
            string keyA = Key(a);
            string keyB = Key(b);
            // La clave no depende del sentido: así la misma arista vista desde
            // sus dos triángulos cae en la misma entrada.
            string key = string.CompareOrdinal(keyA, keyB) <= 0 ? keyA + "|" + keyB : keyB + "|" + keyA;

            if (edges.TryGetValue(key, out var existing))
            {
                edges[key] = (existing.Item1, existing.Item2, existing.Item3 + 1);
            }
            else
            {
                edges[key] = (a, b, 1);
            }
        }

        private static string Key(XYZ p)
        {
            return $"{Math.Round(p.X, 5)},{Math.Round(p.Y, 5)},{Math.Round(p.Z, 5)}";
        }

        private static List<XYZ> RemoveDuplicates(IList<XYZ> points, double tolerance)
        {
            var pts = new List<XYZ>(points.Count);
            foreach (XYZ p in points)
            {
                if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > tolerance)
                {
                    pts.Add(p);
                }
            }
            return pts;
        }

        /// <summary>Último índice hasta el que todos los puntos siguen alineados con el primero.</summary>
        private static int FurthestCollinear(List<XYZ> pts, int start, double tolerance)
        {
            int end = start + 1;
            while (end + 1 < pts.Count && end - start < MaxRunLength)
            {
                if (!AllWithinLine(pts, start, end + 1, tolerance))
                {
                    break;
                }
                end++;
            }
            return end;
        }

        private static bool AllWithinLine(List<XYZ> pts, int start, int end, double tolerance)
        {
            for (int k = start + 1; k < end; k++)
            {
                if (DistanceToSegment(pts[k], pts[start], pts[end]) > tolerance)
                {
                    return false;
                }
            }
            return true;
        }

        private static double DistanceToSegment(XYZ p, XYZ a, XYZ b)
        {
            XYZ ab = b - a;
            double len = ab.GetLength();
            if (len < 1e-12)
            {
                return p.DistanceTo(a);
            }
            return ab.CrossProduct(p - a).GetLength() / len;
        }

        /// <summary>
        /// Último índice hasta el que todos los puntos caen sobre la misma
        /// circunferencia que definen los tres primeros del tramo.
        /// </summary>
        private static int FurthestOnArc(List<XYZ> pts, int start, double tolerance)
        {
            if (start + 2 >= pts.Count)
            {
                return start;
            }

            if (!TryCircle(pts[start], pts[start + 1], pts[start + 2], out XYZ center, out double radius))
            {
                return start;
            }

            int end = start + 2;
            while (end + 1 < pts.Count && end - start < MaxRunLength)
            {
                double d = Math.Abs(Distance2d(pts[end + 1], center) - radius);
                if (d > tolerance)
                {
                    break;
                }
                end++;
            }

            // Evita cerrar la circunferencia entera: Arc.Create necesita
            // extremos distintos.
            if (pts[start].DistanceTo(pts[end]) < tolerance)
            {
                end--;
            }

            return end >= start + 2 ? end : start;
        }

        private static double Distance2d(XYZ p, XYZ center)
        {
            double dx = p.X - center.X;
            double dy = p.Y - center.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Circunferencia que pasa por tres puntos, en el plano XY.</summary>
        private static bool TryCircle(XYZ p1, XYZ p2, XYZ p3, out XYZ center, out double radius)
        {
            center = null;
            radius = 0;

            double ax = p1.X, ay = p1.Y;
            double bx = p2.X, by = p2.Y;
            double cx = p3.X, cy = p3.Y;

            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-12)
            {
                return false;
            }

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;

            double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;

            center = new XYZ(ux, uy, p1.Z);
            radius = Distance2d(p1, center);

            return radius > 1e-9 && radius < 1e7;
        }

        private static Arc TryCreateArc(XYZ start, XYZ middle, XYZ end)
        {
            try
            {
                return Arc.Create(start, end, middle);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
