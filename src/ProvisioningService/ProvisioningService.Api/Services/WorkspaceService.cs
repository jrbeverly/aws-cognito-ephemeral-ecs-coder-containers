using System.Security.Cryptography;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProvisioningService.Api.Data;
using ProvisioningService.Api.Models;
using InvalidOperationException = System.InvalidOperationException;

namespace ProvisioningService.Api.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceRepository _repository;
    private readonly IAmazonCloudFormation _cloudFormation;
    private readonly WorkspaceStackOptions _options;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        IWorkspaceRepository repository,
        IAmazonCloudFormation cloudFormation,
        IOptions<WorkspaceStackOptions> options,
        ILogger<WorkspaceService> logger)
    {
        _repository = repository;
        _cloudFormation = cloudFormation;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WorkspaceRecord> CreateWorkspaceAsync(string userEmail)
    {
        _logger.LogInformation(
            "Provisioning requested for {UserEmail}", userEmail);

        var existing = await _repository.GetByUserEmailAsync(userEmail);
        if (existing != null)
        {
            var status = Enum.Parse<WorkspaceStatus>(existing.Status, ignoreCase: true);
            if (status is WorkspaceStatus.Provisioning or WorkspaceStatus.Active or WorkspaceStatus.Deleting)
            {
                _logger.LogWarning(
                    "Provisioning blocked for {UserEmail} — existing workspace {WorkspaceId} has status {Status}",
                    userEmail, existing.WorkspaceId, existing.Status);
                throw new InvalidOperationException($"User already has a workspace with status '{existing.Status}'.");
            }

            _logger.LogInformation(
                "Overwriting failed workspace {OldWorkspaceId} for {UserEmail}",
                existing.WorkspaceId, userEmail);
        }

        var workspaceId = GenerateWorkspaceId();
        var password = GeneratePassword();
        var stackName = $"workspace-{workspaceId}";
        var now = DateTime.UtcNow;

        var record = new WorkspaceRecord
        {
            WorkspaceId = workspaceId,
            UserEmail = userEmail,
            StackId = string.Empty,
            StackName = stackName,
            Status = WorkspaceStatus.Provisioning,
            CreatedAt = now,
            UpdatedAt = now
        };

        var item = WorkspaceItem.FromRecord(record);
        await _repository.SaveAsync(item);

        _logger.LogInformation(
            "Workspace record created {WorkspaceId} for {UserEmail} — initiating CloudFormation stack {StackName}",
            workspaceId, userEmail, stackName);

        try
        {
            var createRequest = new CreateStackRequest
            {
                StackName = stackName,
                TemplateURL = _options.TemplateUrl,
                RoleARN = string.IsNullOrEmpty(_options.CloudFormationRoleArn) ? null : _options.CloudFormationRoleArn,
                Capabilities = ["CAPABILITY_NAMED_IAM"],
                Parameters =
                [
                    new() { ParameterKey = "WorkspaceId", ParameterValue = workspaceId },
                    new() { ParameterKey = "UserEmail", ParameterValue = userEmail },
                    new() { ParameterKey = "WorkspacePassword", ParameterValue = password },
                    new() { ParameterKey = "VpcId", ParameterValue = _options.VpcId },
                    new() { ParameterKey = "VpcCidr", ParameterValue = _options.VpcCidr },
                    new() { ParameterKey = "PublicSubnetIds", ParameterValue = _options.PublicSubnetIds },
                    new() { ParameterKey = "PrivateSubnetIds", ParameterValue = _options.PrivateSubnetIds },
                    new() { ParameterKey = "EcsClusterName", ParameterValue = _options.EcsClusterName },
                    new() { ParameterKey = "EcsTaskExecutionRoleArn", ParameterValue = _options.EcsTaskExecutionRoleArn },
                    new() { ParameterKey = "EcsTaskRoleArn", ParameterValue = _options.EcsTaskRoleArn },
                    new() { ParameterKey = "AlbSecurityGroupId", ParameterValue = _options.AlbSecurityGroupId },
                    new() { ParameterKey = "CognitoUserPoolId", ParameterValue = _options.CognitoUserPoolId },
                    new() { ParameterKey = "CognitoUserPoolArn", ParameterValue = _options.CognitoUserPoolArn },
                    new() { ParameterKey = "CognitoDomainPrefix", ParameterValue = _options.CognitoDomainPrefix },
                    new() { ParameterKey = "HostedZoneId", ParameterValue = _options.HostedZoneId },
                    new() { ParameterKey = "DomainName", ParameterValue = _options.DomainName },
                    new() { ParameterKey = "CertificateArn", ParameterValue = _options.CertificateArn },
                    new() { ParameterKey = "ContainerImage", ParameterValue = _options.ContainerImage },
                    new() { ParameterKey = "WorkspaceIamActions", ParameterValue = _options.WorkspaceIamActions },
                    new() { ParameterKey = "WorkspaceIamResourceArns", ParameterValue = _options.WorkspaceIamResourceArns },
                    new() { ParameterKey = "TaskCpu", ParameterValue = _options.TaskCpu.ToString() },
                    new() { ParameterKey = "TaskMemory", ParameterValue = _options.TaskMemory.ToString() }
                ],
                Tags =
                [
                    new() { Key = "WorkspaceId", Value = workspaceId },
                    new() { Key = "UserEmail", Value = userEmail },
                    new() { Key = "ManagedBy", Value = "ProvisioningService" },
                    new() { Key = "Service", Value = "Workspace" },
                    new() { Key = "Environment", Value = _options.Environment }
                ]
            };

            var createResponse = await _cloudFormation.CreateStackAsync(createRequest);

            _logger.LogInformation(
                "CloudFormation stack creation submitted {StackName} (StackId: {StackId}) for workspace {WorkspaceId} ({UserEmail})",
                stackName, createResponse.StackId, workspaceId, userEmail);

            record = record with { StackId = createResponse.StackId };
            item = WorkspaceItem.FromRecord(record);
            await _repository.SaveAsync(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CloudFormation stack creation failed for workspace {WorkspaceId} ({UserEmail}) — stack {StackName}",
                workspaceId, userEmail, stackName);

            record = record with { Status = WorkspaceStatus.Failed, UpdatedAt = DateTime.UtcNow };
            item = WorkspaceItem.FromRecord(record);
            await _repository.SaveAsync(item);
            throw;
        }

        return record;
    }

    public async Task<WorkspaceRecord?> GetWorkspaceAsync(string userEmail)
    {
        var item = await _repository.GetByUserEmailAsync(userEmail);
        if (item == null)
        {
            _logger.LogDebug(
                "No workspace found for {UserEmail}", userEmail);
            return null;
        }

        var record = item.ToRecord();

        _logger.LogDebug(
            "Workspace lookup {WorkspaceId} ({Status}) for {UserEmail}",
            record.WorkspaceId, record.Status, userEmail);

        if (record.Status == WorkspaceStatus.Provisioning && !string.IsNullOrEmpty(record.StackId))
        {
            record = await RefreshFromCloudFormation(record);
        }

        return record;
    }

    public async Task DeleteWorkspaceAsync(string userEmail)
    {
        var item = await _repository.GetByUserEmailAsync(userEmail);
        if (item == null)
        {
            _logger.LogWarning(
                "Delete requested for {UserEmail} but no workspace found", userEmail);
            throw new InvalidOperationException("No workspace found for this user.");
        }

        var record = item.ToRecord();

        _logger.LogInformation(
            "Deleting workspace {WorkspaceId} ({Status}) for {UserEmail} — stack {StackName}",
            record.WorkspaceId, record.Status, userEmail, record.StackName);

        record = record with { Status = WorkspaceStatus.Deleting, UpdatedAt = DateTime.UtcNow };
        var updatedItem = WorkspaceItem.FromRecord(record);
        await _repository.SaveAsync(updatedItem);

        if (!string.IsNullOrEmpty(record.StackName))
        {
            _logger.LogInformation(
                "Deleting CloudFormation stack {StackName} for workspace {WorkspaceId}",
                record.StackName, record.WorkspaceId);

            await _cloudFormation.DeleteStackAsync(new DeleteStackRequest
            {
                StackName = record.StackName
            });
        }

        await _repository.DeleteAsync(userEmail);

        _logger.LogInformation(
            "Workspace {WorkspaceId} deleted for {UserEmail}",
            record.WorkspaceId, userEmail);
    }

    private async Task<WorkspaceRecord> RefreshFromCloudFormation(WorkspaceRecord record)
    {
        try
        {
            var describeResponse = await _cloudFormation.DescribeStacksAsync(new DescribeStacksRequest
            {
                StackName = record.StackId
            });

            var stack = describeResponse.Stacks[0];
            var cfStatus = stack.StackStatus.Value;

            _logger.LogDebug(
                "CloudFormation stack {StackName} status {StackStatus} for workspace {WorkspaceId}",
                record.StackName, cfStatus, record.WorkspaceId);

            return cfStatus switch
            {
                "CREATE_COMPLETE" or "UPDATE_COMPLETE" => UpdateFromStackOutputs(record, stack, WorkspaceStatus.Active),
                "CREATE_IN_PROGRESS" => record,
                "ROLLBACK_COMPLETE" or "ROLLBACK_IN_PROGRESS" or "DELETE_FAILED"
                    => OnCFFailure(record, cfStatus),
                "DELETE_COMPLETE"
                    => OnCFDeleted(record),
                _ => record
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to refresh CloudFormation state for workspace {WorkspaceId} ({UserEmail}) — stack {StackName}",
                record.WorkspaceId, record.UserEmail, record.StackName);
            return record;
        }
    }

    private WorkspaceRecord OnCFFailure(WorkspaceRecord record, string cfStatus)
    {
        _logger.LogWarning(
            "Workspace {WorkspaceId} ({UserEmail}) marked Failed — CloudFormation stack {StackName} status {StackStatus}",
            record.WorkspaceId, record.UserEmail, record.StackName, cfStatus);
        return record with { Status = WorkspaceStatus.Failed, UpdatedAt = DateTime.UtcNow };
    }

    private WorkspaceRecord OnCFDeleted(WorkspaceRecord record)
    {
        _logger.LogInformation(
            "Workspace {WorkspaceId} ({UserEmail}) CloudFormation stack {StackName} already deleted",
            record.WorkspaceId, record.UserEmail, record.StackName);
        return record with { Status = WorkspaceStatus.Failed, UpdatedAt = DateTime.UtcNow };
    }

    private WorkspaceRecord UpdateFromStackOutputs(WorkspaceRecord record, Stack stack, WorkspaceStatus status)
    {
        string? workspaceUrl = null;
        string? albDnsName = null;
        string? ecsServiceName = null;

        foreach (var output in stack.Outputs ?? [])
        {
            switch (output.OutputKey)
            {
                case "WorkspaceUrl":
                    workspaceUrl = output.OutputValue;
                    break;
                case "AlbDnsName":
                    albDnsName = output.OutputValue;
                    break;
                case "EcsServiceName":
                    ecsServiceName = output.OutputValue;
                    break;
            }
        }

        _logger.LogInformation(
            "Workspace {WorkspaceId} ({UserEmail}) transitioned to {Status} — " +
            "WorkspaceUrl: {WorkspaceUrl}, AlbDnsName: {AlbDnsName}, EcsServiceName: {EcsServiceName}",
            record.WorkspaceId, record.UserEmail, status,
            workspaceUrl, albDnsName, ecsServiceName);

        return record with
        {
            Status = status,
            WorkspaceUrl = workspaceUrl,
            AlbDnsName = albDnsName,
            EcsServiceName = ecsServiceName,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static string GenerateWorkspaceId()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var id = Convert.ToHexStringLower(bytes)[..12];
        return id;
    }

    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghjkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%^&*";
        const string all = upper + lower + digits + symbols;

        var bytes = RandomNumberGenerator.GetBytes(24);
        var chars = new char[16];

        chars[0] = upper[bytes[0] % upper.Length];
        chars[1] = lower[bytes[1] % lower.Length];
        chars[2] = digits[bytes[2] % digits.Length];
        chars[3] = symbols[bytes[3] % symbols.Length];

        for (var i = 4; i < chars.Length; i++)
            chars[i] = all[bytes[i] % all.Length];

        RandomNumberGenerator.GetBytes(8);
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = bytes[8 + i % 8] % (i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}

public class WorkspaceStackOptions
{
    public required string TemplateUrl { get; init; }
    public required string VpcId { get; init; }
    public required string VpcCidr { get; init; }
    public required string PublicSubnetIds { get; init; }
    public required string PrivateSubnetIds { get; init; }
    public required string EcsClusterName { get; init; }
    public required string EcsTaskExecutionRoleArn { get; init; }
    public required string EcsTaskRoleArn { get; init; }
    public required string AlbSecurityGroupId { get; init; }
    public required string CognitoUserPoolId { get; init; }
    public required string CognitoUserPoolArn { get; init; }
    public required string CognitoDomainPrefix { get; init; }
    public required string HostedZoneId { get; init; }
    public required string DomainName { get; init; }
    public required string CertificateArn { get; init; }
    public required string ContainerImage { get; init; }
    public string CloudFormationRoleArn { get; init; } = "";
    public string WorkspaceIamActions { get; init; } = "logs:CreateLogStream,logs:PutLogEvents,logs:DescribeLogStreams,ssm:GetParameter,ssm:GetParameters";
    public string WorkspaceIamResourceArns { get; init; } = "*";
    public int TaskCpu { get; init; } = 512;
    public int TaskMemory { get; init; } = 1024;
    public string Environment { get; init; } = "prod";
}
