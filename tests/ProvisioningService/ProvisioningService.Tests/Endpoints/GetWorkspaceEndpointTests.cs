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

public class GetWorkspaceEndpointTests
{
    private readonly Mock<IWorkspaceService> _service = new();
    private static ILogger GetHandlerLogger() =>
        Mock.Of<ILogger>();

    [Fact]
    public async Task HandleAsync_WorkspaceExists_Returns200()
    {
        var record = new WorkspaceRecord
        {
            WorkspaceId = "abc123",
            UserEmail = "user@example.com",
            StackId = "stack-id",
            StackName = "workspace-abc123",
            Status = WorkspaceStatus.Active,
            WorkspaceUrl = "https://abc123.example.com",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _service.Setup(s => s.GetWorkspaceAsync("user@example.com"))
            .ReturnsAsync(record);

        var context = CreateHttpContext("user@example.com");
        var result = await GetWorkspaceEndpoint.Handler.HandleAsync(
            context, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<Ok<WorkspaceResponse>>();
        var okResult = (Ok<WorkspaceResponse>)result;
        okResult.Value!.WorkspaceId.Should().Be("abc123");
        okResult.Value.Status.Should().Be("active");
    }

    [Fact]
    public async Task HandleAsync_NoWorkspace_Returns404()
    {
        _service.Setup(s => s.GetWorkspaceAsync("user@example.com"))
            .ReturnsAsync((WorkspaceRecord?)null);

        var context = CreateHttpContext("user@example.com");
        var result = await GetWorkspaceEndpoint.Handler.HandleAsync(
            context, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task HandleAsync_NoEmailClaim_Returns401()
    {
        var context = CreateHttpContext(email: null);
        var result = await GetWorkspaceEndpoint.Handler.HandleAsync(
            context, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(401);
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
