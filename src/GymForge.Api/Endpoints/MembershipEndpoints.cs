using GymForge.Application.UseCases.Memberships;
using MediatR;

namespace GymForge.Api.Endpoints;

public static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memberships").WithTags("Memberships");

        // POST /api/memberships
        group.MapPost("/", async (CreateMembershipCommand cmd, IMediator mediator) =>
        {
            try
            {
                var dto = await mediator.Send(cmd);
                return Results.Created($"/api/memberships/{dto.Id}", dto);
            }
            catch (FluentValidation.ValidationException vex)
            {
                return Results.ValidationProblem(
                    vex.Errors.GroupBy(e => e.PropertyName)
                       .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
            }
        });

        // POST /api/memberships/{id}/freeze
        group.MapPost("/{id:guid}/freeze", async (Guid id, FreezeMembershipRequest req, IMediator mediator) =>
        {
            var dto = await mediator.Send(
                new FreezeMembershipCommand(id, req.FreezeStart, req.FreezeEnd, req.Reason));
            return Results.Ok(dto);
        });

        // POST /api/memberships/{id}/cancel
        group.MapPost("/{id:guid}/cancel", async (Guid id, CancelMembershipRequest req, IMediator mediator) =>
        {
            var dto = await mediator.Send(new CancelMembershipCommand(id, req.Reason));
            return Results.Ok(dto);
        });

        return app;
    }
}

record FreezeMembershipRequest(DateOnly FreezeStart, DateOnly FreezeEnd, string Reason);
record CancelMembershipRequest(string Reason);
