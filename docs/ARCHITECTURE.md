# Documento de Arquitectura de Software: Recortador de Videos MP4

Este documento define la arquitectura de software, principios de diseño (SOLID y Clean Architecture), estructura de directorios y patrones técnicos para el proyecto **Recortador de Videos Sin Pérdida de Calidad**.

---

## 1. Principios de Diseño Fundamentales

El sistema se rige bajo los principios de **Clean Architecture (Robert C. Martin)** y **SOLID**:

### 1.1. Principios SOLID Aplicados
*   **S - Single Responsibility Principle (SRP):** Cada clase tiene una única responsabilidad bien acotada:
    *   `TimeRange`: Validar coherencia de tiempos (inicio < fin).
    *   `IMediaAnalyzer`: Consultar metadatos y keyframes.
    *   `IVideoTrimmer`: Construir y ejecutar el comando de recorte sin pérdida.
    *   `IAudioMuxer`: Orquestar la combinación, reemplazo o mezcla de audio.
*   **O - Open/Closed Principle (OCP):** El sistema está abierto a la extensión pero cerrado a la modificación.
    *   Nuevas estrategias de recorte (ej. `KeyframeSnapTrimmer`, `SmartCutTrimmer`) implementan `ITrimStrategy` sin modificar el código de la UI ni de la aplicación.
*   **L - Liskov Substitution Principle (LSP):** Cualquier implementación de un contrato (ej. un trimmer simulado en memoria para tests) puede sustituir a la implementación real de `FFmpegVideoTrimmer` sin alterar el comportamiento esperado del caso de uso.
*   **I - Interface Segregation Principle (ISP):** Interfaces pequeñas y específicas en lugar de una interfaz monolítica de multimedia:
    *   `IMediaAnalyzer` (solo lectura/análisis).
    *   `IVideoTrimmer` (solo corte).
    *   `IAudioMuxer` (solo mezcla y agregado de audio).
    *   `IProcessProgressReporter` (reporte de progreso desacoplado).
*   **D - Dependency Inversion Principle (DIP):** Las capas de alto nivel (Dominio y Casos de Uso) **no dependen de detalles de bajo nivel**. Dependen exclusivamente de abstracciones (interfaces). La capa de Infraestructura implementa esas interfaces.

---

## 2. Regla de Dependencia de Clean Architecture

```
                      ┌────────────────────────────────────────┐
                      │          4. Presentación / UI          │
                      │     (WPF / Avalonia / MVVM / CLI)      │
                      └───────────────────┬────────────────────┘
                                          │ depende de
                                          ▼
                      ┌────────────────────────────────────────┐
                      │         3. Casos de Uso (App)          │
                      │   (TrimVideoUseCase, AnalyzeVideo...)  │
                      └─────────┬──────────────────────┬───────┘
                     depende de │                      │ implementado por
                                ▼                      ▼
             ┌────────────────────────┐      ┌────────────────────────┐
             │       1. Dominio       │      │   2. Infraestructura   │
             │(Entities, ValueObjects,│◄─────┤(FFmpeg, ProcessRunner, │
             │   Domain Exceptions)   │  DIP │  File System, Windows) │
             └────────────────────────┘      └────────────────────────┘
```

*   **Regla de Oro:** Las dependencias apuntan **hacia adentro**.
*   El **Dominio** no referencia a ninguna otra capa ni paquete externo de terceros.
*   La **Aplicación** solo conoce al Dominio y declara las interfaces que necesita.
*   La **Infraestructura** conoce a la Aplicación y al Dominio para implementar dichas interfaces.
*   La **Presentación** conoce a la Aplicación (y Dominio) para invocar los casos de uso e inyectar dependencias en el arranque (`Composition Root`).

---

## 3. Estructura de Directorios (Caparazón del Proyecto)

