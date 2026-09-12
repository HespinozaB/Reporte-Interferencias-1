# RevitDwgExploder — Addin para Revit 2024

Addin que agrega un botón en la cinta de Revit para convertir las instancias de
DWG importadas/vinculadas de la vista activa en **Detail Lines nativas de
Revit**, en la misma posición, escala y estilo de línea (capa) que ya tenían,
**sin modificar el DWG original** (no se borra ni se toca).

## Aviso importante: por qué NO es un "Full Explode"

Se intentó primero automatizar el comando nativo "Explotar completamente" de
Revit, pero se verificó directamente contra los metadatos de `RevitAPI.dll` /
`RevitAPIUI.dll` 2024 que **esa operación no existe en la API pública**: ni
`Document.Explode()` (no hay tal método en la clase `Document`) ni un
`PostableCommand` equivalente (no existe `FullExplode`/`PartialExplode` en ese
enum). Es una limitación conocida y documentada de Autodesk: el explode de un
DWG sólo se puede disparar manualmente desde el menú **Modificar → Explotar**.

Por eso este addin usa la única vía soportada por la API: leer la geometría
real del DWG (`ImportInstance.get_Geometry`) y recrearla como elementos
nativos (`DetailCurve`) con el mismo `LineStyle`. Esto preserva posición,
escala y apariencia de líneas exactamente.

## Texto: cómo se recupera (y su única limitación real)

La API de Revit no expone el contenido de un texto de CAD como cadena
editable a través de la geometría (no existe una clase `Text` entre los
`GeometryObject`). Para poder recrear el texto como `TextNote` nativo, el
addin **abre el archivo `.dwg` directamente** con
[ACadSharp](https://github.com/DomCR/ACadSharp) (lector .NET de código
abierto, no requiere AutoCAD) y lee sus entidades `TEXT`/`MTEXT` reales:
cadena, posición, altura y rotación.

**Un DWG *vinculado* (Link CAD) funciona automático:** conserva la ruta al
archivo original en disco, así que el addin lo reabre solo.

**Un DWG *importado* (embebido) no guarda esa ruta**, y en la práctica el
archivo original tampoco siempre se puede rastrear (se movió, se perdió, no
se tiene a mano). Para esos casos hay dos salidas, y el addin las combina:

1. Si al pulsar **Explotar DWGs** detecta instancias sin vínculo resoluble,
   pregunta si quieres localizar manualmente el/los archivo(s) `.dwg`
   originales. Si los indicas, obtiene el texto **exacto** igual que con un
   vínculo.
