using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using ProvisioningService.Api.Endpoints.Workspaces.v1;
using ProvisioningService.Api.Models;
using ProvisioningService.Api.Services;

namespace ProvisioningService.Tests.Endpoints;

public class CreateWorkspaceEndpointTests
{
    private readonly Mock<IWorkspaceService> _service = new();
    private static ILogger GetHandlerLogger() =>
        Mock.Of<ILogger>();

    [Fact]
    public async Task HandleAsync_AuthenticatedUser_CreatesWorkspace()
    {
        var record = new WorkspaceRecord
        {
            WorkspaceId = "abc123",
            UserEmail = "user@example.com",
            StackId = "stack-id",
            StackName = "workspace-abc123",
            Status = WorkspaceStatus.Provisioning,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _service.Setup(s => s.CreateWorkspaceAsync("user@example.com"))
            .ReturnsAsync(record);

        var httpContext = CreateHttpContext("user@example.com");
        var result = await CreateWorkspaceEndpoint.Handler.HandleAsync(
            httpContext, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<JsonHttpResult<WorkspaceResponse>>();
        var jsonResult = (JsonHttpResult<WorkspaceResponse>)result;
        jsonResult.StatusCode.Should().Be(202);
        jsonResult.Value!.WorkspaceId.Should().Be("abc123");
        jsonResult.Value.Status.Should().Be("provisioning");
    }

    [Fact]
    public async Task HandleAsync_NoEmailClaim_Returns401()
    {
        var httpContext = CreateHttpContext(email: null);
        var result = await CreateWorkspaceEndpoint.Handler.HandleAsync(
            httpContext, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task HandleAsync_ExistingWorkspace_Returns409()
    {
        _service.Setup(s => s.CreateWorkspaceAsync("user@example.com"))
            .ThrowsAsync(new InvalidOperationException("User already has a workspace with status 'active'."));

        var httpContext = CreateHttpContext("user@example.com");
        var result = await CreateWorkspaceEndpoint.Handler.HandleAsync(
            httpContext, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(409);
    }

    private static HttpContext CreateHttpContext(string? email)
    {
        var context = new DefaultHttpContext();
        if (email != null)
        {
            var claims = new List<Claim> { new(ClaimTypes.Email, email) };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }
        return context;
    }
}
