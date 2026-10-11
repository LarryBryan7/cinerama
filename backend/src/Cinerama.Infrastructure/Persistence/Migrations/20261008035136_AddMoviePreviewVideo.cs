using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cinerama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMoviePreviewVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviewVideoUrl",
                table: "Movies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviewVideoUrl",
                table: "Movies");
        }
    }
}
