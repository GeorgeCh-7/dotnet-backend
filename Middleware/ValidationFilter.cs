using DotnetBackend.Models;

namespace DotnetBackend.Middleware;

public class ValidationFilter<T> : IEndpointFilter where T : class, IValidatable
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
            return Results.BadRequest(new { error = "Invalid or missing request body" });

        var errors = argument.Validate().ToList();
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        return await next(context);
    }
}
