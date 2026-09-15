using Microsoft.Extensions.DependencyInjection;
using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.UseCases.AnalyzeVideo;
using RecortadorDeVideos.Application.UseCases.TrimVideo;
using RecortadorDeVideos.Infrastructure.FFmpeg;
using RecortadorDeVideos.Infrastructure.FileSystem;

namespace RecortadorDeVideos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRecortadorServices(this IServiceCollection services)
    {
        // Infraestructura - Sistema de Archivos
        services.AddSingleton<IFileSystemService, LocalFileSystemService>();

        // Infraestructura - FFmpeg
        services.AddSingleton<IFFmpegBinaryLocator, FFmpegBinaryLocator>();
        services.AddTransient<IFFmpegProcessRunner, FFmpegProcessRunner>();
        services.AddTransient<IMediaAnalyzer, FFmpegMediaAnalyzer>();
        services.AddTransient<IVideoTrimmer, FFmpegVideoTrimmer>();
        services.AddTransient<IAudioMuxer, FFmpegAudioMuxer>();

        // Aplicación - Casos de Uso
        services.AddTransient<IAnalyzeVideoUseCase, AnalyzeVideoUseCase>();
        services.AddTransient<ITrimVideoUseCase, TrimVideoUseCase>();

        return services;
    }
}