```
recortadorDeVideos/
├── docs/                                  # Documentación del proyecto
│   ├── PLAN.md                            # Plan maestro de fases y requerimientos
│   └── ARCHITECTURE.md                    # Este documento de arquitectura
│
├── src/                                   # Código fuente de la solución
│   ├── RecortadorDeVideos.Domain/         # Capa 1: Núcleo de Dominio (Sin dependencias externas)
│   │   ├── Common/                        # Clases base (Entity, ValueObject, Result)
│   │   ├── Entities/                      # Entidades del negocio (VideoClip, ProjectSession)
│   │   ├── ValueObjects/                  # Objetos de valor inmutables (TimeRange, Resolution, Bitrate)
│   │   ├── Enums/                         # Enumeraciones (AudioMode, CutStrategy, VideoCodec)
│   │   └── Exceptions/                    # Excepciones de dominio (InvalidTimeRangeException, etc.)
│   │
│   ├── RecortadorDeVideos.Application/    # Capa 2: Casos de uso y contratos de orquestación
│   │   ├── Common/                        # Interfaces comunes, DTOs y Result Pattern
│   │   ├── Contracts/                     # Puertos de salida (Interfaces hacia infraestructura)
│   │   │   ├── IMediaAnalyzer.cs          # Contrato para análisis con FFprobe
│   │   │   ├── IVideoTrimmer.cs           # Contrato para corte de video sin pérdida
│   │   │   ├── IAudioMuxer.cs             # Contrato para agregado/reemplazo/mezcla de audio
│   │   │   ├── IProcessProgressReporter.cs# Contrato para emisión de progreso en tiempo real
│   │   │   └── IFileSystemService.cs      # Operaciones seguras sobre archivos y rutas
│   │   ├── Models/                        # DTOs de entrada y salida (VideoMetadataDto, TrimResultDto)
│   │   └── UseCases/                      # Casos de uso específicos (Command/Query style)
│   │       ├── AnalyzeVideo/              # Caso de uso: inspeccionar metadatos y keyframes
│   │       ├── TrimVideo/                 # Caso de uso: orquestar el corte sin pérdida
│   │       └── MuxAudio/                  # Caso de uso: incorporar o mezclar audio
│   │
│   ├── RecortadorDeVideos.Infrastructure/ # Capa 3: Implementaciones concretas de infraestructura
│   │   ├── FFmpeg/                        # Adaptador FFmpeg
│   │   │   ├── FFmpegBinaryLocator.cs     # Localizador / gestor del binario ffmpeg.exe y ffprobe.exe
│   │   │   ├── FFmpegProcessRunner.cs     # Ejecutor de procesos asíncronos con parsing de stderr
│   │   │   ├── FFmpegMediaAnalyzer.cs     # Implementación concreta de IMediaAnalyzer
│   │   │   ├── FFmpegVideoTrimmer.cs      # Implementación concreta de IVideoTrimmer (Stream Copy)
│   │   │   └── FFmpegAudioMuxer.cs        # Implementación concreta de IAudioMuxer (Mix/Replace)
│   │   ├── FileSystem/                    # Adaptador de sistema de archivos
│   │   │   └── LocalFileSystemService.cs  # Implementación de IFileSystemService
│   │   └── DependencyInjection.cs         # Registro de servicios de infraestructura
│   │
│   └── RecortadorDeVideos.UI/             # Capa 4: Presentación de Usuario (Desktop / MVVM)
│       ├── Assets/                        # Iconos, estilos y recursos gráficos
│       ├── ViewModels/                    # ViewModels (MainViewModel, VideoPlayerViewModel, AudioSettingsViewModel)
│       ├── Views/                         # Vistas / Ventanas (MainWindow, TimelineControl)
│       ├── Controls/                      # Controles de usuario personalizados (Timeline con marcadores)
│       ├── Services/                      # Servicios de UI (DialogService, FilePickerService)
│       └── App.xaml                       # Punto de entrada y Composition Root (IoC Container)
│
└── tests/                                 # Pruebas automatizadas
    ├── RecortadorDeVideos.Domain.UnitTests/         # Pruebas unitarias de entidades y value objects
    ├── RecortadorDeVideos.Application.UnitTests/    # Pruebas unitarias de casos de uso con Mocks
    └── RecortadorDeVideos.Infrastructure.IntegrationTests/ # Pruebas de integración con FFmpeg real
```

