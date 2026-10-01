# Reporte de interferencias — Tabique de fachada ventilada con placa cementicia

Prueba de detección de interferencias en Navisworks (Clash Detective).

| Campo | Valor |
|---|---|
| Elemento A (rojo) | Tabique de fachada ventilada con placa cementicia |
| Elemento B (verde) | Estructura: losas, vigas y pilares |
| Tipo de prueba | Hard clash |
| Capturas | 62 (`cd000001.jpg` – `cd000062.jpg`) |
| Partida en Lookahead | Frente 5 – Arquitectura, ítem 5 (fila 542 de `LooKAHead_infraestructura_sur_ok.xlsx`) |
| Estado | Nuevo — pendiente de revisión por especialidades |

> Clasificación hecha a partir de las capturas. Sin el `.nwd` ni el XML del Clash Detective no hay distancias de penetración ni IDs de elemento; conviene complementar con el reporte exportado desde Navisworks.

## Resumen

| Grupo | Descripción | Prioridad | Cant. |
|---|---|---|---|
| [G1](#g1) | Tabique de fachada vs. canto de losa / viga perimetral (vista exterior) | Alta | 20 |
| [G2](#g2) | Tabique de fachada vs. losa (vista superior, tabique bajo la losa) | Alta | 14 |
| [G3](#g3) | Tabique en encuentro pilar–viga (esquinas) | Alta | 8 |
| [G4](#g4) | Tabique bajo losa junto a pilar (vista inferior) | Media | 6 |
| [G5](#g5) | Elemento puntual atravesando losa sobre tabique | Media | 2 |
| [G6](#g6) | Vistas no concluyentes (cámara dentro de la losa o choque oculto) | Por revisar | 12 |
| | **Total** | | **62** |

**Conclusión:** la interferencia es sistemática — el tabique de fachada ventilada está modelado en el plano del borde de la estructura en lugar de por fuera de ella. Corregir la posición del muro en el modelo de arquitectura (línea de ubicación y restricciones base–tope) resolvería G1–G4 en bloque; G5 requiere detalle de anclaje y G6 nuevas vistas.

## Impacto en programación (Lookahead)

- La partida *Tabique de fachada ventilada con placa cementicia* figura en el Lookahead (Frente 5 – Arquitectura, ítem 5) **sin barras programadas** en el horizonte del documento (ago-2025 en adelante). Lo mismo ocurre con las partidas relacionadas *Panel aislante de fachada con malla superficial* (ítem 12) y *Acabado microcemento color gris en fachada* (ítem 13).
- Las interferencias deben cerrarse antes de programar la partida: la solución de G1–G4 cambia el detalle de anclaje al canto de losa y puede requerir insertos o escuadras que deben quedar definidos antes del vaciado de losas y columnas de cada bloque.
- Restricción sugerida para el Lookahead: *"Liberar modelo de fachada ventilada sin interferencias con estructura"* como predecesora de la partida.

## G1

### Tabique de fachada vs. canto de losa / viga perimetral (vista exterior)

- **Prioridad:** Alta
- **Capturas:** `cd000001.jpg`, `cd000002.jpg`, `cd000003.jpg`, `cd000004.jpg`, `cd000005.jpg`, `cd000006.jpg`, `cd000012.jpg`, `cd000014.jpg`, `cd000022.jpg`, `cd000023.jpg`, `cd000024.jpg`, `cd000031.jpg`, `cd000042.jpg`, `cd000043.jpg`, `cd000055.jpg`, `cd000057.jpg`, `cd000058.jpg`, `cd000059.jpg`, `cd000060.jpg`, `cd000061.jpg`
- **Descripción:** El paño del tabique (placa cementicia + perfilería) atraviesa el canto de la losa y la viga de borde en lugar de quedar adosado o colgado por fuera. Se repite en todo el desarrollo de la fachada y en cada nivel.
- **Acción propuesta:** Desplazar el tabique hacia el exterior del plomo de losa (o recortarlo entre losas) dejando la holgura de montaje; definir detalle de anclaje de la subestructura de la fachada ventilada al canto de losa.

|   |   |   |   |
|---|---|---|---|
| <img src="cd000001.jpg" width="220"><br>`cd000001.jpg` | <img src="cd000002.jpg" width="220"><br>`cd000002.jpg` | <img src="cd000003.jpg" width="220"><br>`cd000003.jpg` | <img src="cd000004.jpg" width="220"><br>`cd000004.jpg` |
| <img src="cd000005.jpg" width="220"><br>`cd000005.jpg` | <img src="cd000006.jpg" width="220"><br>`cd000006.jpg` | <img src="cd000012.jpg" width="220"><br>`cd000012.jpg` | <img src="cd000014.jpg" width="220"><br>`cd000014.jpg` |
| <img src="cd000022.jpg" width="220"><br>`cd000022.jpg` | <img src="cd000023.jpg" width="220"><br>`cd000023.jpg` | <img src="cd000024.jpg" width="220"><br>`cd000024.jpg` | <img src="cd000031.jpg" width="220"><br>`cd000031.jpg` |
| <img src="cd000042.jpg" width="220"><br>`cd000042.jpg` | <img src="cd000043.jpg" width="220"><br>`cd000043.jpg` | <img src="cd000055.jpg" width="220"><br>`cd000055.jpg` | <img src="cd000057.jpg" width="220"><br>`cd000057.jpg` |
| <img src="cd000058.jpg" width="220"><br>`cd000058.jpg` | <img src="cd000059.jpg" width="220"><br>`cd000059.jpg` | <img src="cd000060.jpg" width="220"><br>`cd000060.jpg` | <img src="cd000061.jpg" width="220"><br>`cd000061.jpg` |

## G2

### Tabique de fachada vs. losa (vista superior, tabique bajo la losa)

- **Prioridad:** Alta
- **Capturas:** `cd000008.jpg`, `cd000009.jpg`, `cd000011.jpg`, `cd000013.jpg`, `cd000017.jpg`, `cd000018.jpg`, `cd000020.jpg`, `cd000021.jpg`, `cd000025.jpg`, `cd000026.jpg`, `cd000040.jpg`, `cd000045.jpg`, `cd000046.jpg`, `cd000047.jpg`
- **Descripción:** Mismo tipo de choque que G1 visto desde la losa superior: el tabique se proyecta dentro del espesor de la losa a lo largo del borde.
- **Acción propuesta:** Igual que G1. Revisar restricciones base/tope del tabique para que termine bajo la losa con junta.

|   |   |   |   |
|---|---|---|---|
| <img src="cd000008.jpg" width="220"><br>`cd000008.jpg` | <img src="cd000009.jpg" width="220"><br>`cd000009.jpg` | <img src="cd000011.jpg" width="220"><br>`cd000011.jpg` | <img src="cd000013.jpg" width="220"><br>`cd000013.jpg` |
| <img src="cd000017.jpg" width="220"><br>`cd000017.jpg` | <img src="cd000018.jpg" width="220"><br>`cd000018.jpg` | <img src="cd000020.jpg" width="220"><br>`cd000020.jpg` | <img src="cd000021.jpg" width="220"><br>`cd000021.jpg` |
| <img src="cd000025.jpg" width="220"><br>`cd000025.jpg` | <img src="cd000026.jpg" width="220"><br>`cd000026.jpg` | <img src="cd000040.jpg" width="220"><br>`cd000040.jpg` | <img src="cd000045.jpg" width="220"><br>`cd000045.jpg` |
| <img src="cd000046.jpg" width="220"><br>`cd000046.jpg` | <img src="cd000047.jpg" width="220"><br>`cd000047.jpg` |   |   |

## G3

### Tabique en encuentro pilar–viga (esquinas)

- **Prioridad:** Alta
- **Capturas:** `cd000032.jpg`, `cd000033.jpg`, `cd000034.jpg`, `cd000035.jpg`, `cd000041.jpg`, `cd000051.jpg`, `cd000056.jpg`, `cd000062.jpg`
- **Descripción:** Paños verticales de tabique en el nudo pilar–viga atraviesan el pilar y las vigas que llegan a él, recortando también la losa en el encuentro.
- **Acción propuesta:** Interrumpir el tabique en las caras del pilar y resolver el encuentro con remate/esquinero; la subestructura no debe anclarse dentro del pilar.

|   |   |   |   |
|---|---|---|---|
| <img src="cd000032.jpg" width="220"><br>`cd000032.jpg` | <img src="cd000033.jpg" width="220"><br>`cd000033.jpg` | <img src="cd000034.jpg" width="220"><br>`cd000034.jpg` | <img src="cd000035.jpg" width="220"><br>`cd000035.jpg` |
| <img src="cd000041.jpg" width="220"><br>`cd000041.jpg` | <img src="cd000051.jpg" width="220"><br>`cd000051.jpg` | <img src="cd000056.jpg" width="220"><br>`cd000056.jpg` | <img src="cd000062.jpg" width="220"><br>`cd000062.jpg` |

## G4

### Tabique bajo losa junto a pilar (vista inferior)

- **Prioridad:** Media
- **Capturas:** `cd000036.jpg`, `cd000037.jpg`, `cd000038.jpg`, `cd000039.jpg`, `cd000044.jpg`, `cd000050.jpg`
- **Descripción:** El tope del tabique penetra la cara inferior de la losa en la zona del pilar de esquina.
- **Acción propuesta:** Ajustar el nivel de tope del tabique a cielo de losa menos holgura.

|   |   |   |   |
|---|---|---|---|
| <img src="cd000036.jpg" width="220"><br>`cd000036.jpg` | <img src="cd000037.jpg" width="220"><br>`cd000037.jpg` | <img src="cd000038.jpg" width="220"><br>`cd000038.jpg` | <img src="cd000039.jpg" width="220"><br>`cd000039.jpg` |
| <img src="cd000044.jpg" width="220"><br>`cd000044.jpg` | <img src="cd000050.jpg" width="220"><br>`cd000050.jpg` |   |   |

## G5

### Elemento puntual atravesando losa sobre tabique

- **Prioridad:** Media
- **Capturas:** `cd000049.jpg`, `cd000054.jpg`
- **Descripción:** Elemento vertical delgado (montante/perfil) del sistema de fachada atraviesa la losa sobre el paño del tabique.
- **Acción propuesta:** Si es montante de la subestructura, anclarlo con escuadra al canto de losa en lugar de pasar a través de ella.

|   |   |   |   |
|---|---|---|---|
| <img src="cd000049.jpg" width="220"><br>`cd000049.jpg` | <img src="cd000054.jpg" width="220"><br>`cd000054.jpg` |   |   |

## G6

### Vistas no concluyentes (cámara dentro de la losa o choque oculto)

- **Prioridad:** Por revisar
- **Capturas:** `cd000007.jpg`, `cd000010.jpg`, `cd000015.jpg`, `cd000016.jpg`, `cd000019.jpg`, `cd000027.jpg`, `cd000028.jpg`, `cd000029.jpg`, `cd000030.jpg`, `cd000048.jpg`, `cd000052.jpg`, `cd000053.jpg`
- **Descripción:** La vista guardada queda dentro del sólido de la losa o el elemento en rojo no es visible.
- **Acción propuesta:** Regenerar el punto de vista en Navisworks (Clash Detective → vista automática / aislar) y reclasificar.

|   |   |   |   |
|---|---|---|---|
| <img src="cd000007.jpg" width="220"><br>`cd000007.jpg` | <img src="cd000010.jpg" width="220"><br>`cd000010.jpg` | <img src="cd000015.jpg" width="220"><br>`cd000015.jpg` | <img src="cd000016.jpg" width="220"><br>`cd000016.jpg` |
| <img src="cd000019.jpg" width="220"><br>`cd000019.jpg` | <img src="cd000027.jpg" width="220"><br>`cd000027.jpg` | <img src="cd000028.jpg" width="220"><br>`cd000028.jpg` | <img src="cd000029.jpg" width="220"><br>`cd000029.jpg` |
| <img src="cd000030.jpg" width="220"><br>`cd000030.jpg` | <img src="cd000048.jpg" width="220"><br>`cd000048.jpg` | <img src="cd000052.jpg" width="220"><br>`cd000052.jpg` | <img src="cd000053.jpg" width="220"><br>`cd000053.jpg` |

## Correlativo

| Captura | Grupo |
|---|---|
| [`cd000001.jpg`](cd000001.jpg) | G1 |
| [`cd000002.jpg`](cd000002.jpg) | G1 |
| [`cd000003.jpg`](cd000003.jpg) | G1 |
| [`cd000004.jpg`](cd000004.jpg) | G1 |
| [`cd000005.jpg`](cd000005.jpg) | G1 |
| [`cd000006.jpg`](cd000006.jpg) | G1 |
| [`cd000007.jpg`](cd000007.jpg) | G6 |
| [`cd000008.jpg`](cd000008.jpg) | G2 |
| [`cd000009.jpg`](cd000009.jpg) | G2 |
| [`cd000010.jpg`](cd000010.jpg) | G6 |
| [`cd000011.jpg`](cd000011.jpg) | G2 |
| [`cd000012.jpg`](cd000012.jpg) | G1 |
| [`cd000013.jpg`](cd000013.jpg) | G2 |
| [`cd000014.jpg`](cd000014.jpg) | G1 |
| [`cd000015.jpg`](cd000015.jpg) | G6 |
| [`cd000016.jpg`](cd000016.jpg) | G6 |
| [`cd000017.jpg`](cd000017.jpg) | G2 |
| [`cd000018.jpg`](cd000018.jpg) | G2 |
| [`cd000019.jpg`](cd000019.jpg) | G6 |
| [`cd000020.jpg`](cd000020.jpg) | G2 |
| [`cd000021.jpg`](cd000021.jpg) | G2 |
| [`cd000022.jpg`](cd000022.jpg) | G1 |
| [`cd000023.jpg`](cd000023.jpg) | G1 |
| [`cd000024.jpg`](cd000024.jpg) | G1 |
| [`cd000025.jpg`](cd000025.jpg) | G2 |
| [`cd000026.jpg`](cd000026.jpg) | G2 |
| [`cd000027.jpg`](cd000027.jpg) | G6 |
| [`cd000028.jpg`](cd000028.jpg) | G6 |
| [`cd000029.jpg`](cd000029.jpg) | G6 |
| [`cd000030.jpg`](cd000030.jpg) | G6 |
| [`cd000031.jpg`](cd000031.jpg) | G1 |
| [`cd000032.jpg`](cd000032.jpg) | G3 |
| [`cd000033.jpg`](cd000033.jpg) | G3 |
| [`cd000034.jpg`](cd000034.jpg) | G3 |
| [`cd000035.jpg`](cd000035.jpg) | G3 |
| [`cd000036.jpg`](cd000036.jpg) | G4 |
| [`cd000037.jpg`](cd000037.jpg) | G4 |
| [`cd000038.jpg`](cd000038.jpg) | G4 |
| [`cd000039.jpg`](cd000039.jpg) | G4 |
| [`cd000040.jpg`](cd000040.jpg) | G2 |
| [`cd000041.jpg`](cd000041.jpg) | G3 |
| [`cd000042.jpg`](cd000042.jpg) | G1 |
| [`cd000043.jpg`](cd000043.jpg) | G1 |
| [`cd000044.jpg`](cd000044.jpg) | G4 |
| [`cd000045.jpg`](cd000045.jpg) | G2 |
| [`cd000046.jpg`](cd000046.jpg) | G2 |
| [`cd000047.jpg`](cd000047.jpg) | G2 |
| [`cd000048.jpg`](cd000048.jpg) | G6 |
| [`cd000049.jpg`](cd000049.jpg) | G5 |
| [`cd000050.jpg`](cd000050.jpg) | G4 |
| [`cd000051.jpg`](cd000051.jpg) | G3 |
| [`cd000052.jpg`](cd000052.jpg) | G6 |
| [`cd000053.jpg`](cd000053.jpg) | G6 |
| [`cd000054.jpg`](cd000054.jpg) | G5 |
| [`cd000055.jpg`](cd000055.jpg) | G1 |
| [`cd000056.jpg`](cd000056.jpg) | G3 |
| [`cd000057.jpg`](cd000057.jpg) | G1 |
| [`cd000058.jpg`](cd000058.jpg) | G1 |
| [`cd000059.jpg`](cd000059.jpg) | G1 |
| [`cd000060.jpg`](cd000060.jpg) | G1 |
| [`cd000061.jpg`](cd000061.jpg) | G1 |
| [`cd000062.jpg`](cd000062.jpg) | G3 |
