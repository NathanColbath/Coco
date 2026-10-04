using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicStudio.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ComicPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Bio = table.Column<string>(type: "TEXT", nullable: true),
                    Personality = table.Column<string>(type: "TEXT", nullable: true),
                    SpeechStyle = table.Column<string>(type: "TEXT", nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Visual = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComicProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    StoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Premise = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorMode = table.Column<int>(type: "INTEGER", nullable: false),
                    PageWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    PageHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    DialogueAdaptation = table.Column<int>(type: "INTEGER", nullable: false),
                    CoverImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Style = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicProjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComicProjects_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComicProjects_Stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "Stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImageAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    StoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceMetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageAssets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PageLayoutTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    PanelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Gutter = table.Column<double>(type: "REAL", nullable: false),
                    Margin = table.Column<double>(type: "REAL", nullable: false),
                    BorderThickness = table.Column<double>(type: "REAL", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    SlotsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageLayoutTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComicPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    LayoutTemplateId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    RenderImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComicPages_ComicProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ComicProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CoverPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: true),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    ImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Seed = table.Column<long>(type: "INTEGER", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", nullable: true),
                    TitleText = table.Column<string>(type: "TEXT", nullable: false),
                    TitleFont = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TitleX = table.Column<double>(type: "REAL", nullable: false),
                    TitleY = table.Column<double>(type: "REAL", nullable: false),
                    TitleMaxWidth = table.Column<double>(type: "REAL", nullable: false),
                    ExtraText = table.Column<string>(type: "TEXT", nullable: true),
                    RenderedImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoverPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CoverPlans_ComicProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ComicProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CurationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RawOutputJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CurationRuns_ComicProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ComicProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CharacterSheetImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PromptUsed = table.Column<string>(type: "TEXT", nullable: true),
                    Seed = table.Column<long>(type: "INTEGER", nullable: true),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterSheetImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CharacterSheetImages_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CharacterSheetImages_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Panels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Shot = table.Column<int>(type: "INTEGER", nullable: false),
                    CameraNotes = table.Column<string>(type: "TEXT", nullable: true),
                    Setting = table.Column<string>(type: "TEXT", nullable: true),
                    TimeOfDay = table.Column<string>(type: "TEXT", nullable: true),
                    Mood = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: true),
                    ContinuityNotes = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PositivePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    NegativePrompt = table.Column<string>(type: "TEXT", nullable: true),
                    PromptSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    Seed = table.Column<long>(type: "INTEGER", nullable: true),
                    SelectedImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "DialogueLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PanelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SpeakerCharacterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    X = table.Column<double>(type: "REAL", nullable: false),
                    Y = table.Column<double>(type: "REAL", nullable: false),
                    Width = table.Column<double>(type: "REAL", nullable: false),
                    TailX = table.Column<double>(type: "REAL", nullable: false),
                    TailY = table.Column<double>(type: "REAL", nullable: false),
                    StyleJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DialogueLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DialogueLines_Characters_SpeakerCharacterId",
                        column: x => x.SpeakerCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DialogueLines_Panels_PanelId",
                        column: x => x.PanelId,
                        principalTable: "Panels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PanelCharacters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PanelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Expression = table.Column<string>(type: "TEXT", nullable: true),
                    Pose = table.Column<string>(type: "TEXT", nullable: true),
                    OutfitOverride = table.Column<string>(type: "TEXT", nullable: true),
                    ExtraTags = table.Column<string>(type: "TEXT", nullable: true),
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

            migrationBuilder.CreateTable(
                name: "PanelGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    PanelId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SheetKind = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestJson = table.Column<string>(type: "TEXT", nullable: false),
                    Seed = table.Column<long>(type: "INTEGER", nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Steps = table.Column<int>(type: "INTEGER", nullable: false),
                    Scale = table.Column<double>(type: "REAL", nullable: false),
                    IsHighResolution = table.Column<bool>(type: "INTEGER", nullable: false),
                    CostAnlas = table.Column<int>(type: "INTEGER", nullable: true),
                    WasFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageAssetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PanelGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PanelGenerations_ImageAssets_ImageAssetId",
                        column: x => x.ImageAssetId,
                        principalTable: "ImageAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PanelGenerations_Panels_PanelId",
                        column: x => x.PanelId,
                        principalTable: "Panels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Characters_StoryId",
                table: "Characters",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSheetImages_CharacterId",
                table: "CharacterSheetImages",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_CharacterSheetImages_ImageAssetId",
                table: "CharacterSheetImages",
                column: "ImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_ComicPages_ProjectId_Index",
                table: "ComicPages",
                columns: new[] { "ProjectId", "Index" });

            migrationBuilder.CreateIndex(
                name: "IX_ComicProjects_StoryId",
                table: "ComicProjects",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ComicProjects_UserId",
                table: "ComicProjects",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CoverPlans_ProjectId",
                table: "CoverPlans",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CurationRuns_ProjectId",
                table: "CurationRuns",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DialogueLines_PanelId",
                table: "DialogueLines",
                column: "PanelId");

            migrationBuilder.CreateIndex(
                name: "IX_DialogueLines_SpeakerCharacterId",
                table: "DialogueLines",
                column: "SpeakerCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_ProjectId_Kind",
                table: "ImageAssets",
                columns: new[] { "ProjectId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_Sha256",
                table: "ImageAssets",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_ImageAssets_UserId",
                table: "ImageAssets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelCharacters_CharacterId",
                table: "PanelCharacters",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelCharacters_PanelId",
                table: "PanelCharacters",
                column: "PanelId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelGenerations_ImageAssetId",
                table: "PanelGenerations",
                column: "ImageAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PanelGenerations_PanelId_CreatedAt",
                table: "PanelGenerations",
                columns: new[] { "PanelId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Panels_PageId_Index",
                table: "Panels",
                columns: new[] { "PageId", "Index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CharacterSheetImages");

            migrationBuilder.DropTable(
                name: "CoverPlans");

            migrationBuilder.DropTable(
                name: "CurationRuns");

            migrationBuilder.DropTable(
                name: "DialogueLines");

            migrationBuilder.DropTable(
                name: "PageLayoutTemplates");

            migrationBuilder.DropTable(
                name: "PanelCharacters");

            migrationBuilder.DropTable(
                name: "PanelGenerations");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "ImageAssets");

            migrationBuilder.DropTable(
                name: "Panels");

            migrationBuilder.DropTable(
                name: "ComicPages");

            migrationBuilder.DropTable(
                name: "ComicProjects");
        }
    }
}
