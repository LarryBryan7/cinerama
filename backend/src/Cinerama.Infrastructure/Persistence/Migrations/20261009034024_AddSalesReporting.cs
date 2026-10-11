using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cinerama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La vista va primero: el procedimiento la usa.
            migrationBuilder.Sql(SqlResources.Read("vw_ShowtimeSales.sql"));
            migrationBuilder.Sql(SqlResources.Read("sp_GetSalesSummary.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.sp_GetSalesSummary;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS dbo.vw_ShowtimeSales;");
        }
    }
}
