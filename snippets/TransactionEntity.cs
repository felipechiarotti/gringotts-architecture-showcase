// Trimmed excerpt of Gringotts.Domain/Entities/Transaction.cs
// Every setter is private; state only changes through named, intention-revealing methods.

using Gringotts.Domain.Enums;
using Gringotts.Domain.Interfaces;

namespace Gringotts.Domain.Entities;

public class Transaction : BaseEntity, ISoftDeletable
{
    public Guid UserId { get; private set; }
    public Guid SubCategoryId { get; private set; }
    public Guid PaymentMethodId { get; private set; }
    public TransactionType Type { get; private set; }
    public TransactionStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Description { get; private set; } = null!;
    public DateOnly Date { get; private set; }
    public bool IsDeleted { get; private set; }

    // No public constructor — an instance can only come from Create(), which is the
    // single place that decides what a "valid" Transaction looks like at the moment of birth.
    public static Transaction Create(
        Guid userId, Guid subCategoryId, Guid paymentMethodId,
        TransactionType type, TransactionStatus status, decimal amount,
        string description, DateOnly date)
    {
        return new Transaction
        {
            UserId = userId,
            SubCategoryId = subCategoryId,
            PaymentMethodId = paymentMethodId,
            Type = type,
            Status = status,
            Amount = amount,
            Description = description,
            Date = date
        };
    }

    // State transitions are named methods, not property assignments — the intent is in the
    // method name, and it's the only place that transition can happen from.
    public void ConfirmPayment() => Status = TransactionStatus.Paid;
    public void MarkOverdue() => Status = TransactionStatus.Overdue;

    public void Update(Guid subCategoryId, Guid paymentMethodId, TransactionType type,
        decimal amount, string description, DateOnly date)
    {
        SubCategoryId = subCategoryId;
        PaymentMethodId = paymentMethodId;
        Type = type;
        Amount = amount;
        Description = description;
        Date = date;
    }
}
