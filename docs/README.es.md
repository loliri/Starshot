<div align="center">

<img src="../src/logo.png" width="120" alt="Logo de Starshot">

# Starshot

**Herramienta de captura de pantalla HDR nativa de nueva generación para Windows**

**Next-generation Windows-native HDR Screenshot Tool**

Captura 16bit de pipeline completo · Captura de región · Codificación AVIF / JPEG XL · Gestión del color

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](https://github.com/loliri/Starshot?tab=MIT-1-ov-file)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../../releases)

[Descargar](../../../releases) · [Inicio rápido](#inicio-rápido) · [Funcionalidades](#funcionalidades) · [Compilar desde el código fuente](#compilar-desde-el-código-fuente)

**[English](../README.md)** | **[简体中文](README.zh-CN.md)** | **[繁體中文](README.zh-TW.md)** | **[日本語](README.ja.md)** | **[Français](README.fr.md)** | **[Русский](README.ru.md)** | **Español**

</div>

---

## Por qué Starshot

Las herramientas de captura integradas de Windows (Snipping Tool, Win+Shift+S) solo producen imágenes SDR de 8 bits incluso en pantallas HDR: el compositor del sistema tritura los fotogramas HDR de 16 bits, recortando las altas luces y estrechando la gama de colores. Las herramientas de captura de terceros más comunes (ShareX, etc.) también están limitadas por el pipeline tradicional GDI/BitBlt y no pueden acceder a los datos HDR.

Starshot captura directamente el framebuffer bruto `R16G16B16A16Float` scRGB desde la capa DXGI, preservando completamente la información de luminancia HDR (hasta miles de nits). Las capturas se codifican como AVIF o JPEG XL HDR de 16 bits con metadatos de espacio de color BT.2020 y función de transferencia PQ. También ofrece degradación automática para pantallas SDR, captura de región, conversión por lotes multiformato y todo lo que se espera de una herramienta de captura de propósito general.

**Características principales**

- 🎯 **Pipeline HDR completo sin pérdidas**: captura, codificación y gestión del color en 16 bits de principio a fin. Sin mapeo tonal con pérdidas.
- 🧠 **Detección inteligente HDR/SDR**: el histograma maxCLL distingue el contenido HDR real del contenido SDR envuelto en un formato HDR.
- ✂️ **Captura de región**: superposición multimonitor con fotograma congelado, detección de ventanas y lupa para selección precisa al píxel.
- 📋 **Portapapeles nativo**: API nativa de Win32 escribe directamente en el portapapeles, evitando los fallos de pegado por renderizado diferido de WinRT.
- 🗂️ **Soporte multiformato**: AVIF / JPEG XL / UHDR JPEG / PNG, incluyendo herramienta de conversión por lotes.
- 📦 **Portable**: extraer y ejecutar. Sin instalación, sin privilegios de administrador.

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**Otras herramientas**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="Captura SDR mostrando altas luces recortadas y colores deslavados">
</td>
<td align="center" width="50%">

**Starshot (Ultra HDR JPEG)**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Captura Starshot Ultra HDR JPEG preservando el detalle completo de altas luces mediante gain map">
</td>
</tr>
</table>
<sub>Imágenes de *Arknights: Endfield*</sub>
</div>
</br>

> [!NOTE]
> Dado que GitHub no admite la renderización AVIF, la comparación anterior usa Ultra HDR JPEG. La imagen AVIF original puede verse [aquí](https://r2.cialo.site/endfield/3840x2160.dlaa.avif).

En pantallas SDR, Starshot usa automáticamente la ruta de captura SDR estándar y funciona como una herramienta de captura de propósito general. En pantallas HDR, es una de las pocas soluciones de captura de escritorio capaces de preservar completamente los datos HDR.

## Requisitos del sistema

- Windows 10 / 11; se recomienda Windows 11 para la mejor experiencia
- Arquitectura x64
- **Se requiere una pantalla HDR para la captura HDR** (en pantallas SDR se usa automáticamente la ruta SDR)

## Descarga

Descarga el archivo desde [Releases](../../../releases), extráelo y ejecuta `Starshot.exe` desde el directorio raíz. No necesita instalación: solo extraer y ejecutar.

## Capturas de pantalla

![Screenshot](Screenshot.jpg)

## Inicio rápido

| Acción                                                                          | Atajo por defecto |
| ------------------------------------------------------------------------------- | ----------------- |
| Captura de pantalla completa                                                    | Alt+W             |
| Captura de región (guardar archivo + copiar al portapapeles tras seleccionar)   | Alt+Q             |
| Solo copiar región (copiar al portapapeles sin guardar archivo)                 | Alt+A             |

Todos los atajos se pueden personalizar en la configuración.

## Funcionalidades

### Pipeline de captura HDR

La mayoría de las herramientas de captura solo obtienen SDR de 8 bits incluso en pantallas HDR: el fotograma scRGB de punto flotante de 16 bits del compositor del sistema se comprime a SDR con altas luces recortadas y gama reducida. Starshot captura el **framebuffer HDR bruto**:

1. **Captura HDR**: cuando la pantalla informa modo HDR, solicita el formato de píxel `R16G16B16A16Float` para obtener los datos scRGB de punto flotante completos (luminancia de hasta miles de nits)
2. **Guardado HDR**: AVIF / JPEG XL de 16 bits, espacio de color BT.2020 + función de transferencia PQ. Las altas luces no se recortan, la gama no se reduce
3. **Cálculo de maxCLL**: el efecto de histograma de Win2D calcula la luminancia máxima del contenido, usada para distinguir el contenido HDR real del contenido SDR en contenedor HDR
4. **Gestión del color**: lee el perfil ICC de la pantalla para extraer los primarios de gama reales, escribe los chunks cICP/ICC en el archivo. HDR fuerza BT.2020; la gestión de color SDR se puede activar/desactivar (activado = leer gama real del ICC, desactivado = BT.709)

#### Manejo de contenido SDR

En una pantalla HDR, el escritorio y las aplicaciones SDR también se capturan en formato HDR (R16G16B16A16Float), pero la luminancia real del contenido está a nivel SDR. Starshot lo maneja así:

- **Por defecto**: se guarda igualmente en formato HDR (16 bits), **sin mapeo tonal de 8 bits**, evitando degradación y cambios de color
- **Opción Eliminar HDR para contenido SDR** (opcional): al activarla, el contenido por debajo del umbral maxCLL se convierte automáticamente a SDR (según el formato de guardado SDR configurado) y se elimina el archivo HDR para ahorrar espacio

#### Respaldo UHDR JPEG

Las capturas HDR pueden producir simultáneamente un Ultra HDR JPEG (imagen base SDR + gain map HDR), que se muestra correctamente incluso en software sin soporte HDR. Codificado mediante `UhdrEncoder` de `Starward.Codec`.

#### Compromiso HDR en la captura de región

La superposición de captura de región **intencionadamente** hace mapeo tonal de fotogramas HDR a SDR para su visualización, porque `CanvasControl` de WinUI usa una cadena de intercambio SDR, y la salida directa de punto flotante scRGB aparecería con colores alterados u oscurecida. **El archivo guardado es HDR completo**, intacto; la compresión de altas luces durante la selección solo afecta la vista previa, nunca la salida.

### Tres modos de captura

| Modo                   | Objetivo                                                | Formato del portapapeles | ¿Guarda archivo? |
| ---------------------- | ------------------------------------------------------- | ------------------------ | ---------------- |
| Pantalla completa      | Monitor completo (ventana en primer plano / pantalla del cursor, conmutable) | CF_HDROP (archivo)       | Sí              |
| Región                 | Selección rectangular / clic en ventana                 | CF_DIB (mapa de bits BGRA) | Sí              |
| Solo copiar región     | Selección rectangular / clic en ventana                 | CF_DIB (mapa de bits BGRA) | No              |

Los tres modos comparten la misma detección HDR, gestión del color, plantillas de nombre de archivo, pipeline de guardado y notificación informativa.

### Superposición de captura de región

- **Fotograma congelado**: primero captura todos los monitores en un solo mapa de bits compuesto; la superposición muestra este fotograma congelado para que la imagen permanezca fija durante la selección. La superposición en sí queda excluida de la captura.
- **Multimonitor**: cubre toda la pantalla virtual. La lupa y la caja de coordenadas están limitadas al monitor donde se encuentra el cursor (sin desbordamiento entre pantallas).
- **Detección de ventanas**: EnumWindows + filtrado DWM cloaked/toolwindow + bordes extendidos DWM (eliminación de sombras) + doble candidato de área cliente + selección por orden Z. Haz clic en una ventana para capturarla directamente (QuickCrop).
- **Lupa**: alineación entera NearestNeighbor + cuadrícula de píxeles (15×15 píxeles, 10px cada uno), haciendo que los píxeles individuales sean claramente distinguibles.
- **Línea de selección animada + coordenadas en tiempo real**: X/Y/An/Al de la selección + coordenadas físicas del cursor.
- **Precisión al píxel**: arrastre de selección +1px; rectángulo de ventana +0.
- Esc / clic derecho para cancelar; Enter para confirmar la ventana bajo el cursor.

### Portapapeles

El `Clipboard.SetContent` de WinRT en aplicaciones WinUI no empaquetadas no es fiable (renderizado diferido + problemas de Flush, el contenido a menudo no llega a otras aplicaciones). Starshot usa directamente las API nativas de Win32 (`OpenClipboard` / `SetClipboardData`):

- **Captura de pantalla completa**: CF_HDROP (formato de arrastrar y soltar archivo). Pega en el Explorador o en una aplicación de chat para obtener el archivo directamente.
- **Captura de región**: CF_DIB (mapa de bits BGRA). El mapa de bits SDR recortado de la superposición se coloca directamente en el portapapeles, sin lectura de archivo, sin recodificación, sin segundo mapeo tonal.
- Se puede llamar desde cualquier hilo, con 10×20 ms de reintento para manejar la contención del portapapeles.

### Guardado

- **Estructura plana** (sin subcarpetas). Por defecto `Imágenes\Starshot`, personalizable.
- **Formato SDR** (PNG / AVIF / JPEG XL; por defecto PNG) y **formato HDR** (AVIF / JPEG XL; por defecto AVIF) configurados por separado.
- Niveles de calidad: Medio / Alto / Sin pérdidas.
- Metadatos XMP (CreatorTool = Starshot).
- Codificación serializada (SemaphoreSlim) para evitar conflictos de codificación concurrente.
- **Estadísticas de almacenamiento**: la página de configuración muestra el espacio en disco usado por capturas / caché de miniaturas / fondos de pantalla / registros / copias de seguridad, con actualización y limpieza de caché en un clic (también limpia archivos huérfanos de fondos de pantalla).

#### Formatos admitidos

| Formato    | Profundidad de bits             | Soporte HDR                         | Caso de uso                          |
| ---------- | ------------------------------- | ----------------------------------- | ------------------------------------ |
| PNG        | 8 bits / 16 bits                | Se puede guardar pero mala compatibilidad | SDR por defecto, sin pérdidas       |
| AVIF       | 8 bits / 10 bits / 12 bits      | HDR completo                        | HDR por defecto, alta compresión     |
| JPEG XL    | 8 bits / 16 bits                | HDR completo                        | Alternativa HDR, compresión reversible |
| UHDR JPEG  | 8 bits + gain map               | Respaldo HDR compatible con SDR     | Salida HDR adicional                 |

### Plantillas de nombre de archivo

Las capturas de pantalla completa y de región usan **plantillas independientes**.

| Marcador                                                    | Significado                                       | Ejemplo             |
| ----------------------------------------------------------- | ------------------------------------------------- | ------------------- |
| `{process}`                                                 | Nombre del proceso (sin extensión)                | `explorer`          |
| `{processPath}`                                             | Nombre del archivo exe (con extensión)            | `explorer.exe`      |
| `{title}`                                                   | Título de la ventana (trim + longitud máxima configurable) | `Genshin Impact`    |
| `{timestamp}`                                               | Marca de tiempo Unix                              | `1721234567`        |
| `{time}`                                                    | yyyyMMdd_HHmmssff                                 | `20260718_14302512` |
| `{date}`                                                    | yyyyMMdd                                          | `20260718`          |
| `{width}` `{height}`                                        | Dimensiones de la imagen (px)                     | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}`   | Componentes de fecha/hora                         |                     |

Los caracteres no válidos en nombres de archivo se reemplazan uniformemente por `_`.

### Notificación informativa

Después de una captura, aparece una miniatura + notificación de estado (no interfiere con las capturas: tiene `WDA_EXCLUDEFROMCAPTURE` establecido, otras herramientas de captura no pueden capturar esta ventana):

- **Procesando** (animación giratoria) / **Guardado** (con botón Abrir) / **Copiado** (marca verde) / **Error**
- Contador de disparos en ráfaga (ej. 2/3)
- Animaciones de deslizamiento Composition (entrada/salida)

### Biblioteca de capturas

- Navegación por múltiples carpetas (directorio de capturas por defecto + carpetas añadidas por el usuario)
- `FileSystemWatcher` para detección en tiempo real de adiciones/eliminaciones
- Agrupado por fecha, carga diferida de miniaturas
- Menú contextual: Abrir / Copiar archivo / Copiar como JPG / Abrir en Explorador / Abrir con / Eliminar
- Selección múltiple + arrastrar fuera + punto de entrada de conversión por lotes

### Visor de imágenes

- Zoom (deslizador / botones / rueda del ratón / doble clic para ajustar), modo pantalla completa (F11)
- Anterior / Siguiente (teclas de flecha, rueda del ratón, tira de miniaturas inferior)
- Arrastrar y soltar archivos para abrirlos directamente
- Menú contextual: Copiar archivo / ruta / imagen, Eliminar, Abrir en Explorador, Abrir con
- **Panel de edición**: cambio de modo de visualización HDR / SDR / Auto, deslizador de brillo SDR (100–500 nits), información de imagen y pantalla
- **Conversión de formato**: exportar como PNG / AVIF / JPEG XL (pantalla SDR) o UHDR JPEG / AVIF / JPEG XL (pantalla HDR)
- **Gestión del color**: lee el perfil ICC de la pantalla y AdvancedColorInfo

### Conversión por lotes de formato

| Dirección de conversión              | Motor                                 |
| ------------------------------------ | ------------------------------------- |
| JPG / PNG → AVIF / JXL               | avifenc.exe / cjxl.exe (CLI)          |
| AVIF / JXL → JPG / PNG               | avifdec.exe / djxl.exe (CLI)          |
| JXR / WEBP / HEIC etc. → AVIF / JXL  | ImageSaver en proceso (avifEncoderLite) |

### Personalización

- **Fondo de pantalla personalizado**: tres modos
  - **Imagen específica**: elige una imagen, se muestra fija
  - **Video específico**: reproducción en bucle silenciada; se pausa automáticamente al ocultar la ventana principal
  - **Aleatorio desde carpeta**: elige una imagen o video aleatorio de una carpeta en cada inicio
  - Detección automática de pérdida de fuente del fondo, limpieza de configuración y vuelta a sin fondo + notificación toast
- **Color de acento**:
  - **Extracción automática del fondo** (activado por defecto): muestrea el color dominante del fondo como color de acento de la aplicación (aumento de saturación HSV). Para videos, solo se muestrea el primer fotograma para evitar parpadeos.
  - **Color personalizado**: el selector de color manual anula la extracción automática
- **Tema**: Seguir el sistema / Claro / Oscuro
- **Efecto acrílico**: en modo fondo de pantalla, se puede elegir entre capa de vidrio esmerilado o transparencia directa del fondo

### Pantalla de inicio

Muestra el logo + eslogan al iniciar. Retraso de 700 ms y luego se desvanece en 400 ms. Solo se activa en la primera apertura de la ventana; no se reproduce al restaurar desde la bandeja del sistema.

### Bandeja del sistema

- Clic izquierdo muestra la ventana principal, clic derecho abre el menú contextual (Mostrar / Salir)
- Cerrar la ventana principal minimiza a la bandeja (se puede desactivar)
- El mecanismo `ForceExit` garantiza que "Salir" desde la bandeja realmente cierre la aplicación

### Inicio automático

- Clave de registro `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, apuntando al lanzador (`Starshot.exe` en la raíz)
- Opción `--hide` para iniciar minimizado en la bandeja (requiere que la bandeja esté activada)
- El interruptor lee el registro en tiempo real (sin caché en base de datos): deshabilitar desde el Administrador de tareas solo modifica StartupApproved sin eliminar la entrada Run, el interruptor sigue mostrando Activado
- Al iniciar, verifica si el exe apuntado por la entrada de inicio automático existe; si no, elimina automáticamente la entrada y muestra una notificación toast

### Buscar actualizaciones

- Verificación limitada al inicio (≥24h + interruptor activado) de la última versión en GitHub Releases, o verificación manual desde la página Acerca de
- Las actualizaciones usan descompresión por streaming real de SharpCompress (conexión directa al flujo de red, sin guardar zip en disco). Cada entrada se escribe directamente en el directorio raíz. En caso de error, se restaura el estado anterior. En caso de éxito, reinicia el lanzador con `--cleanup-old` para limpiar versiones antiguas
- Solo verifica releases CI/CD (lee el número de versión de `version.ini`). Las compilaciones locales no tienen número de versión (`AppVersion = Local`) y no activan la verificación

## Arquitectura

### Estructura de directorios

```
Raíz/
  Starshot.exe            ← Lanzador C++ (lee version.ini para decidir qué directorio app lanzar)
  StarshotDatabase.db     ← Base de datos SQLite de configuración
  version.ini             ← Número de versión (solo releases CI/CD; ausente en compilaciones locales)
  app-{version}/          ← Directorio del programa principal (versionado para releases CI/CD, app/ para compilaciones locales)
    Starshot.exe          ← Programa principal (WinUI 3 / .NET 10)
    *.dll                 ← Dependencias
    avifenc.exe etc.      ← Herramientas de códec (de Starward.Codec NuGet)
  backup/                 ← Copias de seguridad de la base de datos
%LOCALAPPDATA%/Starshot/  (por defecto, configurable)
  log/                    ← Registros
  bg/                     ← Fondos de pantalla
  thumb/                  ← Caché de miniaturas
```

### Lanzador

Programa nativo en C++ (~400 KB). Lee `version.ini` para decidir si lanzar `app-{version}/Starshot.exe` (si no hay version.ini, usa `app/` para compilaciones debug/local). Al ejecutarse con `--cleanup-old`, recorre los directorios `app-*` y elimina los que no corresponden a la versión actual.

### Bandeja e inicio en segundo plano

- `--hide`: al autoiniciar, no se crea MainWindow. Los atajos globales se registran en el hwnd de SystemTrayWindow (la ventana de la bandeja sirve como host persistente)
- El TaskbarIcon de H.NotifyIcon.WinUI requiere un Window.Show para activar `Loaded` antes de registrar el icono. Durante la inicialización, `WS_EX_LAYERED + alpha=0` hace que la ventana complete este Show de forma transparente, evitando un destello visible en el autoinicio con `--hide`
- El lanzador C++ recombina `argv[1..]` para transmitir los argumentos de línea de comandos

### Stack tecnológico

| Capa                     | Tecnología                                                              |
| ------------------------ | ----------------------------------------------------------------------- |
| Framework UI             | WinUI 3 (Windows App SDK 1.8)                                           |
| Tiempo de ejecución      | .NET 10                                                                 |
| Gráficos                 | Win2D 1.3 (interoperabilidad D3D11, mapeo tonal HDR, efectos de histograma) |
| Códecs                   | Starward.Codec NuGet (wrapper P/Invoke de libavif / libjxl / UltraHDR)  |
| Almacenamiento de datos  | SQLite + Dapper                                                         |
| Registro (logs)          | Serilog                                                                 |
| Bandeja del sistema      | H.NotifyIcon.WinUI                                                      |
| Miniaturas               | Scighost.WinUI ImageEx + CachedImage personalizado                      |
| Superposición de región  | Win2D CanvasControl (renderizado de fotograma congelado + dibujo de selección) |
| Portapapeles             | API nativa Win32 (OpenClipboard / SetClipboardData)                     |
| Lanzador                 | C++ nativo (toolset v145, CRT estático)                                 |

### Protección contra reentrada

Guarda global `Interlocked.CompareExchange`. Los modos de pantalla completa, región y solo copia comparten una única bandera `_isCapturing`: las pulsaciones rápidas repetidas o los atajos consecutivos no disparan múltiples capturas.

### Configuración de compilación

|                       | Debug                                          | Release                                                                                                |
| --------------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| .NET Runtime          | Framework-dependent (no autocontenido)          | Autocontenido                                                                                          |
| Librerías nativas     | Solo win-x64 (RuntimeIdentifier, plano en la raíz de salida) | Igual que Debug                                                                                        |
| Trim                  | No                                             | Parcial                                                                                                |
| ReadyToRun            | No                                             | Sí                                                                                                     |
| Limpieza adicional    | —                                              | Elimina DirectML.dll / onnxruntime.dll / NpuDetect (componentes WinML/AI del Windows App SDK, no usados) |
| Ruta de salida        | `build/app/`                                   | `build/release/app/` + lanzador copiado a `build/release/`                                             |
| Tamaño                | ~80 MB                                         | Más pequeño (Trim + eliminación de librerías IA)                                                       |

## Compilar desde el código fuente

### Requisitos previos

- Visual Studio 2022 / 2026 (con Desarrollo de escritorio C++ y Desarrollo de escritorio .NET)
- .NET 10 SDK
- Windows SDK 10.0.26100

### Pasos

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# Compilar el programa principal (salida en build/app/)
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# Compilar el lanzador (salida en build/Starshot.exe; requiere MSBuild de VS)
"C:\Program Files\Microsoft Visual Studio\<versión>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# Ejecutar: build/Starshot.exe (lanzador) o build/app/Starshot.exe (programa principal)

# === Publicación Release ===
# 1. Primero compilar el lanzador (salida en build/Starshot.exe)
"C:\Program Files\Microsoft Visual Studio\<versión>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. Publicar el programa principal (salida en build/release/app/, copia automática del lanzador a build/release/Starshot.exe + elimina librerías IA)
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# Estructura resultante:
# build/release/
#   Starshot.exe        ← Lanzador (copiado automáticamente)
#   app/
#     Starshot.exe      ← Programa principal (autocontenido + trim + R2R)
#     *.dll / avifenc.exe etc.
```

## Limitaciones conocidas

- La superposición de captura de región muestra fotogramas HDR como SDR (CanvasControl de WinUI usa una cadena de intercambio SDR); los archivos guardados no se ven afectados
- Los fondos de pantalla personalizados usan `UniformToFill` para cubrir la ventana, pero el recorte de WinUI no está centrado, actualmente está alineado **arriba a la izquierda**. Por ejemplo, un fondo estrecho (vertical) en una ventana ancha solo mostrará la parte superior (recortado desde arriba en lugar de centrado)
- Al abrir la superposición de captura de región, el cursor mantiene la forma predeterminada del sistema. **Hay que mover el ratón una vez** para que aparezca el cursor en cruz (`ProtectedCursor` de WinUI no surte efecto inmediato sobre un puntero inmóvil que ya está sobre el elemento; al moverlo una vez se activa un evento pointer, tras lo cual funciona normalmente)

## Notas de desarrollo

Este proyecto está en fase de desarrollo activo. Las funcionalidades pueden cambiar en cualquier momento. ¡Mantente atento a las actualizaciones!

Contribuciones bienvenidas:

- ¿Encontraste un bug? [Abre un Issue](../../../issues/new)
- ¿Tienes una sugerencia? [Inicia una discusión](../../../issues/new)
- ¿Quieres contribuir con código? Envía un [Pull Request](../../../pulls)

## Preguntas frecuentes

<details>
<summary><b>Las imágenes en la biblioteca de capturas (página de inicio) muestran colores incorrectos / distorsionados</b></summary>

Esto suele ser un problema de los códecs de imagen del sistema Windows (extensiones AVIF / HEIF / JPEG XL), no un bug de Starshot. Intenta buscar y actualizar los siguientes componentes en Microsoft Store:

- **AV1 Video Extension**
- **HEIF Image Extensions**
- **HEVC Video Extensions**
- **Webp Image Extensions**

Reinicia Starshot después de actualizar. Si el problema persiste, [abre un Issue](../../../issues/new) adjuntando una captura de pantalla.

</details>

<details>
<summary><b>Los colores de la captura se ven diferentes a lo que veo en pantalla</b></summary>

Si estás usando una pantalla HDR, asegúrate de que el interruptor HDR de Windows esté activado (Configuración → Sistema → Pantalla → HDR). La función de captura HDR solo funciona en modo HDR.

</details>

<details>
<summary><b>No puedo pegar desde el portapapeles después de hacer una captura</b></summary>

Starshot usa la API nativa de Win32 para escribir en el portapapeles, lo que en teoría es más fiable que WinRT. Si aún así falla el pegado, puede que la aplicación destino no admita el formato de portapapeles correspondiente (CF_HDROP para archivos / CF_DIB para mapas de bits). Prueba a pegar en el Explorador (archivos) o Paint (mapas de bits) para verificar.

</details>

## Agradecimientos

- [Starward](https://github.com/Scighost/Starward) — El núcleo de captura, el motor de códecs y el framework de ventanas provienen de Starward, desarrollado por [@Scighost](https://github.com/Scighost)
- [ShareX](https://github.com/ShareX/ShareX) — Referencia para la detección de ventanas y el diseño de interacción de la superposición de captura de región

**Y todas las bibliotecas de terceros utilizadas**:

- [CommunityToolkit](https://github.com/CommunityToolkit) — Framework MVVM + controles WinUI (Segmented / Behaviors / Helpers)
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — Descompresión por streaming
- [Dapper](https://github.com/DapperLib/Dapper) — ORM ligero para SQLite
- [H.NotifyIcon.WinUI](https://github.com/HavenDV/H.NotifyIcon) — Bandeja del sistema
- [Vanara.PInvoke](https://github.com/dahall/Vanara) — Wrappers de API Win32 (DwmApi / Ole / Shell32)
- [ComputeSharp.D2D1](https://github.com/Sergio0694/ComputeSharp) — Efectos de cómputo GPU
- [Serilog](https://github.com/serilog/serilog) — Registro estructurado

## License

MIT
