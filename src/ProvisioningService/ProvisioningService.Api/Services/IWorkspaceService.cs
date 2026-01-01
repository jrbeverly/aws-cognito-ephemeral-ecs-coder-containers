using ProvisioningService.Api.Models;

namespace ProvisioningService.Api.Services;

public interface IWorkspaceService
{
    Task<WorkspaceRecord> CreateWorkspaceAsync(string userEmail);
    Task<WorkspaceRecord?> GetWorkspaceAsync(string userEmail);
    Task DeleteWorkspaceAsync(string userEmail);
}
