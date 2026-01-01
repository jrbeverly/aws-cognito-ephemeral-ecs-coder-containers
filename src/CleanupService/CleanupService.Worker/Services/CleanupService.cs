using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Amazon.DynamoDBv2.Model;
using CleanupService.Worker.Data;
using CleanupService.Worker.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanupService.Worker.Services;

public class WorkspaceCleanupService : ICleanupService
{
    private readonly IWorkspaceRepository _repository;
    private readonly IAmazonCloudFormation _cloudFormation;
    private readonly CleanupPolicyOptions _options;
    private readonly ILogger<WorkspaceCleanupService> _logger;

    public WorkspaceCleanupService(
        IWorkspaceRepository repository,
        IAmazonCloudFormation cloudFormation,
        IOptions<CleanupPolicyOptions> options,
        ILogger<WorkspaceCleanupService> logger)
    {
        _repository = repository;
        _cloudFormation = cloudFormation;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ExecuteCleanupAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleanup scan starting");

        var items = await _repository.ScanAllAsync();
        var scanned = items.Count;
        var skipped = 0;
        var cleaned = 0;
        var errors = 0;
        var reasonCounts = new Dictionary<string, int>();
        var statusCounts = new Dictionary<string, int>();

        foreach (var item in items)
            statusCounts[item.Status] = statusCounts.GetValueOrDefault(item.Status) + 1;

        _logger.LogInformation(
            "Scanned {Scanned} workspace records — status breakdown: {@StatusCounts}",
            scanned, statusCounts);

        if (scanned > 100)
        {
            _logger.LogWarning(
                "Scanned {Count} workspace records — consider adding a status-indexed GSI for efficiency",
                scanned);
        }

        var now = DateTime.UtcNow;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reason = EvaluateCleanup(item, now);
            if (reason == null)
            {
                skipped++;
                _logger.LogDebug(
                    "Workspace {WorkspaceId} ({Status}) for {UserEmail} — skipping, not eligible",
                    item.WorkspaceId, item.Status, item.UserEmail);
                continue;
            }

            _logger.LogInformation(
                "Cleaning up workspace {WorkspaceId} for {UserEmail} ({Status}) — " +
                "reason: {Reason}, stack: {StackName}, created: {CreatedAt}",
                item.WorkspaceId, item.UserEmail, item.Status,
                reason, item.StackName ?? item.StackId, item.CreatedAt.ToString("O"));

            try
            {
                await CleanupWorkspaceAsync(item, reason);
                cleaned++;
                reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason) + 1;
            }
            catch (Exception ex)
            {
                errors++;
                _logger.LogError(ex,
                    "Failed to clean up workspace {WorkspaceId} ({Status}) for {UserEmail} — reason: {Reason}",
                    item.WorkspaceId, item.Status, item.UserEmail, reason);
            }
        }

        _logger.LogInformation(
            "Cleanup scan complete — scanned: {Scanned}, skipped: {Skipped}, cleaned: {Cleaned}, errors: {Errors}, " +
            "reasons: {@ReasonCounts}",
            scanned, skipped, cleaned, errors, reasonCounts);
    }

    private string? EvaluateCleanup(WorkspaceItem item, DateTime now)
    {
        return item.Status switch
        {
            "provisioning" when now > item.UpdatedAt.AddMinutes(_options.ProvisioningTimeoutMinutes)
                => "provisioning timed out",
            "active" when now > item.CreatedAt.AddHours(_options.MaxAgeHours)
                => $"max age exceeded ({_options.MaxAgeHours}h)",
            "deleting" when now > item.UpdatedAt.AddMinutes(_options.DeletingTimeoutMinutes)
                => "stuck in deletion",
            "failed" when now > item.UpdatedAt.AddMinutes(_options.FailedCleanupTimeoutMinutes)
                => "stale failure cleaned up",
            _ => null
        };
    }

    private async Task CleanupWorkspaceAsync(WorkspaceItem item, string reason)
    {
        var stackIdentifier = item.StackName ?? item.StackId;

        // Delete the CloudFormation stack if it still exists
        if (!string.IsNullOrEmpty(stackIdentifier))
        {
            var exists = await StackExistsAsync(stackIdentifier);
            if (exists)
            {
                _logger.LogInformation(
                    "Deleting CloudFormation stack {StackName} for workspace {WorkspaceId} ({UserEmail}) — reason: {Reason}",
                    stackIdentifier, item.WorkspaceId, item.UserEmail, reason);

                await _cloudFormation.DeleteStackAsync(new DeleteStackRequest
                {
                    StackName = stackIdentifier
                });
            }
            else
            {
                _logger.LogInformation(
                    "CloudFormation stack {StackName} for workspace {WorkspaceId} ({UserEmail}) already deleted — cleaning DynamoDB record only",
                    stackIdentifier, item.WorkspaceId, item.UserEmail);
            }
        }
        else
        {
            _logger.LogWarning(
                "Workspace {WorkspaceId} ({UserEmail}) has no stack reference — cleaning DynamoDB record only",
                item.WorkspaceId, item.UserEmail);
        }

        // Remove DynamoDB record — conditional delete prevents race with re-provisioning
        try
        {
            await _repository.DeleteAsync(item);
            _logger.LogInformation(
                "Removed workspace record {WorkspaceId} for {UserEmail}",
                item.WorkspaceId, item.UserEmail);
        }
        catch (ConditionalCheckFailedException)
        {
            _logger.LogInformation(
                "Workspace {WorkspaceId} for {UserEmail} was re-provisioned — skipping DynamoDB delete",
                item.WorkspaceId, item.UserEmail);
        }
    }

    private async Task<bool> StackExistsAsync(string stackName)
    {
        try
        {
            var response = await _cloudFormation.DescribeStacksAsync(new DescribeStacksRequest
            {
                StackName = stackName
            });

            var status = response.Stacks is { Count: > 0 }
                ? response.Stacks[0].StackStatus?.Value ?? "UNKNOWN"
                : "NO_STACKS";
            _logger.LogDebug(
                "CloudFormation stack {StackName} exists with status {StackStatus}",
                stackName, status);
            return true;
        }
        catch (AmazonCloudFormationException ex) when (
            ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "CloudFormation stack {StackName} does not exist", stackName);
            return false;
        }
    }
}
