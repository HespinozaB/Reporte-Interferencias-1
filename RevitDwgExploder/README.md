# RevitDwgExploder — Addin para Revit 2024

Addin que agrega un botón en la cinta de Revit para **explotar por completo (Full Explode)**
las instancias de DWG importadas/vinculadas en la vista activa, preservando:

- Escala / tamaño real de la geometría (no se reescala nada, se usa el comando nativo de Revit).
- Tipos y grosores de línea (Revit los mapea a Line Styles según las capas del DWG).
- Texto (se convierte a `TextNote` nativo de Revit con el tamaño real del DWG).
- La geometría no se modifica: es la misma conversión que hace Revit al usar
  manualmente **Modificar → Explotar → Explotar completamente**.

## Por qué se usa el comando de UI (`PostCommand`) y no la API directa

La Revit API expone `Document.Explode(ElementId)`, pero ese método sólo realiza un
**"Partial Explode"** (agrupa la geometría por capa dentro de `ImportInstance` anidados,
sin convertirla a elementos nativos). La conversión completa a `Line`, `TextNote`,
`FilledRegion`, etc. (**"Full Explode"**) sólo está disponible como comando de interfaz
(`PostableCommand.FullExplode`). Por eso este addin selecciona cada `ImportInstance`
y despacha ese comando nativo, en lugar de reimplementar la conversión (lo que sí
podría alterar tamaños, estilos de línea o el texto).

## Estructura

```
RevitDwgExploder/
├── RevitDwgExploder.sln
└── RevitDwgExploder/
    ├── RevitDwgExploder.csproj
    ├── App.cs                     # IExternalApplication: crea el botón en la cinta
    ├── Commands/
    │   └── ExplodeDwgCommand.cs   # IExternalCommand: ubica los DWG y los explota
    └── Properties/
        └── AssemblyInfo.cs
```

El manifiesto `RevitDwgExploder.addin` se genera/copia automáticamente al
directorio de addins de Revit al compilar (ver `.csproj`, target `AfterBuild`),
o puedes copiarlo manualmente (ver más abajo).

## Requisitos

- Visual Studio 2022.
- Revit 2024 instalado (para tomar `RevitAPI.dll` y `RevitAPIUI.dll` desde
  `C:\Program Files\Autodesk\Revit 2024\`), o el paquete NuGet
  `Nice3point.Revit.Api.RevitAPI` / `Nice3point.Revit.Api.RevitAPIUI` versión `2024.*`.
- .NET Framework 4.8 (Revit 2024 sigue usando .NET Framework para addins clásicos).

## Compilar

1. Abre `RevitDwgExploder.sln` en Visual Studio.
2. Si no usas el paquete NuGet, ajusta en `RevitDwgExploder.csproj` el `HintPath`
   de `RevitAPI` y `RevitAPIUI` a tu instalación de Revit 2024.
3. Compila en `Debug` o `Release`. El postbuild copia:
   - `RevitDwgExploder.dll` (+ pdb)
   - `RevitDwgExploder.addin`

   a `%AppData%\Autodesk\Revit\Addins\2024\`.

4. Abre Revit 2024. En la pestaña **Add-Ins** aparecerá el panel **DWG Tools**
   con el botón **Explotar DWGs**.

## Uso

1. Abre la vista donde están las importaciones/vínculos de DWG.
2. (Opcional) Selecciona manualmente las instancias de DWG que quieres explotar;
   si no seleccionas nada, el addin tomará **todas** las instancias de CAD
   visibles en la vista activa.
3. Pulsa **Explotar DWGs**.
4. El addin:
   - Ignora elementos anclados (`Pinned`) y avisa cuáles se saltó.
   - Va, uno por uno, seleccionando cada `ImportInstance` y ejecutando
     `Full Explode` nativo de Revit (Revit puede mostrar su propio diálogo de
     advertencia estándar; simplemente acéptalo).
   - Al terminar muestra un resumen (cuántos se explotaron / se saltaron).

> Nota: como el "Full Explode" es un comando de interfaz, Revit lo procesa de forma
> asíncrona entre transacciones (evento `Idling`). Si tienes muchos DWG, el proceso
> puede tardar unos segundos porque Revit necesita "respirar" entre cada uno.
