using System.Text.Json;
using SlotMath.Api.Features.Configs;
using SlotMath.Api.Infrastructure;
using SlotMath.Core.Compiler;
using SlotMath.Core.Model;

namespace SlotMath.Api.Features.Validate;

/// <summary>
///     POST /validate — returns the same errors as the G14 compiler.
/// </summary>
public static class ValidateEndpoints
{
    public static RouteGroupBuilder MapValidate(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/validate");

        group.MapPost("/simplify", GraphSimplificationEndpoint.Simplify)
            .Produces<GraphSimplificationResponse>().RequireRateLimiting("compute");

        group.MapPost("/", (ValidateRequest request) =>
        {
            GraphConfig config;
            try
            {
                config = ConfigsEndpoints.DeserializeConfig(request.Config);
                if (config is null)
                {
                    return Results.BadRequest(new ValidateResponse
                    {
                        IsValid = false,
                        Errors = new[]
                        {
                            new ValidateErrorItem
                            {
                                Code = "INVALID_CONFIG",
                                Message = "Configuration is missing or null."
                            }
                        }
                    });
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return Results.BadRequest(new ValidateResponse
                {
                    IsValid = false,
                    Errors = new[]
                    {
                        new ValidateErrorItem
                        {
                            Code = "INVALID_JSON",
                            Message = ex.Message
                        }
                    }
                });
            }

            var compiler = new GraphCompiler();
            CompileResult result = compiler.Compile(config);

            return Results.Ok(new ValidateResponse
            {
                IsValid = result.IsValid,
                Errors = result.Errors.Select(e => new ValidateErrorItem
                {
                    NodeId = e.NodeId,
                    EdgeId = e.EdgeId,
                    Message = e.Message,
                    Code = e.Code
                }).ToList()
            });
        }).Produces<ValidateResponse>();

        return group;
    }
}
