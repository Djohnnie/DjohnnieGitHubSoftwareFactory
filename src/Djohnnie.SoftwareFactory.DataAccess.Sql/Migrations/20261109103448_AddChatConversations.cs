using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CopilotKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WorkItemId = table.Column<int>(type: "int", nullable: true),
                    WorkItemTemplateId = table.Column<int>(type: "int", nullable: true),
                    AgentTemplateId = table.Column<int>(type: "int", nullable: true),
                    AgentId = table.Column<int>(type: "int", nullable: true),
                    RepositoryId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                    table.CheckConstraint("CK_ChatConversations_ExactlyOneOwner", "(CASE WHEN [WorkItemId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [WorkItemTemplateId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AgentTemplateId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [AgentId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [RepositoryId] IS NULL THEN 0 ELSE 1 END) = 1");
                    table.ForeignKey(
                        name: "FK_ChatConversations_AgentTemplates_AgentTemplateId",
                        column: x => x.AgentTemplateId,
                        principalTable: "AgentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatConversations_Agents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "Agents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatConversations_Repositories_RepositoryId",
                        column: x => x.RepositoryId,
                        principalTable: "Repositories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatConversations_WorkItemTemplates_WorkItemTemplateId",
                        column: x => x.WorkItemTemplateId,
                        principalTable: "WorkItemTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatConversations_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    DisplayText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsError = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_AgentId",
                table: "ChatConversations",
                column: "AgentId",
                unique: true,
                filter: "[AgentId] IS NOT NULL AND [ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_AgentTemplateId",
                table: "ChatConversations",
                column: "AgentTemplateId",
                unique: true,
                filter: "[AgentTemplateId] IS NOT NULL AND [ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_RepositoryId",
                table: "ChatConversations",
                column: "RepositoryId",
                unique: true,
                filter: "[RepositoryId] IS NOT NULL AND [ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_WorkItemId",
                table: "ChatConversations",
                column: "WorkItemId",
                unique: true,
                filter: "[WorkItemId] IS NOT NULL AND [ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_WorkItemTemplateId",
                table: "ChatConversations",
                column: "WorkItemTemplateId",
                unique: true,
                filter: "[WorkItemTemplateId] IS NOT NULL AND [ClosedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_Sequence",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatConversations");
        }
    }
}
