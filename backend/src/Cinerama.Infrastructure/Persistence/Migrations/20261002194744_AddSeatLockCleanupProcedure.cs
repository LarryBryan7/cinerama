using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cinerama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatLockCleanupProcedure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El procedimiento vive como archivo SQL versionado (Persistence/Sql) y se aplica con el esquema.
            migrationBuilder.Sql(SqlResources.Read("sp_ReleaseExpiredSeatLocks.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE dbo.sp_ReleaseExpiredSeatLocks;");
        }
    }
}
