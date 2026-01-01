namespace ProvisioningService.Api.Models;

public record WorkspaceRecord
{
    public required string WorkspaceId { get; init; }
    public required string UserEmail { get; init; }
    public required string StackId { get; init; }
    public required string StackName { get; init; }
    public required WorkspaceStatus Status { get; init; }
    public string? WorkspaceUrl { get; init; }
    public string? AlbDnsName { get; init; }
    public string? EcsServiceName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
