namespace CleanupService.Worker.Models;

public class CleanupPolicyOptions
{
    public double MaxAgeHours { get; init; } = 8;
    public double ProvisioningTimeoutMinutes { get; init; } = 30;
    public double DeletingTimeoutMinutes { get; init; } = 60;
    public double FailedCleanupTimeoutMinutes { get; init; } = 60;
}
