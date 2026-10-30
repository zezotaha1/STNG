using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using STNG.Families;
using Volo.Abp;
using Volo.Abp.Data;
using MembershipHistoryRecord = STNG.Families.FamilyMembershipHistory;

namespace STNG.EntityFrameworkCore;

public partial class STNGDbContext
{
    private async Task<int> SaveFamilyChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken)
    {
        ChangeTracker.DetectChanges();
        var families = ChangeTracker.Entries<Family>().Where(x => x.State == EntityState.Modified)
            .OrderBy(x => x.Entity.Id).ToList();
        var hasFamilyChanges = ChangeTracker.Entries().Any(x => (x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted) &&
            x.Entity is Family or FamilyMember or OwnershipTransfer or MembershipHistoryRecord);

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is Family or FamilyMember or OwnershipTransfer or MembershipHistoryRecord)
                throw new InvalidOperationException("Family records must be retained; use the domain lifecycle.");
            if (entry.State == EntityState.Modified && entry.Entity is MembershipHistoryRecord)
                throw new InvalidOperationException("Membership history is append-only.");
            Guid? familyId = entry.Entity switch
            {
                FamilyMember member => member.FamilyId,
                OwnershipTransfer transfer => transfer.FamilyId,
                MembershipHistoryRecord history => history.FamilyId,
                _ => null
            };
            if (familyId.HasValue && (entry.State is EntityState.Added or EntityState.Modified) &&
                !ChangeTracker.Entries<Family>().Any(x => x.Entity.Id == familyId &&
                    (x.State == EntityState.Added || x.Property(f => f.Version).IsModified)))
                throw new InvalidOperationException("Family children must be changed through their aggregate root.");
        }

        // Serialize writers at the root before touching children. Keep the original version for
        // SaveChanges' concurrency check; the no-op update acquires a transaction-scoped write lock.
        var transaction = Database.CurrentTransaction;
        var ownsTransaction = families.Count > 0 && transaction == null;
        var savepoint = "Family_" + Guid.NewGuid().ToString("N")[..24];
        var hasSavepoint = families.Count > 0 && transaction?.SupportsSavepoints == true;
        if (ownsTransaction) transaction = await Database.BeginTransactionAsync(cancellationToken);
        if (hasSavepoint) await transaction!.CreateSavepointAsync(savepoint, cancellationToken);
        try
        {
            foreach (var entry in families)
            {
                var id = entry.Entity.Id;
                var version = entry.Property(x => x.Version).OriginalValue;
                var stamp = entry.Property(x => x.ConcurrencyStamp).OriginalValue;
                var affected = await Families.Where(x => x.Id == id && x.Version == version && x.ConcurrencyStamp == stamp)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Version, version), cancellationToken);
                if (affected != 1) throw new AbpDbConcurrencyException("The family was changed by another request.");
            }

            // SQL Server checks filtered unique indexes per statement. Demote the old owner
            // first, in the SAME transaction, so EF's member-update ordering cannot create two owners.
            foreach (var entry in ChangeTracker.Entries<FamilyMember>().Where(x => x.State == EntityState.Modified &&
                         x.Property(m => m.Role).OriginalValue == FamilyMemberRole.Owner &&
                         x.Entity.Role != FamilyMemberRole.Owner))
            {
                var id = entry.Entity.Id;
                var affected = await FamilyMembers.Where(x => x.Id == id && x.Role == FamilyMemberRole.Owner)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Role, FamilyMemberRole.Admin), cancellationToken);
                if (affected != 1) throw new AbpDbConcurrencyException("The family owner has changed.");
            }

            // Retire a terminal/expired request before inserting its replacement into the
            // filtered unique pending-transfer index.
            foreach (var entry in ChangeTracker.Entries<OwnershipTransfer>().Where(x => x.State == EntityState.Modified &&
                         x.Property(t => t.Status).OriginalValue == OwnershipTransferStatus.Pending &&
                         x.Entity.Status != OwnershipTransferStatus.Pending))
            {
                var id = entry.Entity.Id;
                var status = entry.Entity.Status;
                var affected = await OwnershipTransfers.Where(x => x.Id == id && x.Status == OwnershipTransferStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, status), cancellationToken);
                if (affected != 1) throw new AbpDbConcurrencyException("The ownership transfer has changed.");
            }

            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            if (ownsTransaction) await transaction!.CommitAsync(cancellationToken);
            if (hasSavepoint) await transaction!.ReleaseSavepointAsync(savepoint, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            // SQL Server has already rolled back a deadlock victim's transaction.
            var deadlock = exception is SqlException { Number: 1205 } or
                DbUpdateException { InnerException: SqlException { Number: 1205 } };
            if (!deadlock)
            {
                if (ownsTransaction) await transaction!.RollbackAsync(CancellationToken.None);
                else if (hasSavepoint) await transaction!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
            }
            if (hasFamilyChanges && (exception is AbpDbConcurrencyException or DbUpdateConcurrencyException ||
                exception is SqlException { Number: 2601 or 2627 or 1205 } ||
                exception is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 or 1205 } }))
                throw new BusinessException(FamilyErrorCodes.ConcurrencyConflict, innerException: exception);
            throw;
        }
        finally
        {
            if (ownsTransaction) await transaction!.DisposeAsync();
        }
    }
}
