namespace ProvisioningService.Api.Models;

public record WorkspaceResponse
{
    public required string WorkspaceId { get; init; }
    public required string UserEmail { get; init; }
    public required string Status { get; init; }
    public string? WorkspaceUrl { get; init; }
    public string? AlbDnsName { get; init; }
    public string? EcsServiceName { get; init; }
    public string? StackName { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static WorkspaceResponse FromRecord(WorkspaceRecord record) => new()
    {
        WorkspaceId = record.WorkspaceId,
        UserEmail = record.UserEmail,
        Status = record.Status.ToString().ToLowerInvariant(),
        WorkspaceUrl = record.WorkspaceUrl,
        AlbDnsName = record.AlbDnsName,
        EcsServiceName = record.EcsServiceName,
        StackName = record.StackName,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };
}
