// Real pattern from Gringotts.Application/Features/Goals/Commands/DeleteGoalCommandHandler.cs
// The same ownership-check shape is repeated across every resource type (transactions, goals,
// payment methods, categories, templates, requests) — 30+ handlers, one consistent rule.

namespace Gringotts.Application.Features.Goals.Commands;

public record DeleteGoalCommand(Guid Id) : IRequest<bool>;

public class DeleteGoalCommandHandler(
    ILogger<DeleteGoalCommandHandler> logger,
    IGoalRepository goalRepository,
    ICurrentUserService currentUserService) : IRequestHandler<DeleteGoalCommand, bool>
{
    public async Task<bool> Handle(DeleteGoalCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId;
        var goal = await goalRepository.GetByIdAsync(request.Id, cancellationToken)
                   ?? throw new NotFoundException($"Goal {request.Id} not found");

        // The ID in the URL is never trusted on its own — ownership is re-checked here,
        // every time, regardless of what the caller claims to be requesting.
        if (goal.UserId != userId)
            throw new UnauthorizedException("You do not own this goal.");

        goalRepository.Delete(goal);
        logger.LogInformation("Goal {GoalId} deleted", goal.Id);
        return await goalRepository.SaveChangesAsync(cancellationToken) > 0;
    }
}

// The user identity itself never comes from a request parameter — only from the validated JWT:
public interface ICurrentUserService
{
    Guid UserId { get; }
    void SetUserId(Guid userId);
}
