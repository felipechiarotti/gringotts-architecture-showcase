// Real file: Gringotts.Api/Modules/Transactions/TransactionEndpoints.cs
// One static class per feature area, routes grouped under a prefix — no controller
// classes, no attribute routing. Every handler is a one-line MediatR dispatch.

using Gringotts.Application.Common.Models;
using Gringotts.Application.Features.Transactions.Commands;
using Gringotts.Application.Features.Transactions.Queries;
using MediatR;

namespace Gringotts.Api.Modules.Transactions;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").WithTags("Transactions");

        group.MapPost("/", async (CreateTransactionCommand command, ISender sender) =>
        {
            var transactionId = await sender.Send(command);
            return Results.Created($"/api/transactions/{transactionId}", null);
        });

        group.MapGet("/", async ([AsParameters] TransactionFilter filter, ISender sender) =>
        {
            var transactions = await sender.Send(new GetTransactionsQuery(filter));
            return Results.Ok(transactions);
        });

        group.MapGet("/{id:guid}", async (Guid id, ISender sender) =>
            Results.Ok(await sender.Send(new GetTransactionByIdQuery(id))));

        group.MapGet("/forecast", async ([AsParameters] TransactionFilter filter, ISender sender) =>
            Results.Ok(await sender.Send(new GetTransactionForecastQuery(filter))));

        group.MapPut("/{id:guid}", async (Guid id, UpdateTransactionCommand command, ISender sender) =>
        {
            await sender.Send(command with { TransactionId = id });
            return Results.NoContent();
        });

        group.MapPatch("/{id:guid}/confirm", async (Guid id, ISender sender) =>
        {
            await sender.Send(new ConfirmTransactionPaymentCommand(id));
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender) =>
        {
            await sender.Send(new DeleteTransactionCommand(id));
            return Results.NoContent();
        });
    }
}
