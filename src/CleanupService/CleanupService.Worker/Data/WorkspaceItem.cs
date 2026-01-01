namespace CleanupService.Worker.Data;

public class WorkspaceItem
{
    public string PK { get; set; } = null!;
    public string SK { get; set; } = "WORKSPACE";
    public string WorkspaceId { get; set; } = null!;
    public string UserEmail { get; set; } = null!;
    public string? StackId { get; set; }
    public string? StackName { get; set; }
    public string Status { get; set; } = "provisioning";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static WorkspaceItem FromAttributes(Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue> attributes)
    {
        return new WorkspaceItem
        {
            PK = attributes["PK"].S,
            SK = attributes["SK"].S,
            WorkspaceId = attributes["WorkspaceId"].S,
            UserEmail = attributes["UserEmail"].S,
            StackId = attributes.GetValueOrDefault("StackId")?.S,
            StackName = attributes.GetValueOrDefault("StackName")?.S,
            Status = attributes["Status"].S,
            CreatedAt = DateTime.Parse(attributes["CreatedAt"].S),
            UpdatedAt = DateTime.Parse(attributes["UpdatedAt"].S)
        };
    }
}
