using RecortadorDeVideos.Application.Contracts;
using RecortadorDeVideos.Application.Models;
using RecortadorDeVideos.Domain.Common;
using RecortadorDeVideos.Domain.Entities;

namespace RecortadorDeVideos.Application.UseCases.ChangeSpeed;

public class ChangeSpeedUseCase : IChangeSpeedUseCase
{
    private readonly IVideoSpeedChanger _speedChanger;
    private readonly IFileSystemService _fileSystemService;

    public ChangeSpeedUseCase(
        IVideoSpeedChanger speedChanger,
        IFileSystemService fileSystemService)
    {
        _speedChanger = speedChanger ?? throw new ArgumentNullException(nameof(speedChanger));
        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
    }

    public async Task<Result<ChangeSpeedResponseDto>> ExecuteAsync(
        ChangeSpeedRequestDto request,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
            return Result<ChangeSpeedResponseDto>.Failure("La solicitud no puede ser nula.");

        if (!_fileSystemService.FileExists(request.SourceVideoPath))
            return Result<ChangeSpeedResponseDto>.Failure($"El video de origen no existe: {request.SourceVideoPath}");

        if (request.SpeedMultiplier <= 0)
            return Result<ChangeSpeedResponseDto>.Failure("El multiplicador de velocidad debe ser mayor a cero.");

        if (request.OriginalDuration <= TimeSpan.Zero)
            return Result<ChangeSpeedResponseDto>.Failure("La duración original del video debe ser mayor a cero.");

        if (string.Equals(request.SourceVideoPath, request.DestinationVideoPath, StringComparison.OrdinalIgnoreCase))
            return Result<ChangeSpeedResponseDto>.Failure("El archivo de destino no puede ser el mismo archivo de origen.");

        SpeedJob job;
        try
        {
            job = new SpeedJob(
                request.SourceVideoPath,
                request.DestinationVideoPath,
                request.SpeedMultiplier,
                request.OriginalDuration,
                request.MuteAudio);
        }
        catch (Exception ex)
        {
            return Result<ChangeSpeedResponseDto>.Failure(ex.Message);
        }

        var executionResult = await _speedChanger.ChangeSpeedAsync(job, progress, cancellationToken);
        if (executionResult.IsFailure)
        {
            return Result<ChangeSpeedResponseDto>.Failure(executionResult.ErrorMessage!);
        }

        long outputSize = _fileSystemService.FileExists(job.DestinationVideoPath)
            ? _fileSystemService.GetFileSize(job.DestinationVideoPath)
            : 0;

        var response = new ChangeSpeedResponseDto(
            Success: true,
            OutputFilePath: job.DestinationVideoPath,
            NewDuration: job.NewDuration,
            OutputSizeBytes: outputSize);

        return Result<ChangeSpeedResponseDto>.Success(response);
    }
}
