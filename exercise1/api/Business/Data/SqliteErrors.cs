using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace StargateAPI.Business.Data
{
    /// <summary>SQLite-specific and transitional (like StarbaseDatabase): replace when the project moves to SQL Server.</summary>
    public static class SqliteErrors
    {
        /// <summary>SQLITE_CONSTRAINT_UNIQUE.</summary>
        public const int UniqueConstraintExtendedCode = 2067;

        public static bool IsUniqueViolation(DbUpdateException exception) =>
            exception.InnerException is SqliteException { SqliteExtendedErrorCode: UniqueConstraintExtendedCode };
    }
}
