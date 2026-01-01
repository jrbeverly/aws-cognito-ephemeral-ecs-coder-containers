using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace CleanupService.Worker.Data;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;

    public WorkspaceRepository(IAmazonDynamoDB client, string tableName)
    {
        _client = client;
        _tableName = tableName;
    }

    public async Task<List<WorkspaceItem>> ScanAllAsync()
    {
        var items = new List<WorkspaceItem>();
        var request = new ScanRequest { TableName = _tableName };

        ScanResponse response;
        do
        {
            response = await _client.ScanAsync(request);
            items.AddRange((response.Items ?? []).Select(WorkspaceItem.FromAttributes));
            request.ExclusiveStartKey = response.LastEvaluatedKey;
        } while (response.LastEvaluatedKey is { Count: > 0 });

        return items;
    }

    public async Task SaveAsync(WorkspaceItem item)
    {
        item.UpdatedAt = DateTime.UtcNow;

        var attributes = new Dictionary<string, AttributeValue>
        {
            ["PK"] = new() { S = item.PK },
            ["SK"] = new() { S = item.SK },
            ["WorkspaceId"] = new() { S = item.WorkspaceId },
            ["UserEmail"] = new() { S = item.UserEmail },
            ["Status"] = new() { S = item.Status },
            ["CreatedAt"] = new() { S = item.CreatedAt.ToString("O") },
            ["UpdatedAt"] = new() { S = item.UpdatedAt.ToString("O") }
        };

        if (item.StackId != null)
            attributes["StackId"] = new() { S = item.StackId };
        if (item.StackName != null)
            attributes["StackName"] = new() { S = item.StackName };

        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = _tableName,
            Item = attributes
        });
    }

    public async Task DeleteAsync(WorkspaceItem item)
    {
        await _client.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = item.PK },
                ["SK"] = new() { S = item.SK }
            },
            ConditionExpression = "WorkspaceId = :wid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":wid"] = new() { S = item.WorkspaceId }
            }
        });
    }
}
