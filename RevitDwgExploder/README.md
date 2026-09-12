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

**Limitación de texto:** la API de Revit no expone el contenido de un texto de
CAD como cadena editable (no existe una clase `Text` entre los
`GeometryObject`). Si el DWG dibuja el texto con fuentes de línea (SHX), su
contorno queda representado por las Detail Lines resultantes; si usa fuentes
TrueType (rellenas), no hay forma soportada por la API de recuperarlo como
texto ni como líneas — sólo el comando manual de Revit puede hacerlo.

## Instalación rápida (sin compilar)

Ya hay un `.dll` compilado y verificado en `dist/RevitDwgExploder-2024.zip`:

1. Descomprime `dist/RevitDwgExploder-2024.zip`.
2. Copia la carpeta `RevitDwgExploder-2024` completa (con `RevitDwgExploder.dll`
   y `RevitDwgExploder.addin` juntos) a:
   `%AppData%\Autodesk\Revit\Addins\2024\`
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
    │   └── ExplodeDwgCommand.cs   # IExternalCommand: lee geometría y crea Detail Lines
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
4. El addin crea las Detail Lines correspondientes y muestra un resumen
   (DWGs procesados, líneas creadas, segmentos omitidos). El DWG original
   queda intacto — si ya no lo necesitas, ocúltalo o bórralo tú manualmente
   después de revisar el resultado.
