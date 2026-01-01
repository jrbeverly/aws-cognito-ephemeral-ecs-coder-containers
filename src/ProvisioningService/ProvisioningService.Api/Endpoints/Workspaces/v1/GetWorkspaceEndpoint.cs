using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProvisioningService.Api.Models;
using ProvisioningService.Api.Services;

namespace ProvisioningService.Api.Endpoints.Workspaces.v1;

public static class GetWorkspaceEndpoint
{
    public static class Registration
    {
        public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
            group.MapGet("/me", Handler.HandleAsync)
                .WithName("GetWorkspace")
                .WithTags("Workspaces")
                .RequireAuthorization()
                .Produces<WorkspaceResponse>(StatusCodes.Status200OK)
                .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status401Unauthorized);
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
                    "Get workspace rejected — no email claim in token from {RemoteIp}",
                    httpContext.Connection.RemoteIpAddress);
                return Results.Problem(
                    detail: "Email claim not found in token.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication Required");
            }

            var record = await workspaceService.GetWorkspaceAsync(email);

            if (record is null)
            {
                logger.LogInformation(
                    "Get workspace for {UserEmail} — no workspace found", email);
                return Results.Problem(
                    detail: "No workspace found for this user.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }

            logger.LogInformation(
                "Get workspace for {UserEmail} — {WorkspaceId} ({Status})",
                email, record.WorkspaceId, record.Status);

            return Results.Ok(WorkspaceResponse.FromRecord(record));
        }
    }
}
