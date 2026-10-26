using AF.Blood.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AF.Blood.Api.Middleware;

/// <summary>Maps typed application errors to Problem Details; never leaks
/// internal exception messages to clients.</summary>
public sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is AppException appException)
        {
            context.Response.StatusCode = appException.StatusCode;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = appException.StatusCode,
                Title = appException.Title,
                Detail = appException.Message,
                Instance = context.Request.Path,
            }.WithErrorCode(appException.ErrorCode), cancellationToken);
            return true;
        }

        logger.LogError(exception, "Unhandled exception on {Method} {Path} (correlation {Correlation})",
            context.Request.Method, context.Request.Path, context.GetCorrelationId());

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal server error",
            Detail = "An unexpected error occurred. Reference the correlation id when contacting support.",
            Instance = context.Request.Path,
        }, cancellationToken);
        return true;
    }
}

public static class ProblemDetailsExtensions
{
    public static ProblemDetails WithErrorCode(this ProblemDetails problem, string errorCode)
    {
        problem.Extensions["errorCode"] = errorCode;
        return problem;
    }
}
