// Real file: Gringotts.Application/Common/Behaviors/ValidationBehavior.cs
// Registered once in the MediatR pipeline — every Command/Query gets validated
// automatically before its handler ever runs; no handler calls a validator itself.

using FluentValidation;
using MediatR;

namespace Gringotts.Application.Common.Behaviors;

public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var context = new ValidationContext<TRequest>(request);

        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(result => result.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0) throw new ValidationException(failures);

        return await next(cancellationToken);
    }
}

// Companion behavior, same idea for observability: Gringotts.Application/Common/Behaviors/LoggingBehavior.cs
public class LoggingBehavior<TRequest, TResponse>(Microsoft.Extensions.Logging.ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("{RequestName}: {@Request}", requestName, request);
        try
        {
            var response = await next(cancellationToken);
            logger.LogInformation("{RequestName} executed successfully", requestName);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{RequestName}: {Message}", requestName, ex.Message);
            throw;
        }
    }
}
