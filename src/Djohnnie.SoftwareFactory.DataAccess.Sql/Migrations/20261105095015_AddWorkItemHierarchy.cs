using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Djohnnie.SoftwareFactory.DataAccess.Sql.Migrations
{
    /// <summary>
    /// Turns epics into work items (epics, features, user stories, bugs and tasks) with a parent, and epic templates
    /// into work item templates. Existing rows are kept and become epics (type 0); epics are no longer assigned
    /// to an agent, so their assignment settings are cleared.
    /// </summary>
    public partial class AddWorkItemHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Epics_Repositories_RepositoryId",
                table: "Epics");

            migrationBuilder.DropIndex(
                name: "IX_Epics_RepositoryId_Status",
                table: "Epics");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Epics",
                table: "Epics");

            migrationBuilder.RenameTable(
                name: "Epics",
                newName: "WorkItems");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "WorkItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "WorkItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "IssueId",
                table: "WorkItems",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("UPDATE [WorkItems] SET [CustomAgent] = NULL, [Model] = NULL, [BaseBranch] = NULL, [CustomInstructions] = NULL;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkItems",
                table: "WorkItems",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_ParentId",
                table: "WorkItems",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_RepositoryId_Type_Status",
                table: "WorkItems",
                columns: new[] { "RepositoryId", "Type", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Repositories_RepositoryId",
                table: "WorkItems",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_WorkItems_ParentId",
                table: "WorkItems",
                column: "ParentId",
                principalTable: "WorkItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropPrimaryKey(
                name: "PK_EpicTemplates",
                table: "EpicTemplates");

            migrationBuilder.RenameTable(
                name: "EpicTemplates",
                newName: "WorkItemTemplates");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "WorkItemTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkItemTemplates",
                table: "WorkItemTemplates",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemTemplates_Type",
                table: "WorkItemTemplates",
                column: "Type");
        }

        /// <inheritdoc />
        /// <remarks>Only epics and epic templates survive going back; other work items and templates are deleted.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_WorkItems_ParentId",
                table: "WorkItems");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Repositories_RepositoryId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_ParentId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_RepositoryId_Type_Status",
                table: "WorkItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkItems",
                table: "WorkItems");

            migrationBuilder.Sql("DELETE FROM [WorkItems] WHERE [Type] <> 0;");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "IssueId",
                table: "WorkItems");

            migrationBuilder.RenameTable(
                name: "WorkItems",
                newName: "Epics");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Epics",
                table: "Epics",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Epics_RepositoryId_Status",
                table: "Epics",
                columns: new[] { "RepositoryId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Epics_Repositories_RepositoryId",
                table: "Epics",
                column: "RepositoryId",
                principalTable: "Repositories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropIndex(
                name: "IX_WorkItemTemplates_Type",
                table: "WorkItemTemplates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkItemTemplates",
                table: "WorkItemTemplates");

            migrationBuilder.Sql("DELETE FROM [WorkItemTemplates] WHERE [Type] <> 0;");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WorkItemTemplates");

            migrationBuilder.RenameTable(
                name: "WorkItemTemplates",
                newName: "EpicTemplates");

            migrationBuilder.AddPrimaryKey(
                name: "PK_EpicTemplates",
                table: "EpicTemplates",
                column: "Id");
        }
    }
}
