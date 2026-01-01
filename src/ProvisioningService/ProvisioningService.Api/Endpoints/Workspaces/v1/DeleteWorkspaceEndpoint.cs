using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProvisioningService.Api.Services;

namespace ProvisioningService.Api.Endpoints.Workspaces.v1;

public static class DeleteWorkspaceEndpoint
{
    public static class Registration
    {
        public static RouteHandlerBuilder Map(RouteGroupBuilder group) =>
            group.MapDelete("/me", Handler.HandleAsync)
                .WithName("DeleteWorkspace")
                .WithTags("Workspaces")
                .RequireAuthorization()
                .Produces(StatusCodes.Status202Accepted)
                .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
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
                    "Delete workspace rejected — no email claim in token from {RemoteIp}",
                    httpContext.Connection.RemoteIpAddress);
                return Results.Problem(
                    detail: "Email claim not found in token.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Authentication Required");
            }

            try
            {
                await workspaceService.DeleteWorkspaceAsync(email);
                logger.LogInformation(
                    "Delete workspace accepted for {UserEmail}", email);
                return Results.Accepted();
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(
                    "Delete workspace for {UserEmail} — {Reason}", email, ex.Message);
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found");
            }
        }
    }
}
