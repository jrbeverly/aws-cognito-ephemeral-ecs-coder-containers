namespace ProvisioningService.Api.Data;

public interface IWorkspaceRepository
{
    Task<WorkspaceItem?> GetByUserEmailAsync(string userEmail);
    Task<WorkspaceItem?> GetByWorkspaceIdAsync(string workspaceId);
    Task SaveAsync(WorkspaceItem item);
    Task DeleteAsync(string userEmail);
}
