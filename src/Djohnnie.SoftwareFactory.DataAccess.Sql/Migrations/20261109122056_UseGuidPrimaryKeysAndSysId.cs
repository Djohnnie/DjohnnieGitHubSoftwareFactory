using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <summary>
    /// Gives every table a nonclustered GUID primary key (<c>Id</c>) and a clustered identity alternate key
    /// (<c>SysId</c>). Written by hand to keep the data: the old integer ids become <c>SysId</c>, and foreign keys are
    /// converted to the new GUIDs through them.
    /// </summary>
    public partial class UseGuidPrimaryKeysAndSysId : Migration
    {
        private const string ExactlyOneOwner =
            "(CASE WHEN [WorkItemId] IS NULL THEN 0 ELSE 1 END"
            + " + CASE WHEN [WorkItemTemplateId] IS NULL THEN 0 ELSE 1 END"
            + " + CASE WHEN [AgentTemplateId] IS NULL THEN 0 ELSE 1 END"
            + " + CASE WHEN [AgentId] IS NULL THEN 0 ELSE 1 END"
            + " + CASE WHEN [RepositoryId] IS NULL THEN 0 ELSE 1 END) = 1";

        private static readonly string[] AllTables =
        [
            "Agents", "AgentTemplates", "AiModels", "AuthSessions", "ChatConversations",
            "ChatMessages", "InternalAgents", "Repositories", "WorkItems", "WorkItemTemplates"
        ];

        /// <summary>The tables whose existing identity column simply becomes <c>SysId</c>.</summary>
        private static readonly string[] TablesWithIntIdentity =
        [
            "Agents", "AgentTemplates", "AiModels", "ChatConversations",
            "InternalAgents", "Repositories", "WorkItems", "WorkItemTemplates"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DropRelations(migrationBuilder);

            foreach (var table in AllTables)
            {
                migrationBuilder.DropPrimaryKey(name: $"PK_{table}", table: table);
            }

            foreach (var table in TablesWithIntIdentity)
            {
                migrationBuilder.RenameColumn(name: "Id", table: table, newName: "SysId");
            }

            // A table can only have one identity column, and this one is a bigint: it can't be kept as SysId.
            migrationBuilder.DropColumn(name: "Id", table: "ChatMessages");
            AddSysId(migrationBuilder, "ChatMessages");
            AddSysId(migrationBuilder, "AuthSessions");

            foreach (var table in AllTables)
            {
                AddGuidId(migrationBuilder, table);
            }

            ConvertForeignKey(migrationBuilder, "Agents", "RepositoryId", "Repositories", nullable: false);
            ConvertForeignKey(migrationBuilder, "WorkItems", "RepositoryId", "Repositories", nullable: false);
            ConvertForeignKey(migrationBuilder, "WorkItems", "ParentId", "WorkItems", nullable: true);
            ConvertForeignKey(migrationBuilder, "ChatConversations", "WorkItemId", "WorkItems", nullable: true);
            ConvertForeignKey(migrationBuilder, "ChatConversations", "WorkItemTemplateId", "WorkItemTemplates", nullable: true);
            ConvertForeignKey(migrationBuilder, "ChatConversations", "AgentTemplateId", "AgentTemplates", nullable: true);
            ConvertForeignKey(migrationBuilder, "ChatConversations", "AgentId", "Agents", nullable: true);
            ConvertForeignKey(migrationBuilder, "ChatConversations", "RepositoryId", "Repositories", nullable: true);
            ConvertForeignKey(migrationBuilder, "ChatMessages", "ConversationId", "ChatConversations", nullable: false);

            foreach (var table in AllTables)
            {
                migrationBuilder.AddUniqueConstraint(name: $"AK_{table}_SysId", table: table, column: "SysId")
                    .Annotation("SqlServer:Clustered", true);
                migrationBuilder.AddPrimaryKey(name: $"PK_{table}", table: table, column: "Id")
                    .Annotation("SqlServer:Clustered", false);
            }

            migrationBuilder.CreateIndex(name: "IX_AuthSessions_Key", table: "AuthSessions", column: "Key", unique: true);
            CreateRelations(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("The change to GUID primary keys can't be reverted: restore a backup instead.");
        }

        private static void DropRelations(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Agents_Repositories_RepositoryId", table: "Agents");
            migrationBuilder.DropForeignKey(name: "FK_WorkItems_Repositories_RepositoryId", table: "WorkItems");
            migrationBuilder.DropForeignKey(name: "FK_WorkItems_WorkItems_ParentId", table: "WorkItems");
            migrationBuilder.DropForeignKey(name: "FK_ChatConversations_Agents_AgentId", table: "ChatConversations");
            migrationBuilder.DropForeignKey(name: "FK_ChatConversations_AgentTemplates_AgentTemplateId", table: "ChatConversations");
            migrationBuilder.DropForeignKey(name: "FK_ChatConversations_Repositories_RepositoryId", table: "ChatConversations");
            migrationBuilder.DropForeignKey(name: "FK_ChatConversations_WorkItems_WorkItemId", table: "ChatConversations");
            migrationBuilder.DropForeignKey(name: "FK_ChatConversations_WorkItemTemplates_WorkItemTemplateId", table: "ChatConversations");
            migrationBuilder.DropForeignKey(name: "FK_ChatMessages_ChatConversations_ConversationId", table: "ChatMessages");

            migrationBuilder.DropCheckConstraint(name: "CK_ChatConversations_ExactlyOneOwner", table: "ChatConversations");

            migrationBuilder.DropIndex(name: "IX_Agents_RepositoryId_FileName", table: "Agents");
            migrationBuilder.DropIndex(name: "IX_WorkItems_ParentId", table: "WorkItems");
            migrationBuilder.DropIndex(name: "IX_WorkItems_RepositoryId_Type_Status", table: "WorkItems");
            migrationBuilder.DropIndex(name: "IX_ChatConversations_AgentId", table: "ChatConversations");
            migrationBuilder.DropIndex(name: "IX_ChatConversations_AgentTemplateId", table: "ChatConversations");
            migrationBuilder.DropIndex(name: "IX_ChatConversations_RepositoryId", table: "ChatConversations");
            migrationBuilder.DropIndex(name: "IX_ChatConversations_WorkItemId", table: "ChatConversations");
            migrationBuilder.DropIndex(name: "IX_ChatConversations_WorkItemTemplateId", table: "ChatConversations");
            migrationBuilder.DropIndex(name: "IX_ChatMessages_ConversationId_Sequence", table: "ChatMessages");
        }

        private static void CreateRelations(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(name: "IX_Agents_RepositoryId_FileName", table: "Agents", columns: ["RepositoryId", "FileName"], unique: true);
            migrationBuilder.CreateIndex(name: "IX_WorkItems_ParentId", table: "WorkItems", column: "ParentId");
            migrationBuilder.CreateIndex(name: "IX_WorkItems_RepositoryId_Type_Status", table: "WorkItems", columns: ["RepositoryId", "Type", "Status"]);
            CreateOpenConversationIndex(migrationBuilder, "AgentId");
            CreateOpenConversationIndex(migrationBuilder, "AgentTemplateId");
            CreateOpenConversationIndex(migrationBuilder, "RepositoryId");
            CreateOpenConversationIndex(migrationBuilder, "WorkItemId");
            CreateOpenConversationIndex(migrationBuilder, "WorkItemTemplateId");
            migrationBuilder.CreateIndex(name: "IX_ChatMessages_ConversationId_Sequence", table: "ChatMessages", columns: ["ConversationId", "Sequence"], unique: true);

            migrationBuilder.AddCheckConstraint(name: "CK_ChatConversations_ExactlyOneOwner", table: "ChatConversations", sql: ExactlyOneOwner);

            AddForeignKey(migrationBuilder, "Agents", "RepositoryId", "Repositories", ReferentialAction.Restrict);
            AddForeignKey(migrationBuilder, "WorkItems", "RepositoryId", "Repositories", ReferentialAction.Restrict);
            AddForeignKey(migrationBuilder, "WorkItems", "ParentId", "WorkItems", ReferentialAction.Restrict);
            AddForeignKey(migrationBuilder, "ChatConversations", "AgentId", "Agents", ReferentialAction.Cascade);
            AddForeignKey(migrationBuilder, "ChatConversations", "AgentTemplateId", "AgentTemplates", ReferentialAction.Cascade);
            AddForeignKey(migrationBuilder, "ChatConversations", "RepositoryId", "Repositories", ReferentialAction.Restrict);
            AddForeignKey(migrationBuilder, "ChatConversations", "WorkItemId", "WorkItems", ReferentialAction.Cascade);
            AddForeignKey(migrationBuilder, "ChatConversations", "WorkItemTemplateId", "WorkItemTemplates", ReferentialAction.Cascade);
            AddForeignKey(migrationBuilder, "ChatMessages", "ConversationId", "ChatConversations", ReferentialAction.Cascade);
        }

        private static void AddSysId(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<int>(name: "SysId", table: table, type: "int", nullable: false)
                .Annotation("SqlServer:Identity", "1, 1");
        }

        /// <summary>The default fills the existing rows; the application generates the id of new rows.</summary>
        private static void AddGuidId(MigrationBuilder migrationBuilder, string table)
        {
            migrationBuilder.AddColumn<Guid>(name: "Id", table: table, type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()");
            migrationBuilder.AlterColumn<Guid>(name: "Id", table: table, type: "uniqueidentifier", nullable: false,
                oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldDefaultValueSql: "NEWID()");
        }

        private static void ConvertForeignKey(MigrationBuilder migrationBuilder, string table, string column, string principal, bool nullable)
        {
            var oldColumn = $"{column}Old";
            migrationBuilder.RenameColumn(name: column, table: table, newName: oldColumn);
            migrationBuilder.AddColumn<Guid>(name: column, table: table, type: "uniqueidentifier", nullable: true);
            migrationBuilder.Sql($"UPDATE [dependent] SET [{column}] = [principal].[Id] FROM [{table}] AS [dependent] INNER JOIN [{principal}] AS [principal] ON [dependent].[{oldColumn}] = [principal].[SysId];");
            migrationBuilder.DropColumn(name: oldColumn, table: table);
            if (!nullable)
            {
                migrationBuilder.AlterColumn<Guid>(name: column, table: table, type: "uniqueidentifier", nullable: false,
                    oldClrType: typeof(Guid), oldType: "uniqueidentifier", oldNullable: true);
            }
        }

        private static void CreateOpenConversationIndex(MigrationBuilder migrationBuilder, string column)
        {
            migrationBuilder.CreateIndex(name: $"IX_ChatConversations_{column}", table: "ChatConversations", column: column, unique: true,
                filter: $"[{column}] IS NOT NULL AND [ClosedAt] IS NULL");
        }

        private static void AddForeignKey(MigrationBuilder migrationBuilder, string table, string column, string principal, ReferentialAction onDelete)
        {
            migrationBuilder.AddForeignKey(name: $"FK_{table}_{principal}_{column}", table: table, column: column,
                principalTable: principal, principalColumn: "Id", onDelete: onDelete);
        }
    }
}
