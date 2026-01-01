using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using CleanupService.Worker.Data;
using FluentAssertions;
using Moq;

namespace CleanupService.Tests.Data;

public class WorkspaceRepositoryTests
{
    private readonly Mock<IAmazonDynamoDB> _client = new();
    private const string TableName = "workspace-lookup-test";

    private WorkspaceRepository CreateSut() => new(_client.Object, TableName);

    [Fact]
    public async Task ScanAllAsync_PaginatesCorrectly()
    {
        var callCount = 0;
        _client.Setup(c => c.ScanAsync(It.IsAny<ScanRequest>(), default))
            .ReturnsAsync((ScanRequest r, CancellationToken _) =>
            {
                callCount++;
                return callCount switch
                {
                    1 => new ScanResponse
                    {
                        Items = [ItemAttributes("workspace-1"), ItemAttributes("workspace-2")],
                        LastEvaluatedKey = new() { ["PK"] = new() { S = "p2" } }
                    },
                    2 => new ScanResponse
                    {
                        Items = [ItemAttributes("workspace-3")],
                        LastEvaluatedKey = []
                    },
                    _ => new ScanResponse { Items = [], LastEvaluatedKey = [] }
                };
            });

        var sut = CreateSut();
        var result = await sut.ScanAllAsync();

        result.Should().HaveCount(3);
        result.Select(i => i.WorkspaceId).Should().Equal("workspace-1", "workspace-2", "workspace-3");
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ScanAllAsync_HandlesEmptyTable()
    {
        _client.Setup(c => c.ScanAsync(It.IsAny<ScanRequest>(), default))
            .ReturnsAsync(new ScanResponse { Items = [], LastEvaluatedKey = [] });

        var sut = CreateSut();
        var result = await sut.ScanAllAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_UsesConditionalExpression()
    {
        var item = CreateItem(workspaceId: "test-ws");

        var sut = CreateSut();
        await sut.DeleteAsync(item);

        _client.Verify(c => c.DeleteItemAsync(
            It.Is<DeleteItemRequest>(r =>
                r.TableName == TableName &&
                r.ConditionExpression == "WorkspaceId = :wid" &&
                r.ExpressionAttributeValues[":wid"].S == "test-ws"),
            default), Times.Once);
    }

    private static WorkspaceItem CreateItem(string workspaceId = "workspace-1")
    {
        return new WorkspaceItem
        {
            PK = $"USER#user-{workspaceId}",
            SK = "WORKSPACE",
            WorkspaceId = workspaceId,
            UserEmail = $"user-{workspaceId}@example.com",
            StackId = $"stack-{workspaceId}",
            StackName = $"workspace-{workspaceId}",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static Dictionary<string, AttributeValue> ItemAttributes(string workspaceId)
    {
        return new()
        {
            ["PK"] = new() { S = $"USER#user-{workspaceId}" },
            ["SK"] = new() { S = "WORKSPACE" },
            ["WorkspaceId"] = new() { S = workspaceId },
            ["UserEmail"] = new() { S = $"user-{workspaceId}@example.com" },
            ["Status"] = new() { S = "active" },
            ["CreatedAt"] = new() { S = DateTime.UtcNow.ToString("O") },
            ["UpdatedAt"] = new() { S = DateTime.UtcNow.ToString("O") }
        };
    }
}
