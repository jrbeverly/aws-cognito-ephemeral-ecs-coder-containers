using System.Text.Json;
using Amazon.DynamoDBv2.Model;
using ProvisioningService.Api.Models;

namespace ProvisioningService.Api.Data;

public class WorkspaceItem
{
    public string PK { get; set; } = null!;
    public string SK { get; set; } = "WORKSPACE";
    public string EntityType { get; set; } = "Workspace";
    public string WorkspaceId { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    public string? StackId { get; set; }
    public string? StackName { get; set; }
    public string Status { get; set; } = "provisioning";
    public string? WorkspaceUrl { get; set; }
    public string? AlbDnsName { get; set; }
    public string? EcsServiceName { get; set; }
    public string? GSI1PK { get; set; }
    public string? GSI1SK { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static WorkspaceItem FromRecord(WorkspaceRecord record) => new()
    {
        PK = $"USER#{record.UserEmail}",
        SK = "WORKSPACE",
        WorkspaceId = record.WorkspaceId,
        UserEmail = record.UserEmail,
        StackId = record.StackId,
        StackName = record.StackName,
        Status = record.Status.ToString().ToLowerInvariant(),
        WorkspaceUrl = record.WorkspaceUrl,
        AlbDnsName = record.AlbDnsName,
        EcsServiceName = record.EcsServiceName,
        GSI1PK = $"WORKSPACE#{record.WorkspaceId}",
        GSI1SK = $"USER#{record.UserEmail}",
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt
    };

    public WorkspaceRecord ToRecord() => new()
    {
        WorkspaceId = WorkspaceId,
        UserEmail = UserEmail,
        StackId = StackId ?? string.Empty,
        StackName = StackName ?? string.Empty,
        Status = Enum.Parse<WorkspaceStatus>(Status, ignoreCase: true),
        WorkspaceUrl = WorkspaceUrl,
        AlbDnsName = AlbDnsName,
        EcsServiceName = EcsServiceName,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };

    public Dictionary<string, AttributeValue> ToAttributes()
    {
        var attributes = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new() { S = PK },
            ["SK"] = new() { S = SK },
            ["EntityType"] = new() { S = EntityType },
            ["WorkspaceId"] = new() { S = WorkspaceId },
            ["UserEmail"] = new() { S = UserEmail },
            ["Status"] = new() { S = Status },
            ["CreatedAt"] = new() { S = CreatedAt.ToString("O") },
            ["UpdatedAt"] = new() { S = UpdatedAt.ToString("O") }
        };

        if (StackId != null)
            attributes["StackId"] = new() { S = StackId };
        if (StackName != null)
            attributes["StackName"] = new() { S = StackName };
        if (WorkspaceUrl != null)
            attributes["WorkspaceUrl"] = new() { S = WorkspaceUrl };
        if (AlbDnsName != null)
            attributes["AlbDnsName"] = new() { S = AlbDnsName };
        if (EcsServiceName != null)
            attributes["EcsServiceName"] = new() { S = EcsServiceName };
        if (GSI1PK != null)
            attributes["GSI1PK"] = new() { S = GSI1PK };
        if (GSI1SK != null)
            attributes["GSI1SK"] = new() { S = GSI1SK };

        return attributes;
    }

    public static WorkspaceItem FromAttributes(Dictionary<string, AttributeValue> attributes)
    {
        return new WorkspaceItem
        {
            PK = attributes["PK"].S,
            SK = attributes["SK"].S,
            EntityType = attributes.GetValueOrDefault("EntityType")?.S ?? "Workspace",
            WorkspaceId = attributes["WorkspaceId"].S,
            UserEmail = attributes["UserEmail"].S,
            StackId = attributes.GetValueOrDefault("StackId")?.S,
            StackName = attributes.GetValueOrDefault("StackName")?.S,
            Status = attributes["Status"].S,
            WorkspaceUrl = attributes.GetValueOrDefault("WorkspaceUrl")?.S,
            AlbDnsName = attributes.GetValueOrDefault("AlbDnsName")?.S,
            EcsServiceName = attributes.GetValueOrDefault("EcsServiceName")?.S,
            GSI1PK = attributes.GetValueOrDefault("GSI1PK")?.S,
            GSI1SK = attributes.GetValueOrDefault("GSI1SK")?.S,
            CreatedAt = DateTime.Parse(attributes["CreatedAt"].S),
            UpdatedAt = DateTime.Parse(attributes["UpdatedAt"].S)
        };
    }
}
