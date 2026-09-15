# Plan de Proyecto: Recortador de Videos MP4 Sin Pérdida de Calidad (Lossless Video Cutter)

---

## 1. Fundamentos Técnicos: ¿Cómo recortar sin perder calidad?

En un archivo de video MP4 convencional (usando códecs como H.264/AVC o H.265/HEVC), el video no es una secuencia de imágenes completas consecutivas, sino una estructura de compresión basada en **GOP (Group of Pictures)**:

*   **I-Frames (Keyframes o fotogramas clave):** Imágenes completas de referencia, independientes.
*   **P-Frames y B-Frames:** Fotogramas dependientes que solo almacenan las diferencias con respecto a fotogramas anteriores o futuros.

### El Secreto del Recorte Sin Pérdida (*Stream Copy*)
Para recortar **sin perder un solo ápice de calidad** y de manera **instantánea**:
1.  **Modo Stream Copy (`-c copy`):** No se realiza recodificación (ni de audio ni de video). Los paquetes comprimidos simplemente se extraen y se reempaquetan en un nuevo contenedor MP4. La velocidad de corte está limitada únicamente por la velocidad del disco duro/SSD (típicamente toma menos de 1 segundo).
2.  **El reto de los Keyframes:**
    *   Si se corta exactamente en un I-Frame (Keyframe), el video comienza de inmediato de forma perfecta.
    *   Si se corta entre keyframes con `-c copy`, los primeros fotogramas pueden quedar congelados o negros hasta el siguiente keyframe.
3.  **Estrategias de Solución:**
    *   **Estrategia A (Recorte Clave / Keyframe Snap):** Alinear los puntos de corte al keyframe más cercano (rápido, 100% lossless).
    *   **Estrategia B (Smart Cut / Renderizado Inteligente):** Mantener el 99% del video en copia directa (`copy`) y únicamente recodificar el pequeño fragmento entre el punto de corte exacto y el siguiente keyframe.

### ¿Cómo añadir o manipular audio sin perder calidad de video?
Dentro de un archivo MP4, el video y el audio son flujos (*streams*) independientes que viajan encapsulados en el mismo contenedor:
*   **Video 100% Intacto (`-c:v copy`):** Modificar o añadir audio **no requiere recodificar el video**. El flujo de video se copia bit a bit (`stream copy`), preservando la resolución, bitrate y calidad original intacta.
*   **Modalidades de Audio soportadas:**
    1.  **Reemplazar Audio:** Descartar el audio original del video y colocar una nueva pista (ej. pista musical limpia en MP3/WAV/AAC).
    2.  **Mezclar Audio (Audio Mix):** Superponer una música de fondo o locución manteniendo el audio original de la grabación, permitiendo balancear el volumen de ambas pistas (`-filter_complex amix`).
    3.  **Añadir Pista Alternativa (Multi-Track):** Incrustar el nuevo audio como una pista seleccionable (Pista 1: Original, Pista 2: Nuevo audio).
    4.  **Alineación y Sincronización:** Recortar automáticamente el audio externo al tiempo de inicio (`-ss`) y duración del video recortado para que coincidan con exactitud.

---

## 2. Herramientas y Lenguajes Necesarios

### 2.1. Motor Central (Obligatorio)
*   **FFmpeg y FFprobe:**
    *   **FFmpeg:** Motor encargado del reempaquetado, demuxing/muxing y corte por stream copy (`-ss`, `-to`, `-c copy`).
    *   **FFprobe:** Herramienta para analizar metadatos del video, duración, pistas de audio, resolución y listar las posiciones exactas de los fotogramas clave (Keyframes/I-Frames).
    *   *Distribución:* Se puede instalar en el sistema vía `winget install Gyan.FFmpeg` o incluir una copia portátil (binarios `ffmpeg.exe` y `ffprobe.exe`) dentro de la carpeta del proyecto.

### 2.2. Lenguaje y Stack Tecnológico (Opciones Disponibles en tu Equipo)

