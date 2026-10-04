using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicStudio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnvironmentsAndScenes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnvironmentDefaults",
                table: "ComicProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExplicitImageModelId",
                table: "ComicProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ImageModelPolicy",
                table: "ComicProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaletteNotes",
                table: "ComicProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EnvironmentId",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelOverride",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelUsed",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MovesToEnvironmentId",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PanelPlanJson",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SceneId",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    VisualTags = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Lighting = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PaletteNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Features = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FirstSeenPage = table.Column<int>(type: "INTEGER", nullable: true),
                    PlateImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Environments_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Scenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MovesToEnvironmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TimeOfDay = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Mood = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Enters = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Leaves = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PageStart = table.Column<int>(type: "INTEGER", nullable: false),
                    PageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenes_ComicProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ComicProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentPlates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PromptUsed = table.Column<string>(type: "TEXT", nullable: true),
                    Seed = table.Column<long>(type: "INTEGER", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentPlates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnvironmentPlates_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EnvironmentPlates_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentPlates_EnvironmentId",
                table: "EnvironmentPlates",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EnvironmentPlates_ImageAssetId",
                table: "EnvironmentPlates",
                column: "ImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Environments_StoryId",
                table: "Environments",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Scenes_ProjectId_Index",
                table: "Scenes",
                columns: new[] { "ProjectId", "Index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnvironmentPlates");

            migrationBuilder.DropTable(
                name: "Scenes");

            migrationBuilder.DropTable(
                name: "Environments");

            migrationBuilder.DropColumn(
                name: "EnvironmentDefaults",
                table: "ComicProjects");

            migrationBuilder.DropColumn(
                name: "ExplicitImageModelId",
                table: "ComicProjects");

            migrationBuilder.DropColumn(
                name: "ImageModelPolicy",
                table: "ComicProjects");

            migrationBuilder.DropColumn(
                name: "PaletteNotes",
                table: "ComicProjects");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "ModelOverride",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "ModelUsed",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "MovesToEnvironmentId",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "PanelPlanJson",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "SceneId",
                table: "ComicPages");
        }
    }
}