2. **Si no los indicas (o de plano no existen), igual recupera el texto**:
   aplica reconocimiento óptico (OCR, con [Tesseract](https://github.com/tesseract-ocr/tesseract)
   embebido — no depende de ningún archivo ni servicio externo) directamente
   sobre los propios trazos ya explotados. Agrupa las curvas cercanas entre sí
   (el patrón típico de los caracteres de una palabra), las reconoce como
   texto y crea el `TextNote` usando la posición y el tamaño **reales de la
   geometría de Revit** — por eso no puede desalinearse ni salir con el
   tamaño equivocado como podía pasarle al método basado en unidades del DWG.

   Es un método aproximado: puede fallar con texto muy pequeño, fuentes poco
   comunes, o cuando otra geometría cercana (tramas, símbolos) se confunde
   con un carácter. Por eso se usa como respaldo automático — nunca reemplaza
   al texto exacto cuando este sí está disponible — y conviene revisar los
   textos creados por esta vía.

El resumen final distingue cuántos TextNotes se crearon desde el `.dwg` real
(exactos) y cuántos por OCR (aproximados), además de cuántos DWG no tenían
vínculo resoluble y cuántos no se pudieron releer.

## Instalación rápida (sin compilar)

Ya hay un `.dll` compilado y verificado en `dist/RevitDwgExploder-2024.zip`:

1. Descomprime `dist/RevitDwgExploder-2024.zip`. Obtendrás:
   - `RevitDwgExploder.addin` (el manifiesto, suelto)
   - `RevitDwgExploder-2024/RevitDwgExploder.dll` (el addin, en su propia subcarpeta)
2. Copia **ambos, tal cual esa estructura**, directamente dentro de:
   `%AppData%\Autodesk\Revit\Addins\2024\`

   Importante: el archivo `RevitDwgExploder.addin` debe quedar **suelto en la
   raíz** de esa carpeta (junto a otros `.addin` que ya tengas, como los de
   Speckle) — Revit sólo escanea manifiestos `.addin` que estén directamente
   ahí, no dentro de subcarpetas. El `.dll` sí puede ir en su propia subcarpeta
   (`RevitDwgExploder-2024\`), tal como lo referencia el manifiesto.
3. Cierra Revit por completo (si estaba abierto) y vuelve a abrirlo.
4. Si aparece un aviso de "publisher no verificado", elige **Always Load**.
5. Ve a la pestaña **Add-Ins** → panel **Explotar** → botón **Explotar DWGs**.

## Estructura

```
RevitDwgExploder/
├── RevitDwgExploder.sln
├── dist/
│   └── RevitDwgExploder-2024.zip   # dll + .addin listos para instalar
└── RevitDwgExploder/
    ├── RevitDwgExploder.csproj
    ├── App.cs                     # IExternalApplication: crea el botón en la cinta
    ├── Commands/
    │   ├── ExplodeDwgCommand.cs     # IExternalCommand: lee geometría y crea Detail Lines
    │   ├── DwgTextImporter.cs       # Relee el .dwg (vinculado o indicado) con ACadSharp → texto exacto
    │   └── DwgTextOcrRecognizer.cs  # Agrupa trazos y reconoce texto por OCR → texto aproximado, sin archivo
    ├── tessdata/
    │   └── eng.traineddata        # Modelo de idioma de Tesseract para el OCR
    └── Properties/
        └── AssemblyInfo.cs
```

## Compilar desde el código fuente (opcional)

El proyecto usa el paquete NuGet `Nice3point.Revit.Api.RevitAPI(UI)` 2024.2.0
(los mismos tipos/firmas que `RevitAPI.dll`/`RevitAPIUI.dll`), así que **no
necesitas tener Revit instalado para compilar** — sólo el SDK de .NET.

1. Abre `RevitDwgExploder.sln` en Visual Studio 2022 (o ejecuta
   `dotnet build -c Release` desde `RevitDwgExploder/RevitDwgExploder/`).
2. Al compilar en Windows, el postbuild copia automáticamente el `.dll` y el
   `.addin` a `%AppData%\Autodesk\Revit\Addins\2024\`.
3. Abre Revit 2024 (pestaña **Add-Ins** → panel **Explotar**).

## Uso

1. Abre la vista donde están las importaciones/vínculos de DWG.
2. (Opcional) Selecciona manualmente las instancias de DWG que quieres
   convertir; si no seleccionas nada, el addin toma **todas** las instancias
   de CAD visibles en la vista activa.
3. Pulsa **Explotar DWGs**.
4. El addin crea las Detail Lines y los TextNotes correspondientes (exactos
   desde el `.dwg` cuando está disponible, por OCR cuando no), y muestra un
   resumen (DWGs procesados, líneas creadas, segmentos omitidos, textos
   exactos vs. por OCR). El DWG original queda intacto — si ya no lo
   necesitas, ocúltalo o bórralo tú manualmente después de revisar el
   resultado.

## Nota sobre precisión del texto

- **Texto exacto (desde el `.dwg`):** la posición y tamaño se calculan usando
  las unidades declaradas en el propio archivo (`INSUNITS`) combinadas con la
  transformación de ubicación del vínculo en Revit. Si al vincular el DWG
  usaste un **factor de escala manual** distinto de "Auto - Detectar" (poco
  común, pero posible en el diálogo de Link CAD), el texto podría aparecer
  desplazado por ese mismo factor.
- **Texto por OCR (sin `.dwg`):** la posición y el tamaño salen directamente
  de la geometría ya explotada en Revit, así que no tiene ese riesgo de
  desalineación — su limitación es la precisión del reconocimiento en sí
  (puede equivocar un carácter, o no detectar un texto muy pequeño/decorativo).

Si alguno de los dos casos no da buen resultado en tu proyecto, cuéntame qué
falla (con una captura ayuda mucho) y ajusto los parámetros correspondientes.
