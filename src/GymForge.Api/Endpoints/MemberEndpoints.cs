using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Api.Endpoints;

public static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/members").WithTags("Members");

        // GET /api/members?companyId=&siteId=&page=&pageSize=&status=
        group.MapGet("/", async (
            Guid companyId, Guid siteId,
            int page = 1, int pageSize = 50,
            MemberStatus? status = null,
            IMediator mediator = default!) =>
        {
            var result = await mediator.Send(
                new GetMembersQuery(companyId, siteId, page, pageSize, status));
            return Results.Ok(result);
        });

        // GET /api/members/search?query=&companyId=&siteId=
        group.MapGet("/search", async (
            string query, Guid companyId, Guid siteId, int take = 20,
            IMediator mediator = default!) =>
        {
            var result = await mediator.Send(
                new SearchMembersQuery(query, companyId, siteId, take));
            return Results.Ok(result);
        });

        // GET /api/members/{id}
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator) =>
        {
            var member = await mediator.Send(new GetMemberByIdQuery(id));
            return member is null ? Results.NotFound() : Results.Ok(member);
        });

        // POST /api/members
        group.MapPost("/", async (CreateMemberCommand cmd, IMediator mediator) =>
        {
            try
            {
                var dto = await mediator.Send(cmd);
                return Results.Created($"/api/members/{dto.Id}", dto);
            }
            catch (FluentValidation.ValidationException vex)
            {
                return Results.ValidationProblem(
                    vex.Errors.GroupBy(e => e.PropertyName)
                       .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
            }
        });

        return app;
    }
}
