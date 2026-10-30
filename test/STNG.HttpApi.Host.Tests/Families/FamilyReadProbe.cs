using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using STNG.Families;

namespace STNG;

// The host collection disables parallel tests. A single observation can span the
// TestServer request thread without relying on ExecutionContext propagation.
internal sealed class FamilyReadProbe : DbCommandInterceptor, IMaterializationInterceptor
{
    private Observation? _observation;

    public Observation Observe()
    {
        var observation = new Observation(this);
        if (Interlocked.CompareExchange(ref _observation, observation, null) != null)
            throw new InvalidOperationException("A family read observation is already active.");
        return observation;
    }

    private void Record(DbCommand command)
    {
        var observation = Volatile.Read(ref _observation);
        if (observation == null || !new[] { "[AppFamilies]", "[AppFamilyMembers]", "[AppOwnershipTransfers]", "[AppFamilyMembershipHistory]" }
                .Any(table => command.CommandText.Contains(table, StringComparison.Ordinal))) return;
        observation.Commands.Enqueue(new FamilyReadCommand(command.CommandText,
            command.Parameters.Cast<SqlParameter>().Select(parameter => (SqlParameter)((ICloneable)parameter).Clone()).ToArray()));
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is Family or FamilyMember or OwnershipTransfer or FamilyMembershipHistory)
            Volatile.Read(ref _observation)?.MaterializedEntities.Enqueue(entity.GetType());
        return entity;
    }

    internal sealed class Observation : IDisposable
    {
        private readonly FamilyReadProbe _probe;
        internal ConcurrentQueue<FamilyReadCommand> Commands { get; } = new();
        internal ConcurrentQueue<Type> MaterializedEntities { get; } = new();
        internal Observation(FamilyReadProbe probe) => _probe = probe;

        public void WriteSql(string directory, string name)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".sql"),
                string.Join(Environment.NewLine + Environment.NewLine, Commands.Select(command => command.Sql)));
        }

        public void Dispose() => Interlocked.CompareExchange(ref _probe._observation, null, this);
    }
}

internal sealed record FamilyReadCommand(string Sql, SqlParameter[] Parameters);
