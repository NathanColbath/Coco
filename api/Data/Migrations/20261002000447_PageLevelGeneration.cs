using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicStudio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PageLevelGeneration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NegativePrompt",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PageImageId",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PositivePrompt",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptSnapshotJson",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Seed",
                table: "ComicPages",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NegativePrompt",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "PageImageId",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "PositivePrompt",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "PromptSnapshotJson",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "Seed",
                table: "ComicPages");
        }
    }
}
