using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Shop.Application.Common;
using Shop.Domain.Common;

namespace Shop.Api.Common;

public sealed class ProblemExceptionHandler(IProblemDetailsService problemDetails, ILogger<ProblemExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        (int status, string code, string title, string detail)? mapped = exception switch
        {
            BadHttpRequestException e => (e.StatusCode, "request.malformed", "Invalid request", e.Message),
            NotFoundException e => (StatusCodes.Status404NotFound, "not_found", "Resource not found", e.Message),
            DomainConflictException e => (StatusCodes.Status409Conflict, e.Code, "Conflict", e.Message),
            DomainException e => (StatusCodes.Status400BadRequest, e.Code, "Business rule violated", e.Message),
            RequestValidationException e => (StatusCodes.Status400BadRequest, e.Code, "Invalid request", e.Message),
            ConcurrencyException => (StatusCodes.Status409Conflict, "concurrency_conflict", "Conflict",
                "The resource was modified by another request. Reload and try again."),
            _ => null,
        };

        if (mapped is not { } m) return false;

        logger.LogInformation("Request failed with {Status} {Code}: {Detail}", m.status, m.code, m.detail);

        httpContext.Response.StatusCode = m.status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = m.status,
                Title = m.title,
                Detail = m.detail,
                Extensions = { ["code"] = m.code },
            },
        });
    }
}
