using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ProvisioningService.Api.Data;
using ProvisioningService.Api.Models;
using ProvisioningService.Api.Services;
using InvalidOperationException = System.InvalidOperationException;

namespace ProvisioningService.Tests.Services;

public class WorkspaceServiceTests
{
    private readonly Mock<IWorkspaceRepository> _repository = new();
    private readonly Mock<IAmazonCloudFormation> _cloudFormation = new();
    private readonly WorkspaceStackOptions _options = CreateOptions();
    private readonly IOptions<WorkspaceStackOptions> _optionsWrapper;

    public WorkspaceServiceTests()
    {
        _optionsWrapper = Options.Create(_options);
    }

    private WorkspaceService CreateSut() =>
        new(_repository.Object, _cloudFormation.Object, _optionsWrapper,
            Mock.Of<ILogger<WorkspaceService>>());

    [Fact]
    public async Task CreateWorkspaceAsync_HasNoExistingWorkspace_CreatesSuccessfully()
    {
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync((WorkspaceItem?)null);

        _cloudFormation.Setup(c => c.CreateStackAsync(It.IsAny<CreateStackRequest>(), default))
            .ReturnsAsync(new CreateStackResponse
            {
                StackId = "arn:aws:cloudformation:us-east-1:123456789:stack/workspace-abc/stack-id"
            });

        var sut = CreateSut();
        var result = await sut.CreateWorkspaceAsync("user@example.com");

        result.Status.Should().Be(WorkspaceStatus.Provisioning);
        result.UserEmail.Should().Be("user@example.com");
        result.WorkspaceId.Should().NotBeNullOrEmpty();
        result.StackId.Should().Be("arn:aws:cloudformation:us-east-1:123456789:stack/workspace-abc/stack-id");

        _repository.Verify(r => r.SaveAsync(It.IsAny<WorkspaceItem>()), Times.AtLeast(2));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("provisioning")]
    [InlineData("deleting")]
    public async Task CreateWorkspaceAsync_HasExistingNonTerminalWorkspace_Throws(string existingStatus)
    {
        var existing = new WorkspaceItem
        {
            PK = "USER#user@example.com",
            WorkspaceId = "existing-ws",
            UserEmail = "user@example.com",
            Status = existingStatus
        };
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync(existing);

        var sut = CreateSut();
        var act = () => sut.CreateWorkspaceAsync("user@example.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"User already has a workspace with status '{existingStatus}'.");
    }

    [Fact]
    public async Task CreateWorkspaceAsync_HasFailedWorkspace_AllowsNewCreation()
    {
        var old = new WorkspaceItem
        {
            PK = "USER#user@example.com",
            WorkspaceId = "old-ws",
            UserEmail = "user@example.com",
            Status = "failed"
        };
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync(old);

        _cloudFormation.Setup(c => c.CreateStackAsync(It.IsAny<CreateStackRequest>(), default))
            .ReturnsAsync(new CreateStackResponse { StackId = "stack-id" });

        var sut = CreateSut();
        var result = await sut.CreateWorkspaceAsync("user@example.com");

        result.Status.Should().Be(WorkspaceStatus.Provisioning);
    }

    [Fact]
    public async Task CreateWorkspaceAsync_CloudFormationFails_SetsFailedStatus()
    {
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync((WorkspaceItem?)null);

        _cloudFormation.Setup(c => c.CreateStackAsync(It.IsAny<CreateStackRequest>(), default))
            .ThrowsAsync(new AmazonCloudFormationException("Test failure"));

        var sut = CreateSut();
        var act = () => sut.CreateWorkspaceAsync("user@example.com");

        await act.Should().ThrowAsync<AmazonCloudFormationException>();

        _repository.Verify(r => r.SaveAsync(
            It.Is<WorkspaceItem>(i => i.Status == "failed")), Times.Once);
    }

    [Fact]
    public async Task GetWorkspaceAsync_NoWorkspace_ReturnsNull()
    {
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync((WorkspaceItem?)null);

        var sut = CreateSut();
        var result = await sut.GetWorkspaceAsync("user@example.com");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetWorkspaceAsync_ActiveWorkspace_ReturnsRecord()
    {
        var item = CreateItem("active");
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync(item);

        var sut = CreateSut();
        var result = await sut.GetWorkspaceAsync("user@example.com");

        result.Should().NotBeNull();
        result!.Status.Should().Be(WorkspaceStatus.Active);
        result.WorkspaceId.Should().Be("test-workspace");
    }

    [Fact]
    public async Task GetWorkspaceAsync_ProvisioningWorkspace_RefreshesFromCloudFormation()
    {
        var item = CreateItem("provisioning", stackId: "stack-arn");
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync(item);

        _cloudFormation.Setup(c => c.DescribeStacksAsync(
                It.Is<DescribeStacksRequest>(r => r.StackName == "stack-arn"), default))
            .ReturnsAsync(new DescribeStacksResponse
            {
                Stacks =
                [
                    new Stack
                    {
                        StackStatus = StackStatus.CREATE_COMPLETE,
                        Outputs =
                        [
                            new() { OutputKey = "WorkspaceUrl", OutputValue = "https://test.example.com" },
                            new() { OutputKey = "AlbDnsName", OutputValue = "alb.dns.name" }
                        ]
                    }
                ]
            });

        _repository.Setup(r => r.SaveAsync(It.IsAny<WorkspaceItem>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        var result = await sut.GetWorkspaceAsync("user@example.com");

        result.Should().NotBeNull();
        result!.Status.Should().Be(WorkspaceStatus.Active);
        result.WorkspaceUrl.Should().Be("https://test.example.com");
        result.AlbDnsName.Should().Be("alb.dns.name");
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_ExistingWorkspace_DeletesSuccessfully()
    {
        var item = CreateItem("active", stackName: "workspace-test");
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync(item);

        _cloudFormation.Setup(c => c.DeleteStackAsync(
                It.Is<DeleteStackRequest>(r => r.StackName == "workspace-test"), default))
            .ReturnsAsync(new DeleteStackResponse());

        var sut = CreateSut();
        await sut.DeleteWorkspaceAsync("user@example.com");

        _repository.Verify(r => r.SaveAsync(It.Is<WorkspaceItem>(
            i => i.Status == "deleting")), Times.Once);
        _repository.Verify(r => r.DeleteAsync("user@example.com"), Times.Once);
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_NoWorkspace_Throws()
    {
        _repository.Setup(r => r.GetByUserEmailAsync("user@example.com"))
            .ReturnsAsync((WorkspaceItem?)null);

        var sut = CreateSut();
        var act = () => sut.DeleteWorkspaceAsync("user@example.com");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No workspace found for this user.");
    }

    private static WorkspaceItem CreateItem(string status, string? stackId = "stack-123",
        string? stackName = "workspace-test")
        => new()
        {
            PK = "USER#user@example.com",
            WorkspaceId = "test-workspace",
            UserEmail = "user@example.com",
            StackId = stackId,
            StackName = stackName,
            Status = status,
            WorkspaceUrl = "https://test.example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static WorkspaceStackOptions CreateOptions() => new()
    {
        TemplateUrl = "https://s3.amazonaws.com/bucket/template.yaml",
        VpcId = "vpc-123",
        VpcCidr = "10.0.0.0/16",
        PublicSubnetIds = "subnet-1,subnet-2",
        PrivateSubnetIds = "subnet-3,subnet-4",
        EcsClusterName = "workspace-cluster",
        EcsTaskExecutionRoleArn = "arn:aws:iam::123:role/exec",
        EcsTaskRoleArn = "arn:aws:iam::123:role/task",
        AlbSecurityGroupId = "sg-123",
        CognitoUserPoolId = "pool-123",
        CognitoUserPoolArn = "arn:aws:cognito-idp:us-east-1:123:userpool/pool-123",
        CognitoDomainPrefix = "my-domain",
        HostedZoneId = "Z123",
        DomainName = "example.com",
        CertificateArn = "arn:aws:acm:us-east-1:123:certificate/cert-123",
        ContainerImage = "123.dkr.ecr.us-east-1.amazonaws.com/workspace-runtime:latest"
    };
}