---

## 4. Descripción Detallada de Módulos y Responsabilidades

### 4.1. `RecortadorDeVideos.Domain`
*   **`TimeRange` (Value Object):**
    *   Representa un intervalo cerrado `[StartTime, EndTime]`.
    *   Regla de negocio: `StartTime >= TimeSpan.Zero` y `EndTime > StartTime`.
    *   Métodos: `Duration`, `Contains(TimeSpan time)`, `FormatFFmpeg()`.
*   **`AudioTrackConfig` (Entity / Value Object):**
    *   Modo: `KeepOriginal`, `Replace`, `Mix`, `Mute`.
    *   Ruta del archivo de audio secundario (opcional).
    *   Volúmenes porcentuales para mezcla (0.0 a 2.0).
    *   Duración y desplazamiento de inicio (*Offset*).
*   **`CutJob` (Aggregate Root):**
    *   Representa la tarea de corte completa: Video origen, rango de tiempo, configuración de audio y ruta de salida objetivo.

### 4.2. `RecortadorDeVideos.Application`
*   **Puertos (Interfaces):**
    *   Permiten a la aplicación orquestar el flujo sin saber cómo se comunica con `ffmpeg.exe`.
*   **Casos de Uso:**
    *   `AnalyzeVideoUseCase`: Recibe la ruta del MP4, solicita a `IMediaAnalyzer` la duración, resolución y lista de keyframes, retornando un `VideoMetadataDto`.
    *   `TrimVideoUseCase`: Recibe un `TrimVideoRequestDto`, valida el rango de tiempo con el dominio, verifica espacio en disco con `IFileSystemService`, solicita el corte a `IVideoTrimmer` o `IAudioMuxer`, reporta progreso mediante `IProgress<double>` y retorna un `TrimResultDto`.

### 4.3. `RecortadorDeVideos.Infrastructure`
*   **`FFmpegProcessRunner`:**
    *   Manejo de procesos con redirección asíncrona de `StandardError`.
    *   Lectura de líneas con formato `time=00:01:23.45` para calcular el porcentaje de avance en tiempo real.
    *   Cancelación limpia mediante `CancellationToken` (terminación controlada del subproceso).
*   **`FFmpegBinaryLocator`:**
    *   Busca `ffmpeg.exe` y `ffprobe.exe` en:
        1. Directorio de la aplicación (`./bin/runtimes/...`).
        2. Directorio configurado por el usuario.
        3. Variables de entorno del sistema (`PATH`).

### 4.4. `RecortadorDeVideos.UI`
*   **Patrón MVVM (Model-View-ViewModel):**
    *   **View:** XAML declarativo, sin lógica de negocio.
    *   **ViewModel:** Contiene comandos (`ICommand`/`RelayCommand`), propiedades observables (`INotifyPropertyChanged`) y llama a los Casos de Uso.
    *   **Data Binding:** Enlace bidireccional entre la posición del video y los marcadores de recorte.

---

## 5. Estrategia de Manejo de Errores y Resultados

Se utilizará el patrón **Result<T>** en la capa de Aplicación para evitar el uso excesivo de excepciones en el flujo normal:

```csharp
public class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorMessage { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
```

---

## 6. Integración y Dependencias entre Proyectos

```
RecortadorDeVideos.UI ──────────► RecortadorDeVideos.Application ──► RecortadorDeVideos.Domain
        │                                      ▲
        │                                      │
        └─────────────► RecortadorDeVideos.Infrastructure
```
*   `UI` registra los tipos concretos de `Infrastructure` en el contenedor IoC (`ServiceCollection`).
*   `UI` solo interactúa con los Casos de Uso y ViewModels.
*   `Application` define los contratos que `Infrastructure` satisface.
*   `Domain` permanece puro, libre de librerías externas de terceros.
