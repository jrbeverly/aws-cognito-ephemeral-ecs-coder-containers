using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

namespace ProvisioningService.Api.Data;

public class WorkspaceRepository : IWorkspaceRepository
{
    private readonly IAmazonDynamoDB _client;
    private readonly string _tableName;

    public WorkspaceRepository(IAmazonDynamoDB client, string tableName)
    {
        _client = client;
        _tableName = tableName;
    }

    public async Task<WorkspaceItem?> GetByUserEmailAsync(string userEmail)
    {
        var request = new GetItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"USER#{userEmail}" },
                ["SK"] = new() { S = "WORKSPACE" }
            }
        };

        var response = await _client.GetItemAsync(request);
        return response.Item is { Count: > 0 }
            ? WorkspaceItem.FromAttributes(response.Item)
            : null;
    }

    public async Task<WorkspaceItem?> GetByWorkspaceIdAsync(string workspaceId)
    {
        var request = new QueryRequest
        {
            TableName = _tableName,
            IndexName = "GSI1",
            KeyConditionExpression = "GSI1PK = :pk",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":pk"] = new() { S = $"WORKSPACE#{workspaceId}" }
            }
        };

        var response = await _client.QueryAsync(request);
        return response.Items?.FirstOrDefault() is { } item
            ? WorkspaceItem.FromAttributes(item)
            : null;
    }

    public async Task SaveAsync(WorkspaceItem item)
    {
        item.UpdatedAt = DateTime.UtcNow;

        var request = new PutItemRequest
        {
            TableName = _tableName,
            Item = item.ToAttributes()
        };

        await _client.PutItemAsync(request);
    }

    public async Task DeleteAsync(string userEmail)
    {
        var request = new DeleteItemRequest
        {
            TableName = _tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["PK"] = new() { S = $"USER#{userEmail}" },
                ["SK"] = new() { S = "WORKSPACE" }
            }
        };

        await _client.DeleteItemAsync(request);
    }
}
