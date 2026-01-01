using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProvisioningService.Api.Models;
using ProvisioningService.Api.Services;

namespace ProvisioningService.Api.Endpoints.Workspaces.v1;

public static class CreateWorkspaceEndpoint
{
    public static class Registration
    {
        public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
            group.MapPost("/", Handler.HandleAsync)
                .WithName("CreateWorkspace")
                .WithTags("Workspaces")
                .RequireAuthorization()
                .Produces<WorkspaceResponse>(StatusCodes.Status202Accepted)
                .Produces<ProblemDetails>(StatusCodes.Status409Conflict)
                .Produces(StatusCodes.Status401Unauthorized)
                .Produces(StatusCodes.Status500InternalServerError);
    }

    public static class Handler
    {
        public static async Task<IResult> HandleAsync(
            HttpContext httpContext,
            IWorkspaceService workspaceService,
            ILogger logger)
        {
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                        ?? httpContext.User.FindFirst("email")?.Value;

            if (string.IsNullOrEmpty(email))
            {
                logger.LogWarning(
                    "Create workspace rejected — no email claim in token from {RemoteIp}",
                    httpContext.Connection.RemoteIpAddress);
                return Results.Problem(
                    detail: "Email claim not found in token.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication Required");
            }

            try
            {
                var record = await workspaceService.CreateWorkspaceAsync(email);

                logger.LogInformation(
                    "Create workspace completed {WorkspaceId} for {UserEmail} — status {Status}",
                    record.WorkspaceId, email, record.Status);

                return Results.Json(
                    WorkspaceResponse.FromRecord(record),
                    statusCode: StatusCodes.Status202Accepted);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already has a workspace"))
            {
                logger.LogWarning(
                    "Create workspace rejected {UserEmail} — {Reason}",
                    email, ex.Message);
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Workspace Exists");
            }
        }
    }
}
