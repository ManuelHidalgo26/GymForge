using GymForge.Application.UseCases.Access;
using GymForge.Domain.Enums;

namespace GymForge.Api.Endpoints;

public static class AccessEndpoints
{
    public static IEndpointRouteBuilder MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/access").WithTags("Access");

        // POST /api/access/swipe — called by AccessBroker or desktop front-desk
        group.MapPost("/swipe", async (SwipeRequest req, ValidateSwipeUseCase gatekeeper) =>
        {
            var decision = await gatekeeper.ValidateSwipeAsync(
                new ValidateSwipeRequest(
                    req.Credential, req.Method,
                    req.DoorId, req.SiteId, req.CompanyId));

            return Results.Ok(decision);
        });

        return app;
    }
}

record SwipeRequest(
    string Credential,
    AccessMethod Method,
    int DoorId,
    Guid SiteId,
    Guid CompanyId);
