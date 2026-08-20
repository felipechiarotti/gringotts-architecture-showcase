// Excerpt of Gringotts.Infrastructure/Data/AppDbContext.cs
// A repository calling Delete()/Remove() on an ISoftDeletable entity never actually
// issues a SQL DELETE — this override quietly rewrites it into an UPDATE first.

using Gringotts.Domain.Entities;
using Gringotts.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Gringotts.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        // Every IEntityTypeConfiguration also applies:
        //   builder.HasQueryFilter(e => !e.IsDeleted);
        // so soft-deleted rows are invisible to every query by default, everywhere.
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        HandleBaseEntity();
        return await base.SaveChangesAsync(ct);
    }

    private void HandleBaseEntity()
    {
        // Intercept anything EF Core marked for deletion...
        var softDeletableEntries = ChangeTracker.Entries<ISoftDeletable>()
            .Where(e => e.State == EntityState.Deleted);

        foreach (var entry in softDeletableEntries)
        {
            // ...and turn it into a flag update instead of a real row deletion.
            entry.Property(e => e.IsDeleted).CurrentValue = true;
            entry.State = EntityState.Modified;
        }

        // Also auto-stamps CreatedAt/ModifiedAt on every BaseEntity — no handler
        // ever sets these manually, so they can't be forgotten or set wrong.
        var entries = ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
                entry.Property(e => e.CreatedAt).CurrentValue = DateTime.UtcNow;
            if (entry.State == EntityState.Modified)
                entry.Property(e => e.ModifiedAt).CurrentValue = DateTime.UtcNow;
        }
    }
}
