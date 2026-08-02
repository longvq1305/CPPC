using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolygonAiBuilder.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "ModelCacheEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ModelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    CapabilitiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    RefreshedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProblemProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InternalName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, collation: "NOCASE"),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentScreen = table.Column<int>(type: "INTEGER", nullable: false),
                    PolygonProblemId = table.Column<long>(type: "INTEGER", nullable: true),
                    PolygonRevision = table.Column<int>(type: "INTEGER", nullable: true),
                    PolygonSyncPhase = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SelectedProvider = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SelectedModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameAvailableCheckedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemProjects", x => x.Id);
                    table.CheckConstraint("CK_ProblemProjects_CurrentScreen", "CurrentScreen BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "CodeArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedFromStatementVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCompileStatus = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    LastCompileOutput = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeArtifacts_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RollingSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Conversations_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GeneralInfos",
                columns: table => new
                {
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InputFile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OutputFile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TimeLimitMs = table.Column<int>(type: "INTEGER", nullable: false),
                    MemoryLimitMb = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneralInfos", x => x.ProblemProjectId);
                    table.CheckConstraint("CK_GeneralInfos_DifferentFiles", "lower(InputFile) <> lower(OutputFile)");
                    table.CheckConstraint("CK_GeneralInfos_MemoryLimit", "MemoryLimitMb BETWEEN 4 AND 1024");
                    table.CheckConstraint("CK_GeneralInfos_TimeLimit", "TimeLimitMs BETWEEN 250 AND 15000 AND TimeLimitMs % 50 = 0");
                    table.ForeignKey(
                        name: "FK_GeneralInfos_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Samples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    SolutionVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GeneratorVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InputIsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    OutputIsStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    WasManuallyEdited = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Samples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Samples_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Statements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Legend = table.Column<string>(type: "TEXT", nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCodeStale = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Statements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Statements_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RemoteResultSummary = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    RetryCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncOperationLogs_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TestsetName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TestCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ScorePerTest = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PointsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Checker = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Script = table.Column<string>(type: "TEXT", nullable: false),
                    SampleTestIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    UseSampleInStatement = table.Column<bool>(type: "INTEGER", nullable: false),
                    CommitMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestConfigurations", x => x.Id);
                    table.CheckConstraint("CK_TestConfigurations_Score", "ScorePerTest >= 0");
                    table.CheckConstraint("CK_TestConfigurations_TestCount", "TestCount BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "FK_TestConfigurations_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CodeArtifactVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CodeArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CompileStatus = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CompilerOutput = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodeArtifactVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodeArtifactVersions_CodeArtifacts_CodeArtifactId",
                        column: x => x.CodeArtifactId,
                        principalTable: "CodeArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConversationMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ContentMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ParentMessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProviderResponseId = table.Column<string>(type: "TEXT", nullable: true),
                    StructuredActionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    StatementVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ErrorDetails = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationMessages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatementVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StatementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Legend = table.Column<string>(type: "TEXT", nullable: false),
                    Input = table.Column<string>(type: "TEXT", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: false),
                    ChangedBy = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatementVersions_Statements_StatementId",
                        column: x => x.StatementId,
                        principalTable: "Statements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProblemProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ExtractedTextPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ProviderFileId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_ConversationMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ConversationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Attachments_ProblemProjects_ProblemProjectId",
                        column: x => x.ProblemProjectId,
                        principalTable: "ProblemProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_MessageId",
                table: "Attachments",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ProblemProjectId",
                table: "Attachments",
                column: "ProblemProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_Sha256",
                table: "Attachments",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_CodeArtifacts_ProblemProjectId_Type",
                table: "CodeArtifacts",
                columns: new[] { "ProblemProjectId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodeArtifactVersions_CodeArtifactId_VersionNumber",
                table: "CodeArtifactVersions",
                columns: new[] { "CodeArtifactId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationMessages_ConversationId_CreatedAt",
                table: "ConversationMessages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProblemProjectId",
                table: "Conversations",
                column: "ProblemProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelCacheEntries_Provider_ModelId",
                table: "ModelCacheEntries",
                columns: new[] { "Provider", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProblemProjects_InternalName",
                table: "ProblemProjects",
                column: "InternalName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Samples_ProblemProjectId_TestIndex",
                table: "Samples",
                columns: new[] { "ProblemProjectId", "TestIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Statements_ProblemProjectId",
                table: "Statements",
                column: "ProblemProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatementVersions_StatementId_VersionNumber",
                table: "StatementVersions",
                columns: new[] { "StatementId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperationLogs_ProblemProjectId_StartedAt",
                table: "SyncOperationLogs",
                columns: new[] { "ProblemProjectId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TestConfigurations_ProblemProjectId",
                table: "TestConfigurations",
                column: "ProblemProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationSettings");

            migrationBuilder.DropTable(
                name: "Attachments");

            migrationBuilder.DropTable(
                name: "CodeArtifactVersions");

            migrationBuilder.DropTable(
                name: "GeneralInfos");

            migrationBuilder.DropTable(
                name: "ModelCacheEntries");

            migrationBuilder.DropTable(
                name: "Samples");

            migrationBuilder.DropTable(
                name: "StatementVersions");

            migrationBuilder.DropTable(
                name: "SyncOperationLogs");

            migrationBuilder.DropTable(
                name: "TestConfigurations");

            migrationBuilder.DropTable(
                name: "ConversationMessages");

            migrationBuilder.DropTable(
                name: "CodeArtifacts");

            migrationBuilder.DropTable(
                name: "Statements");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "ProblemProjects");
        }
    }
}
