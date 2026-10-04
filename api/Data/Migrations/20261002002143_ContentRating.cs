using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicStudio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContentRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rating",
                table: "ComicProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rating",
                table: "ComicProjects");
        }
    }
}
