using Amazon.CloudFormation;
using Amazon.DynamoDBv2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ProvisioningService.Api.Data;
using ProvisioningService.Api.Endpoints.Workspaces.v1;
using ProvisioningService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<WorkspaceStackOptions>(
    builder.Configuration.GetSection("WorkspaceStack"));

builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonCloudFormation>();

builder.Services.AddSingleton<IWorkspaceRepository>(sp =>
{
    var client = sp.GetRequiredService<IAmazonDynamoDB>();
    var tableName = builder.Configuration["DynamoDb:TableName"]
                    ?? "workspace-lookup";
    return new WorkspaceRepository(client, tableName);
});

builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();

// Endpoint handlers take a non-generic ILogger, which ASP.NET does not register.
builder.Services.AddSingleton<ILogger>(sp =>
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("ProvisioningService.Api.Endpoints"));

var cognitoAuthority = builder.Configuration["Cognito:Authority"];
var cognitoClientId = builder.Configuration["Cognito:ClientId"];

if (!string.IsNullOrEmpty(cognitoAuthority))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = cognitoAuthority;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = cognitoAuthority,
                ValidateAudience = !string.IsNullOrEmpty(cognitoClientId),
                ValidAudience = cognitoClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
        });

    builder.Services.AddAuthorization();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

var v1 = app.MapGroup("/api/v1");
var workspaceGroup = v1.MapGroup("/workspaces");

CreateWorkspaceEndpoint.Registration.Map(workspaceGroup);
GetWorkspaceEndpoint.Registration.Map(workspaceGroup);
DeleteWorkspaceEndpoint.Registration.Map(workspaceGroup);

app.MapFallbackToFile("index.html");

app.Run();
