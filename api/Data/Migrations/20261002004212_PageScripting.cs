using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicStudio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PageScripting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DialogueLines_Panels_PanelId",
                table: "DialogueLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PanelGenerations_Panels_PanelId",
                table: "PanelGenerations");

            migrationBuilder.DropTable(
                name: "PanelCharacters");

            migrationBuilder.Sql("DELETE FROM DialogueLines;");

migrationBuilder.DropTable(
                name: "Panels");

            migrationBuilder.DropIndex(
                name: "IX_PanelGenerations_PanelId_CreatedAt",
                table: "PanelGenerations");

            migrationBuilder.DropColumn(
                name: "PanelId",
                table: "PanelGenerations");

            migrationBuilder.DropColumn(
                name: "TailX",
                table: "DialogueLines");

            migrationBuilder.DropColumn(
                name: "TailY",
                table: "DialogueLines");

            migrationBuilder.DropColumn(
                name: "Width",
                table: "DialogueLines");

            migrationBuilder.DropColumn(
                name: "X",
                table: "DialogueLines");

            migrationBuilder.DropColumn(
                name: "Y",
                table: "DialogueLines");

            migrationBuilder.RenameColumn(
                name: "StyleJson",
                table: "DialogueLines",
                newName: "Position");

            migrationBuilder.RenameColumn(
                name: "PanelId",
                table: "DialogueLines",
                newName: "PageId");

            migrationBuilder.RenameIndex(
                name: "IX_DialogueLines_PanelId",
                table: "DialogueLines",
                newName: "IX_DialogueLines_PageId");

            migrationBuilder.AddColumn<bool>(
                name: "IsLegacy",
                table: "ComicProjects",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "StyleAnchorImageId",
                table: "ComicProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Continuity",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SceneTitle",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Script",
                table: "ComicPages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PanelGenerations_Kind_TargetId",
                table: "PanelGenerations",
                columns: new[] { "Kind", "TargetId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DialogueLines_ComicPages_PageId",
                table: "DialogueLines",
                column: "PageId",
                principalTable: "ComicPages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DialogueLines_ComicPages_PageId",
                table: "DialogueLines");

            migrationBuilder.DropIndex(
                name: "IX_PanelGenerations_Kind_TargetId",
                table: "PanelGenerations");

            migrationBuilder.DropColumn(
                name: "IsLegacy",
                table: "ComicProjects");

            migrationBuilder.DropColumn(
                name: "StyleAnchorImageId",
                table: "ComicProjects");

            migrationBuilder.DropColumn(
                name: "Continuity",
                table: "ComicPages");

            migrationBuilder.DropColumn(
                name: "SceneTitle",
                table: "ComicPages");

            migrationBuilder.Sql("UPDATE ComicProjects SET IsLegacy = 1 WHERE Id IN (SELECT DISTINCT ProjectId FROM ComicPages);");

            migrationBuilder.DropColumn(
                name: "Script",
                table: "ComicPages");

            migrationBuilder.RenameColumn(
                name: "Position",
                table: "DialogueLines",
                newName: "StyleJson");

            migrationBuilder.RenameColumn(
                name: "PageId",
                table: "DialogueLines",
                newName: "PanelId");

            migrationBuilder.RenameIndex(
                name: "IX_DialogueLines_PageId",
                table: "DialogueLines",
                newName: "IX_DialogueLines_PanelId");

            migrationBuilder.AddColumn<Guid>(
                name: "PanelId",
                table: "PanelGenerations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TailX",
                table: "DialogueLines",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TailY",
                table: "DialogueLines",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Width",
                table: "DialogueLines",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "X",
                table: "DialogueLines",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Y",
                table: "DialogueLines",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.CreateTable(
                name: "Panels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: true),
                    CameraNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ContinuityNotes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Mood = table.Column<string>(type: "TEXT", nullable: true),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    PositivePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    PromptSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    Seed = table.Column<long>(type: "INTEGER", nullable: true),
                    SelectedImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Setting = table.Column<string>(type: "TEXT", nullable: true),
                    Shot = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeOfDay = table.Column<string>(type: "TEXT", nullable: true),
                    Rect = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Panels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Panels_ComicPages_PageId",
                        column: x => x.PageId,
                        principalTable: "ComicPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PanelCharacters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PanelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Expression = table.Column<string>(type: "TEXT", nullable: true),
                    ExtraTags = table.Column<string>(type: "TEXT", nullable: true),
                    OutfitOverride = table.Column<string>(type: "TEXT", nullable: true),
                    Pose = table.Column<string>(type: "TEXT", nullable: true),
                    PositionX = table.Column<double>(type: "REAL", nullable: false),
                    PositionY = table.Column<double>(type: "REAL", nullable: false),
                    Prominence = table.Column<int>(type: "INTEGER", nullable: false),
                    UseReference = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelCharacters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelCharacters_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PanelCharacters_Panels_PanelId",
                        column: x => x.PanelId,
                        principalTable: "Panels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PanelGenerations_PanelId_CreatedAt",
                table: "PanelGenerations",
                columns: new[] { "PanelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PanelCharacters_CharacterId",
                table: "PanelCharacters",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelCharacters_PanelId",
                table: "PanelCharacters",
                column: "PanelId");

            migrationBuilder.CreateIndex(
                name: "IX_Panels_PageId_Index",
                table: "Panels",
                columns: new[] { "PageId", "Index" });

            migrationBuilder.AddForeignKey(
                name: "FK_DialogueLines_Panels_PanelId",
                table: "DialogueLines",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PanelGenerations_Panels_PanelId",
                table: "PanelGenerations",
                column: "PanelId",
                principalTable: "Panels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
