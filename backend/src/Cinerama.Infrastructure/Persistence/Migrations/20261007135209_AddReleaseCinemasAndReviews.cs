using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cinerama.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseCinemasAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_CinemaRooms_Name",
                table: "CinemaRooms");

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PreSaleStartsOn",
                table: "Movies",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReleaseDate",
                table: "Movies",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Las películas que ya existían se consideran estrenadas el día que se crearon (sin preventa).
            migrationBuilder.Sql("UPDATE Movies SET ReleaseDate = CAST(CreatedAt AS date)");

            migrationBuilder.AddColumn<string>(
                name: "TrailerUrl",
                table: "Movies",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CinemaId",
                table: "CinemaRooms",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Cinemas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cinemas", x => x.Id);
                    table.CheckConstraint("CK_Cinemas_Latitude", "[Latitude] BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_Cinemas_Longitude", "[Longitude] BETWEEN -180 AND 180");
                });

            // Una base que ya tenía salas pasa a tener un cine y todas sus salas dentro. Una base vacía no recibe nada: el seed crea los cines.
            migrationBuilder.Sql(@"
                INSERT INTO Cinemas (Name, Address, City, Region, Latitude, Longitude, LogoUrl, IsActive)
                SELECT N'Cinerama - Chimbote', N'Av. José Pardo 123', N'Chimbote', N'Áncash', -9.074500, -78.593600, NULL, 1
                WHERE EXISTS (SELECT 1 FROM CinemaRooms)");
            migrationBuilder.Sql("UPDATE CinemaRooms SET CinemaId = (SELECT TOP 1 Id FROM Cinemas ORDER BY Id) WHERE CinemaId = 0");

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.CheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_Reviews_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reviews_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Movies_PreSale",
                table: "Movies",
                sql: "[PreSaleStartsOn] IS NULL OR [PreSaleStartsOn] <= [ReleaseDate]");

            migrationBuilder.CreateIndex(
                name: "UX_CinemaRooms_Cinema_Name",
                table: "CinemaRooms",
                columns: new[] { "CinemaId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cinemas_IsActive_City",
                table: "Cinemas",
                columns: new[] { "IsActive", "City" });

            migrationBuilder.CreateIndex(
                name: "UX_Cinemas_Name",
                table: "Cinemas",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_Movie_CreatedAt",
                table: "Reviews",
                columns: new[] { "MovieId", "CreatedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserId",
                table: "Reviews",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_Reviews_Movie_User",
                table: "Reviews",
                columns: new[] { "MovieId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CinemaRooms_Cinemas_CinemaId",
                table: "CinemaRooms",
                column: "CinemaId",
                principalTable: "Cinemas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CinemaRooms_Cinemas_CinemaId",
                table: "CinemaRooms");

            migrationBuilder.DropTable(
                name: "Cinemas");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Movies_PreSale",
                table: "Movies");

            migrationBuilder.DropIndex(
                name: "UX_CinemaRooms_Cinema_Name",
                table: "CinemaRooms");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreSaleStartsOn",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "TrailerUrl",
                table: "Movies");

            migrationBuilder.DropColumn(
                name: "CinemaId",
                table: "CinemaRooms");

            migrationBuilder.CreateIndex(
                name: "UX_CinemaRooms_Name",
                table: "CinemaRooms",
                column: "Name",
                unique: true);
        }
    }
}
