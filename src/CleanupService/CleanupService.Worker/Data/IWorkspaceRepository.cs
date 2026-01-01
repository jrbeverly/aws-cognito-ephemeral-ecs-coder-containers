namespace CleanupService.Worker.Data;

public interface IWorkspaceRepository
{
    Task<List<WorkspaceItem>> ScanAllAsync();
    Task SaveAsync(WorkspaceItem item);
    Task DeleteAsync(WorkspaceItem item);
}
