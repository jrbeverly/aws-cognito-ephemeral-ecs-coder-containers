using Amazon.CloudFormation;
using Amazon.DynamoDBv2;
using CleanupService.Worker.Data;
using CleanupService.Worker.Models;
using CleanupService.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<CleanupPolicyOptions>(
    builder.Configuration.GetSection("CleanupPolicy"));

builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonCloudFormation>();

builder.Services.AddSingleton<IWorkspaceRepository>(sp =>
{
    var client = sp.GetRequiredService<IAmazonDynamoDB>();
    var tableName = builder.Configuration["DynamoDb:TableName"]
                    ?? "workspace-lookup";
    return new WorkspaceRepository(client, tableName);
});

builder.Services.AddSingleton<ICleanupService, WorkspaceCleanupService>();

// Run-to-completion: the scheduler starts a new task every 15 minutes, so the
// process must exit after one scan. A hosted service would keep the host alive.
using var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Cleanup worker starting");
await host.Services.GetRequiredService<ICleanupService>().ExecuteCleanupAsync(CancellationToken.None);
logger.LogInformation("Cleanup worker completed — shutting down");
