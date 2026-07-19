<div align="center">

<img src="../src/logo.png" width="120" alt="Logo de Starshot">

# Starshot

**Herramienta de captura de pantalla HDR nativa de Windows de nueva generación**

Captura de tubería completa de 16 bits · Captura de región · Codificación AVIF / JPEG XL · Gestión del color

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../releases)

[Descargar](../../releases) · [Inicio rápido](#inicio-rápido) · [Características](#características) · [Compilar desde el código fuente](#compilar-desde-el-código-fuente)

**[简体中文](../README.md)** | **[English](README.en.md)** | **[繁體中文](README.zh-TW.md)** | **[日本語](README.ja.md)** | **[Français](README.fr.md)** | **[Русский](README.ru.md)** | **Español**

</div>

---

## Por qué Starshot

Las herramientas de captura integradas de Windows (Recortes, Win+Shift+S) solo pueden capturar imágenes SDR de 8 bits incluso en pantallas HDR: el compositor del sistema comprime el framebuffer HDR de 16 bits, recortando las altas luces y reduciendo la gama de colores. Las herramientas de terceros más comunes (ShareX, etc.) también están limitadas por el pipeline de captura tradicional GDI/BitBlt y no pueden acceder a los datos HDR.

Starshot adquiere directamente el framebuffer scRGB `R16G16B16A16Float` sin procesar de la salida de pantalla desde la capa DXGI, preservando completamente la información de luminancia HDR (hasta miles de nits), y codifica como AVIF o JPEG XL HDR de 16 bits con metadatos de espacio de color BT.2020 + función de transferencia PQ. También ofrece todas las funciones que se esperan de una herramienta de captura de pantalla de propósito general: degradación automática para pantalla SDR, captura de región, conversión por lotes multi-formato y más.

**Características principales**

- 🎯 **Pipeline HDR completo sin pérdidas**: 16 bits en toda la captura, codificación y gestión del color; sin mapeo tonal con pérdidas
- 🧠 **Detección inteligente HDR/SDR**: el histograma maxCLL distingue el contenido HDR real del contenido SDR envuelto en formato HDR
- ✂️ **Captura de región**: superposición multi-pantalla con congelación de fotograma, detección de ventanas + lupa para una selección precisa
- 📋 **Portapapeles nativo**: la API nativa de Win32 escribe directamente en el portapapeles, evitando fallos de pegado por renderizado diferido de WinRT
- 🗂️ **Soporte multi-formato**: AVIF / JPEG XL / UHDR JPEG / PNG, con herramienta de conversión por lotes
- 📦 **Portable**: extraer y ejecutar, sin necesidad de privilegios de administrador

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**Otras herramientas**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="Captura SDR mostrando altas luces recortadas y colores deslavados">
</td>
<td align="center" width="50%">

**Starshot (Ultra HDR JPEG)**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Starshot Ultra HDR JPEG preservando todos los detalles de altas luces mediante mapa de ganancia">
</td>
</tr>
</table>
<sub>Imágenes de *Arknights: Endfield*</sub>
</div>
</br>

> [!NOTE]
> Se muestra Ultra HDR JPEG porque GitHub no soporta la renderización AVIF. El AVIF original se puede ver [aquí](https://r2.cialo.site/endfield/3840x2160.dlaa.avif).

En pantallas SDR, Starshot utiliza automáticamente la ruta de captura SDR estándar como herramienta de captura de propósito general; en pantallas HDR, es una de las pocas soluciones de captura de escritorio que preserva completamente los datos HDR.

## Requisitos del sistema

- Se recomienda Windows 11 para la mejor experiencia
- Arquitectura x64
- **La captura HDR requiere una pantalla HDR** (en pantallas SDR se usa automáticamente la ruta SDR)

## Descarga

Descarga el archivo desde [Releases](../../releases), extráelo y ejecuta `Starshot.exe` en el directorio raíz (el lanzador inicia automáticamente el programa principal en `app/`). No requiere instalación: solo extraer y ejecutar.

## Inicio rápido

| Acción                                                                                       | Atajo predeterminado |
| -------------------------------------------------------------------------------------------- | -------------------- |
| Captura de pantalla completa                                                                 | Alt+W                |
| Captura de región (guardar archivo + copiar al portapapeles)                                 | Alt+Q                |
| Solo copia de región (copiar al portapapeles, sin guardar archivo)                           | Alt+A                |

Todos los atajos se pueden personalizar en la configuración.

## Características

### Pipeline de captura HDR

La mayoría de las herramientas de captura solo pueden capturar SDR de 8 bits incluso en pantallas HDR: el framebuffer scRGB de 16 bits en coma flotante del compositor del sistema se aplasta a SDR, recortando las altas luces y reduciendo la gama de colores. Starshot captura el **framebuffer HDR sin procesar**:

1. **Captura HDR**: Cuando la pantalla informa HDR, solicita el formato de píxel `R16G16B16A16Float` para obtener los datos scRGB completos en coma flotante (luminancia de hasta miles de nits)
2. **Guardado HDR**: AVIF / JPEG XL de 16 bits con gama BT.2020 + función de transferencia PQ. Sin recorte de altas luces, sin reducción de gama
3. **Cálculo maxCLL**: El efecto de histograma Win2D calcula el nivel máximo de luminancia del contenido para distinguir el contenido HDR real del contenido SDR en formato HDR
4. **Gestión del color**: Lee el perfil ICC de la pantalla para analizar los primarios reales de la gama e incrusta los chunks cICP/ICC en los archivos de salida. HDR forzado a BT.2020; SDR conmutable (activado = leer gama real ICC, desactivado = BT.709)

#### Tratamiento del contenido SDR

En pantallas HDR, el escritorio y las aplicaciones SDR también se capturan en formato HDR (R16G16B16A16Float), pero la luminancia real del contenido está a nivel SDR. Starshot lo maneja de la siguiente manera:

- **Por defecto**: Se guarda en formato HDR (16 bits), **sin mapeo tonal de 8 bits**, evitando degradación y cambios de color
- **Opción "Eliminar HDR para contenido SDR"** (opcional): Al activarla, detecta el umbral maxCLL; si el contenido está por debajo, lo convierte automáticamente a SDR (usando el formato de guardado SDR configurado por el usuario) y elimina el archivo HDR, ahorrando espacio

#### Respaldo UHDR JPEG

Las capturas HDR también pueden producir un Ultra HDR JPEG (imagen base SDR + mapa de ganancia HDR), que se muestra correctamente incluso en software sin soporte HDR. Codificado mediante el `UhdrEncoder` de `Starward.Codec`.

#### Compromiso HDR en la captura de región

La superposición de captura de región mapea tonalmente **intencionadamente** los fotogramas HDR a SDR para su visualización, porque el `CanvasControl` de WinUI usa una cadena de intercambio SDR, y la salida directa en coma flotante scRGB aparecería descolorida o negra. **Los archivos guardados son HDR completo**, sin modificar; el recorte de altas luces durante la selección solo afecta a la vista previa, no al resultado.

### Tres modos de captura

| Modo                  | Objetivo                                              | Formato de portapapeles  | Archivo guardado |
| --------------------- | ----------------------------------------------------- | ------------------------ | ---------------- |
| Pantalla completa     | Toda la pantalla (ventana activa / pantalla del cursor, conmutable) | CF_HDROP (archivo)       | Sí               |
| Región                | Selección por arrastre / clic en ventana              | CF_DIB (mapa de bits BGRA) | Sí             |
| Solo copia de región  | Selección por arrastre / clic en ventana              | CF_DIB (mapa de bits BGRA) | No             |

Los tres modos comparten detección HDR, gestión del color, plantillas de nombre de archivo, pipeline de guardado y notificación emergente de información.

### Superposición de captura de región

- **Congelación de fotograma**: Primero captura todas las pantallas en un solo mapa de bits; la superposición muestra un fotograma congelado: la pantalla no se mueve al seleccionar, y la superposición en sí no aparece en la captura
- **Multi-pantalla**: Cubre toda la pantalla virtual; la lupa y la caja de coordenadas están limitadas a la pantalla del cursor (sin cruce entre pantallas)
- **Detección de ventanas**: EnumWindows + filtrado DWM cloaked/toolwindow + límites de marco extendido DWM (eliminación de sombras) + doble candidato de área de cliente + selección por orden Z; clic en una ventana para capturarla directamente (QuickCrop)
- **Lupa**: Alineación entera NearestNeighbor + cuadrícula de píxeles (15×15 píxeles, 10px cada uno), píxeles claramente distinguibles
- **Hormigas animadas + Coordenadas en vivo**: X/Y/An/Al de la selección + coordenadas físicas del cursor
- **Precisión de píxeles**: Selección por arrastre +1px, rectángulo de ventana +0
- ESC / Clic derecho para cancelar, Enter para confirmar ventana bajo el cursor

### Portapapeles

La llamada WinRT `Clipboard.SetContent` en aplicaciones WinUI no empaquetadas no es fiable (renderizado diferido + problemas de Flush, el contenido a menudo no llega a otras aplicaciones). Starshot usa directamente las API nativas de Win32 (`OpenClipboard` / `SetClipboardData`):

- **Pantalla completa**: CF_HDROP (formato de arrastrar y soltar archivos): pega en el Explorador o aplicaciones de chat y obtén directamente un archivo
- **Región**: CF_DIB (mapa de bits BGRA): el mapa de bits SDR recortado de la superposición va directamente al portapapeles, sin lectura de archivo, sin recodificación, sin segundo mapeo tonal
- Invocable desde cualquier hilo, 10×20ms de reintentos cuando el portapapeles está bloqueado

### Guardado

- **Estructura plana** (sin subcarpetas), por defecto `Imágenes\Starshot`, personalizable
- **Formato SDR** (PNG / AVIF / JPEG XL, predeterminado PNG) y **Formato HDR** (AVIF / JPEG XL, predeterminado AVIF) configurados independientemente
- Calidad: Media / Alta / Sin pérdidas
- Metadatos XMP (CreatorTool = Starshot)
- Codificación serializada (SemaphoreSlim) para evitar conflictos de codificación concurrente
- **Estadísticas de almacenamiento**: La página de configuración muestra el uso de disco de capturas / caché de miniaturas / fondos de pantalla / registros, con actualización y limpieza de caché en un clic (también limpia archivos huérfanos de fondos de pantalla)

#### Formatos compatibles

| Formato   | Profundidad de bits               | Soporte HDR                              | Caso de uso                          |
| --------- | --------------------------------- | ---------------------------------------- | ------------------------------------ |
| PNG       | 8 bits / 16 bits                  | Puede guardar HDR pero baja compatibilidad | SDR predeterminado, sin pérdidas     |
| AVIF      | 8 bits / 10 bits / 12 bits        | HDR completo                             | HDR predeterminado, alta compresión  |
| JPEG XL   | 8 bits / 16 bits                  | HDR completo                             | Alternativa HDR, compresión reversible |
| UHDR JPEG | 8 bits + mapa de ganancia         | Respaldo HDR compatible con SDR          | Salida HDR adicional                 |

### Plantillas de nombre de archivo

Las capturas de pantalla completa y de región usan **plantillas independientes**.

| Marcador de posición                                       | Significado                                   | Ejemplo             |
| ---------------------------------------------------------- | --------------------------------------------- | ------------------- |
| `{process}`                                                | Nombre del proceso (sin extensión)            | `explorer`          |
| `{processPath}`                                            | Nombre del archivo exe (con extensión)        | `explorer.exe`      |
| `{title}`                                                  | Título de la ventana (recortado, longitud configurable) | `Genshin Impact` |
| `{timestamp}`                                              | Marca de tiempo Unix                          | `1721234567`        |
| `{time}`                                                   | yyyyMMdd_HHmmssff                             | `20260718_14302512` |
| `{date}`                                                   | yyyyMMdd                                      | `20260718`          |
| `{width}` `{height}`                                       | Dimensiones de la imagen (px)                 | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}`  | Componentes individuales de la hora           |                     |

Los caracteres no válidos en nombres de archivo se reemplazan uniformemente por `_`.

### Notificación emergente de información

Después de una captura, aparece una miniatura + notificación de estado (no afecta a las capturas — configurado con `WDA_EXCLUDEFROMCAPTURE`, otras herramientas de captura no pueden ver esta ventana):

- **Procesando** (animación giratoria) / **Guardado** (con botón abrir) / **Copiado** (marca verde) / **Error**
- Contador de capturas múltiples (ej. 2/3)
- Animación Composition de deslizamiento de entrada/salida

### Biblioteca de capturas

- Navegación por múltiples carpetas (directorio de capturas predeterminado + carpetas añadidas por el usuario)
- `FileSystemWatcher` detecta adiciones/eliminaciones en tiempo real
- Agrupado por fecha, miniaturas con carga diferida
- Menú contextual: Abrir / Copiar archivo / Copiar como JPG / Abrir en el Explorador / Abrir con / Eliminar
- Selección múltiple + arrastrar fuera + entrada de conversión por lotes

### Visor de imágenes

- Zoom (deslizador / botones / rueda del ratón / doble clic para ajustar), modo pantalla completa (F11)
- Anterior / Siguiente (teclas de flecha, rueda del ratón, tira de miniaturas inferior)
- Arrastrar y soltar archivos para abrir
- Menú contextual: Copiar archivo / ruta / imagen, Eliminar, Abrir en el Explorador, Abrir con
- **Panel de edición**: Cambio de modo de visualización HDR / SDR / Auto, deslizador de brillo SDR (100–500 nits), información de imagen y pantalla
- **Conversión de formato**: Exportar a PNG / AVIF / JPEG XL (pantalla SDR) o UHDR JPEG / AVIF / JPEG XL (pantalla HDR)
- **Gestión del color**: Lee el perfil ICC de la pantalla y AdvancedColorInfo

### Conversión por lotes de formato

| Dirección de conversión               | Motor                                   |
| ------------------------------------- | --------------------------------------- |
| JPG / PNG → AVIF / JXL                | avifenc.exe / cjxl.exe (CLI)            |
| AVIF / JXL → JPG / PNG                | avifdec.exe / djxl.exe (CLI)            |
| JXR / WEBP / HEIC etc. → AVIF / JXL   | ImageSaver en proceso (avifEncoderLite) |

### Personalización

- **Fondo de pantalla personalizado**: Tres modos
  - **Imagen fija**: Elige una imagen, se muestra fija
  - **Video fijo**: Reproducción en bucle silenciada, pausa automática cuando la ventana principal está oculta
  - **Carpeta aleatoria**: En cada inicio, selecciona aleatoriamente una imagen o video de una carpeta
  - Detección automática de fuentes de fondo de pantalla faltantes, limpieza de configuración y vuelta a sin fondo + notificación emergente
- **Color de acento**:
  - **Extracción automática del fondo de pantalla** (activado por defecto): Muestrea el color dominante del fondo de pantalla como color de acento de la aplicación (aumento de saturación HSV); los videos solo muestrean el primer fotograma para evitar parpadeos de color
  - **Color personalizado**: El selector de color manual anula la extracción automática
- **Tema**: Seguir el sistema / Claro / Oscuro
- **Efecto acrílico**: En modo fondo de pantalla, elige entre capa de vidrio esmerilado o transparencia directa del fondo

### Pantalla de presentación

Muestra el logotipo + eslogan al inicio, con un retraso de 700 ms seguido de un desvanecimiento de 400 ms. Se activa solo en la primera apertura de ventana; no se reproduce al restaurar desde la bandeja.

### Bandeja del sistema

- Clic izquierdo muestra la ventana principal, clic derecho muestra el menú contextual (Mostrar / Salir)
- Cerrar la ventana principal minimiza a la bandeja (conmutable)
- El mecanismo `ForceExit` garantiza que "Salir" desde la bandeja realmente cierre la aplicación

### Inicio automático

- Registro `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, apuntando al lanzador (raíz `Starshot.exe`)
- Bandera opcional `--hide` para iniciar minimizado en la bandeja (requiere bandeja activada)

## Arquitectura

### Estructura de directorios

```
Raíz/
  Starshot.exe            ← Lanzador C++ (~400KB, inicia el programa principal en app/)
  StarshotDatabase.db     ← Base de datos SQLite de configuración
  app/
    Starshot.exe          ← Programa principal (WinUI 3 / .NET 10)
    *.dll                 ← Dependencias
    avifenc.exe etc.      ← Herramientas de códec (de Starward.Codec NuGet)
%LOCALAPPDATA%/Starshot/  (predeterminado, configurable)
  log/                    ← Registros
  cache/                  ← Caché de miniaturas
```

### Lanzador

Programa nativo C++ (~400KB), actualmente codificado para `app/Starshot.exe`. En el futuro se planea soporte para `version.ini` con directorios versionados + limpieza automática de versiones antiguas.

### Stack tecnológico

| Capa                     | Tecnología                                                               |
| ------------------------ | ------------------------------------------------------------------------ |
| Framework UI             | WinUI 3 (Windows App SDK 1.8)                                            |
| Entorno de ejecución     | .NET 10                                                                  |
| Gráficos                 | Win2D 1.3 (interoperabilidad D3D11, mapeo tonal HDR, efecto de histograma) |
| Códec                    | Starward.Codec NuGet (wrapper P/Invoke libavif / libjxl / UltraHDR)      |
| Almacenamiento de datos  | SQLite + Dapper                                                          |
| Registro                 | Serilog                                                                  |
| Bandeja del sistema      | H.NotifyIcon.WinUI                                                       |
| Miniaturas               | Scighost.WinUI ImageEx + CachedImage personalizado                       |
| Superposición de región  | Win2D CanvasControl (renderizado de fotograma congelado + dibujo de selección) |
| Portapapeles             | API nativa Win32 (OpenClipboard / SetClipboardData)                      |
| Lanzador                 | C++ nativo (toolset v145, CRT estático)                                  |

### Protección contra reentrada

Guardia global `Interlocked.CompareExchange`: los modos de pantalla completa, región y solo copia comparten una única bandera `_isCapturing`, evitando múltiples capturas por pulsaciones rápidas de teclas o atajos consecutivos.

### Configuración de compilación

|                          | Debug                                       | Release                                                                                                  |
| ------------------------ | ------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Entorno .NET             | Framework-dependent (no empaquetado)        | Autocontenido                                                                                            |
| Bibliotecas nativas      | Solo win-x64 (RuntimeIdentifier, plano en raíz de salida) | Igual que Debug                                                                          |
| Trim                     | No                                          | Parcial                                                                                                  |
| ReadyToRun               | No                                          | Sí                                                                                                       |
| Limpieza adicional       | —                                           | Eliminar DirectML.dll / onnxruntime.dll / NpuDetect (componentes WinML/AI de Windows App SDK, no usados) |
| Ruta de salida           | `build/app/`                                | `build/release/app/` + lanzador copiado a `build/release/`                                               |
| Tamaño                   | ~80MB                                       | Más pequeño (Trim + eliminación de bibliotecas AI)                                                       |

## Compilar desde el código fuente

### Requisitos previos

- Visual Studio 2022 / 2026 (con Desarrollo de escritorio en C++ y Desarrollo de escritorio .NET)
- .NET 10 SDK
- Windows SDK 10.0.26100

### Pasos

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# Compilar el programa principal (salida en build/app/)
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# Compilar el lanzador (salida en build/Starshot.exe, requiere MSBuild de VS)
"C:\Program Files\Microsoft Visual Studio\<versión>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# Ejecutar: build/Starshot.exe (lanzador) o build/app/Starshot.exe (programa principal)

# === Publicación Release ===
# 1. Primero compilar el lanzador (salida en build/Starshot.exe)
"C:\Program Files\Microsoft Visual Studio\<versión>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. Publicar el programa principal (salida en build/release/app/, copia automática del lanzador a build/release/Starshot.exe + elimina bibliotecas AI)
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# Estructura de directorios resultante:
# build/release/
#   Starshot.exe        ← Lanzador (copiado automáticamente)
#   app/
#     Starshot.exe      ← Programa principal (autocontenido + trim + R2R)
#     *.dll / avifenc.exe etc.
```

## Limitaciones conocidas

- La superposición de captura de región muestra los fotogramas HDR como SDR (WinUI CanvasControl usa cadena de intercambio SDR); los archivos guardados no se ven afectados
- El fondo de pantalla personalizado usa relleno `UniformToFill`, pero el recorte de WinUI no está centrado — actualmente está alineado **arriba a la izquierda**, por ejemplo, un fondo de pantalla estrecho (vertical) en una ventana ancha solo mostrará la parte superior (recortado desde arriba, no desde el centro)
- Al abrir la superposición de captura de región, el cursor mantiene la forma predeterminada del sistema; **mueve el ratón una vez** para que aparezca el cursor en cruz (WinUI `ProtectedCursor` no se aplica inmediatamente a un puntero estacionario que ya está sobre el elemento — moverlo una vez activa un evento de puntero, tras lo cual funciona normalmente)
- Sin gestión de versiones / actualización automática todavía

## Agradecimientos

- [Starward](https://github.com/Scighost/Starward) — El núcleo de captura, el motor de códec y el marco de ventana derivan de Starward, desarrollado por [@Scighost](https://github.com/Scighost)
- [ShareX](https://github.com/ShareX/ShareX) — Referencia para la detección de ventanas y el diseño de interacción de la superposición de captura de región

## Licencia

MIT
