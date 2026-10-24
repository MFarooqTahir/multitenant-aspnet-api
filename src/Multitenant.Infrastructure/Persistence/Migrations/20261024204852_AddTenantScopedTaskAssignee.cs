using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Multitenant.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantScopedTaskAssignee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_AspNetUsers_AssigneeUserId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_AssigneeUserId",
                table: "WorkItems");

            migrationBuilder.AddColumn<Guid>(
                name: "AssigneeTenantId",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AspNetUsers_TenantId_Id",
                table: "AspNetUsers",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_AssigneeTenantId_AssigneeUserId",
                table: "WorkItems",
                columns: new[] { "AssigneeTenantId", "AssigneeUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkItems_AssigneeTenant",
                table: "WorkItems",
                sql: "\"AssigneeTenantId\" IS NULL OR \"AssigneeTenantId\" = \"TenantId\"");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_AspNetUsers_AssigneeTenantId_AssigneeUserId",
                table: "WorkItems",
                columns: new[] { "AssigneeTenantId", "AssigneeUserId" },
                principalTable: "AspNetUsers",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_AspNetUsers_AssigneeTenantId_AssigneeUserId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_AssigneeTenantId_AssigneeUserId",
                table: "WorkItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkItems_AssigneeTenant",
                table: "WorkItems");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AspNetUsers_TenantId_Id",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AssigneeTenantId",
                table: "WorkItems");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_AssigneeUserId",
                table: "WorkItems",
                column: "AssigneeUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_AspNetUsers_AssigneeUserId",
                table: "WorkItems",
                column: "AssigneeUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
