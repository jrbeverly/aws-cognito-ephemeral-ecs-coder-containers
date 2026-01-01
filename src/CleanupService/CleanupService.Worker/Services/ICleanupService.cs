namespace CleanupService.Worker.Services;

public interface ICleanupService
{
    Task ExecuteCleanupAsync(CancellationToken cancellationToken);
}
