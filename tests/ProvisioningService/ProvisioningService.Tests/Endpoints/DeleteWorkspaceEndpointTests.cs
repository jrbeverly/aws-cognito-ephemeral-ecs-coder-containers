using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using ProvisioningService.Api.Endpoints.Workspaces.v1;
using ProvisioningService.Api.Services;

namespace ProvisioningService.Tests.Endpoints;

public class DeleteWorkspaceEndpointTests
{
    private readonly Mock<IWorkspaceService> _service = new();
    private static ILogger GetHandlerLogger() =>
        Mock.Of<ILogger>();

    [Fact]
    public async Task HandleAsync_WorkspaceExists_Returns202()
    {
        _service.Setup(s => s.DeleteWorkspaceAsync("user@example.com"))
            .Returns(Task.CompletedTask);

        var context = CreateHttpContext("user@example.com");
        var result = await DeleteWorkspaceEndpoint.Handler.HandleAsync(
            context, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<Accepted>();
    }

    [Fact]
    public async Task HandleAsync_NoWorkspace_Returns404()
    {
        _service.Setup(s => s.DeleteWorkspaceAsync("user@example.com"))
            .ThrowsAsync(new InvalidOperationException("No workspace found for this user."));

        var context = CreateHttpContext("user@example.com");
        var result = await DeleteWorkspaceEndpoint.Handler.HandleAsync(
            context, _service.Object, GetHandlerLogger());

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task HandleAsync_NoEmailClaim_Returns401()
    {
        var context = CreateHttpContext(email: null);
        var result = await DeleteWorkspaceEndpoint.Handler.HandleAsync(
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
