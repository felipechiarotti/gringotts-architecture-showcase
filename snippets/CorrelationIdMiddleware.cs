// Real file: Gringotts.Api/Middlewares/CorrelationIdMiddleware.cs
// Reuses an inbound X-Correlation-Id if the caller sent one, otherwise mints a new GUID.
// Pushes it into Serilog's LogContext (every log line in this request carries it) and
// always echoes it back on the response, success or failure.

using Serilog.Context;

namespace Gringotts.Api.Middlewares;

public class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationIdHeader)
            ? correlationIdHeader.FirstOrDefault()
            : Guid.NewGuid().ToString();

        context.Items["CorrelationId"] = correlationId;

        // Registered via OnStarting (not set directly) so it still fires even when the
        // request ends through GlobalExceptionHandler's error path, not just the happy path.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
