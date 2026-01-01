using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Amazon.DynamoDBv2.Model;
using CleanupService.Worker.Data;
using CleanupService.Worker.Models;
using CleanupService.Worker.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace CleanupService.Tests.Services;

public class WorkspaceCleanupServiceTests
{
    private readonly Mock<IWorkspaceRepository> _repository = new();
    private readonly Mock<IAmazonCloudFormation> _cloudFormation = new();
    private readonly Mock<ILogger<WorkspaceCleanupService>> _logger = new();
    private readonly CleanupPolicyOptions _options = new()
    {
        MaxAgeHours = 8,
        ProvisioningTimeoutMinutes = 30,
        DeletingTimeoutMinutes = 60,
        FailedCleanupTimeoutMinutes = 60
    };

    private WorkspaceCleanupService CreateSut() =>
        new(_repository.Object, _cloudFormation.Object, Options.Create(_options), _logger.Object);

    [Fact]
    public async Task ExecuteCleanupAsync_ExpiredActiveWorkspace_IsCleaned()
    {
        var item = CreateItem("active", createdAgo: TimeSpan.FromHours(9));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackExists(item.StackName!, exists: true);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(
            It.Is<DeleteStackRequest>(r => r.StackName == item.StackName), default), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_ValidActiveWorkspace_IsSkipped()
    {
        var item = CreateItem("active", createdAgo: TimeSpan.FromHours(1));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(It.IsAny<DeleteStackRequest>(), default), Times.Never);
        _repository.Verify(r => r.DeleteAsync(It.IsAny<WorkspaceItem>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_StuckProvisioning_IsCleaned()
    {
        var item = CreateItem("provisioning", updatedAgo: TimeSpan.FromMinutes(45));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackExists(item.StackName!, exists: true);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(
            It.Is<DeleteStackRequest>(r => r.StackName == item.StackName), default), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_StuckDeleting_IsCleaned()
    {
        var item = CreateItem("deleting", updatedAgo: TimeSpan.FromMinutes(90));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackExists(item.StackName!, exists: true);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(
            It.Is<DeleteStackRequest>(r => r.StackName == item.StackName), default), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_StaleFailed_IsCleaned()
    {
        var item = CreateItem("failed", updatedAgo: TimeSpan.FromMinutes(90));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackExists(item.StackName!, exists: true);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(
            It.Is<DeleteStackRequest>(r => r.StackName == item.StackName), default), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_NonExistentStack_CleansDynamoOnly()
    {
        var item = CreateItem("active", createdAgo: TimeSpan.FromHours(9));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackDoesNotExist(item.StackName!);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(It.IsAny<DeleteStackRequest>(), default), Times.Never);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_PartialFailure_ContinuesToNextItem()
    {
        var item1 = CreateItem("active", createdAgo: TimeSpan.FromHours(9), email: "user1@example.com",
            stackName: "workspace-stack-1");
        var item2 = CreateItem("active", createdAgo: TimeSpan.FromHours(9), email: "user2@example.com",
            stackName: "workspace-stack-2");
        var item3 = CreateItem("active", createdAgo: TimeSpan.FromHours(9), email: "user3@example.com",
            stackName: "workspace-stack-3");

        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item1, item2, item3]);
        SetupStackExists(item1.StackName!, exists: true);
        // item2's DescribeStacks throws non-matching exception — CleanupWorkspaceAsync propagates it
        _cloudFormation.Setup(c => c.DescribeStacksAsync(
                It.Is<DescribeStacksRequest>(r => r.StackName == item2.StackName), default))
            .ThrowsAsync(new AmazonCloudFormationException("Service error"));
        SetupStackExists(item3.StackName!, exists: true);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _repository.Verify(r => r.DeleteAsync(item1), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item3), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item2), Times.Never);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_UsesStackId_WhenStackNameMissing()
    {
        var item = CreateItem("active", createdAgo: TimeSpan.FromHours(9), stackName: null,
            stackId: "arn:aws:cloudformation:us-east-1:123:stack/ws-abc/stack-id");
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackExists(item.StackId!, exists: true);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(
            It.Is<DeleteStackRequest>(r => r.StackName == item.StackId), default), Times.Once);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_ConditionalDeleteFails_HandlesGracefully()
    {
        var item = CreateItem("active", createdAgo: TimeSpan.FromHours(9));
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);
        SetupStackExists(item.StackName!, exists: true);
        _repository.Setup(r => r.DeleteAsync(item))
            .ThrowsAsync(new ConditionalCheckFailedException("Condition failed"));

        var sut = CreateSut();
        var act = () => sut.ExecuteCleanupAsync(CancellationToken.None);

        // Should not throw — conditional delete failure is handled gracefully
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteCleanupAsync_EmptyTable_CompletesCleanly()
    {
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([]);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(It.IsAny<DeleteStackRequest>(), default), Times.Never);
        _repository.Verify(r => r.DeleteAsync(It.IsAny<WorkspaceItem>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCleanupAsync_NoStackInfo_CleansDynamoOnly()
    {
        var item = CreateItem("failed", updatedAgo: TimeSpan.FromMinutes(90), stackName: null, stackId: null);
        _repository.Setup(r => r.ScanAllAsync()).ReturnsAsync([item]);

        var sut = CreateSut();
        await sut.ExecuteCleanupAsync(CancellationToken.None);

        _cloudFormation.Verify(c => c.DeleteStackAsync(It.IsAny<DeleteStackRequest>(), default), Times.Never);
        _repository.Verify(r => r.DeleteAsync(item), Times.Once);
    }

    private void SetupStackExists(string stackName, bool exists)
    {
        if (exists)
        {
            _cloudFormation.Setup(c => c.DescribeStacksAsync(
                    It.Is<DescribeStacksRequest>(r => r.StackName == stackName), default))
                .ReturnsAsync(new DescribeStacksResponse { Stacks = [new Stack()] });
        }
        else
        {
            SetupStackDoesNotExist(stackName);
        }
    }

    private void SetupStackDoesNotExist(string stackName)
    {
        _cloudFormation.Setup(c => c.DescribeStacksAsync(
                It.Is<DescribeStacksRequest>(r => r.StackName == stackName), default))
            .ThrowsAsync(new AmazonCloudFormationException(
                $"Stack with id {stackName} does not exist"));
    }

    private static WorkspaceItem CreateItem(string status,
        TimeSpan? createdAgo = null,
        TimeSpan? updatedAgo = null,
        string? stackName = "workspace-test-stack",
        string? stackId = "arn:aws:cloudformation:us-east-1:123:stack/ws-test/id",
        string email = "user@example.com")
    {
        var now = DateTime.UtcNow;
        return new WorkspaceItem
        {
            PK = $"USER#{email}",
            SK = "WORKSPACE",
            WorkspaceId = "test-ws-id",
            UserEmail = email,
            StackId = stackId,
            StackName = stackName,
            Status = status,
            CreatedAt = createdAgo.HasValue ? now.Subtract(createdAgo.Value) : now,
            UpdatedAt = updatedAgo.HasValue ? now.Subtract(updatedAgo.Value) : now
        };
    }
}
