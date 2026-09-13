using System.Threading.Tasks;

namespace STNG.Data;

public interface ISTNGDbSchemaMigrator
{
    Task MigrateAsync();
}