El entorno del sistema cuenta actualmente con:
*   **Python 3.12.4** (Instalado y disponible en el sistema).
*   **Node.js 24.18** (Instalado y disponible en el sistema).
*   **.NET Runtime 8.0.8** (Instalado; para compilar código C# se requiere instalar el **.NET SDK 8**).

A continuación se presentan las alternativas de desarrollo:

| Alternativa | Stack / Librerías | Ventajas | Consideraciones |
| :--- | :--- | :--- | :--- |
| **Opción 1: C# (.NET 8 / WPF o Avalonia)** *(Sugerida por la ruta de trabajo `Cursos\Net`)* | C# 12, .NET 8/9, `CliWrap` o `FFMpegCore`, UI con WPF o Avalonia UI. | Alto rendimiento nativo en Windows, tipado fuerte, componentes de escritorio robustos. | Requiere instalar el **.NET SDK 8** (solo el runtime está presente). |
| **Opción 2: Python (Desktop / GUI Moderna)** | Python 3.12, `customtkinter` o `PyQt6` (Escritorio). | Entorno ya listo para usar, prototipado ultra rápido, fácil manipulación de procesos con `subprocess`. | Requiere instalar librerías con `pip`. |
| **Opción 3: Node.js / Electron o Web Local** | Node.js, Electron o Express + reproductor HTML5 nativo con visualizador de timeline. | Fácil creación de interfaces interactivas con timeline de video HTML5 (similar a LosslessCut). | Mayor consumo de memoria si se usa Electron. |

---

## 3. Arquitectura del Sistema

```
┌────────────────────────────────────────────────────────┐
│               Capa de Presentación (UI)                │
│    - Selector de archivo MP4                           │
│    - Selector de audio adicional (MP3/WAV/AAC/M4A)     │
│    - Reproductor de video (Previsualización)           │
│    - Controles de tiempo (Inicio / Fin)               │
│    - Opciones de audio: Conservar / Reemplazar / Mezclar│
│    - Barra de línea de tiempo (Timeline / Slider)      │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│             Capa de Lógica y Orquestación             │
│    - Validación de rangos de tiempo                    │
│    - Sincronización y alineación de pistas de audio    │
│    - Consulta de Keyframes con FFprobe                 │
│    - Generación de comandos FFmpeg correspondientes    │
└──────────────────────────┬─────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────┐
│               Capa de Ejecución FFmpeg                 │
│    - Recorte:  -c copy                                 │
│    - + Audio:  -c:v copy -c:a aac (o copy)             │
│    - Mezcla:   -filter_complex amix -c:v copy          │
└────────────────────────────────────────────────────────┘
```

---

## 4. Fases del Plan de Implementación

### Fase 1: Adquisición de Herramientas y Preparación ✅ (Completada)
1.  **FFmpeg y FFprobe:** Instalados vía winget (`Gyan.FFmpeg.Essentials 9.0.1`) y verificados en el entorno.
2.  **Stack tecnológico:** Seleccionado **C# (.NET 8)** con arquitectura limpia (Clean Architecture, SOLID) y WPF. SDK 8.0.425 instalado y configurado en PATH y DOTNET_ROOT.
3.  **Estructura del repositorio:** Solución `RecortadorDeVideos.sln` creada con proyectos `Domain`, `Application`, `Infrastructure`, `UI`, `Domain.UnitTests`, `Application.UnitTests` e `Infrastructure.IntegrationTests`.

### Fase 2: Módulo Core de Video y Audio (Lógica de Procesamiento) ✅ (Completada)
1.  **Análisis con `ffprobe` (`FFmpegMediaAnalyzer`):**
    *   Duración total, resolución, fps, códecs de audio y video.
    *   Extracción y parseo de Keyframes/I-Frames con soporte moderno para `pts_time` y manejo de metadatos SEI.
2.  **Operaciones con `ffmpeg` (`FFmpegVideoTrimmer` y `FFmpegAudioMuxer`):**
    *   Recorte 100% sin pérdida con Stream Copy (`-c:v copy`).
    *   Reemplazo de pista de audio por pista externa codificada a AAC.
    *   Mezcla de audio (*Audio Mix*) con control de balance de volumen (`amix`).
    *   Modo silenciado (`-an`).
3.  **Validación y Pruebas:**
    *   13 pruebas automatizadas pasando al 100% (7 unitarias + 6 de integración contra binarios reales de FFmpeg).

### Fase 3: Interfaz Gráfica de Usuario (GUI) 🚀 (Siguiente Fase a Continuar)
1.  **Carga de Medios:**
    *   Selector de archivo de video MP4 y soporte Drag & Drop.
    *   Selector opcional de archivo de audio secundario (`.mp3`, `.wav`, `.aac`, `.m4a`).
2.  **Previsualización:**
    *   Reproductor multimedia con barra de tiempo interactiva.
    *   Controles de reproducción, pausa y salto por tiempo.
3.  **Marcadores de Recorte y Configuración de Audio:**
    *   Atajos y botones "Fijar Inicio" (`[`) y "Fijar Fin" (`]`).
    *   Ajuste fino de tiempos (HH:mm:ss.fff).
    *   Selector de modo de audio: Conservar original, Silenciar, Reemplazar pista, Mezclar pistas con deslizador de volumen de fondo.
4.  **Exportación y Feedback en Vivo:**
    *   Selección de ruta y nombre de destino.
    *   Barra de progreso en tiempo real y mensajes de estado.

### Fase 4: Funcionalidades Avanzadas (Opcional / Futuro)
1.  **Efectos de audio:** Fundidos de entrada/salida (*Fade In* / *Fade Out*) en el audio añadido.
2.  **Recorte de múltiples fragmentos:** Cortar varias partes de un mismo video y unirlas sin recodificar (Concat demuxer).
3.  **Smart Cut:** Re-codificar únicamente los límites entre cortes para precisión a nivel de fotograma individual.
4.  **Extracción de audio independiente:** Guardar solo la pista de audio (MP3/AAC) del fragmento seleccionado sin exportar video.

---

## 5. Estado Actual del Proyecto y Próximo Paso

*   **Fase 1:** ✅ Completada.
*   **Fase 2:** ✅ Completada y testeada (13/13 pruebas exitosas).
*   **Fase activa para continuar:** 👉 **Fase 3 (Interfaz Gráfica de Usuario / GUI en WPF)**:
    *   Probar la ejecución interactiva de la UI.
    *   Validar la vinculación entre el reproductor de video (`MediaElement`), los deslizadores de tiempo y los comandos de corte.
