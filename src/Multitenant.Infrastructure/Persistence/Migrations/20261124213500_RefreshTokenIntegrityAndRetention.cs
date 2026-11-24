using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Multitenant.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RefreshTokenIntegrityAndRetention : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RefreshTokens_AspNetUsers_UserId",
            table: "RefreshTokens");

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_ExpiresAtUtc",
            table: "RefreshTokens",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_RevokedAtUtc",
            table: "RefreshTokens",
            column: "RevokedAtUtc",
            filter: "\"RevokedAtUtc\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_TenantId_UserId",
            table: "RefreshTokens",
            columns: new[] { "TenantId", "UserId" });

        migrationBuilder.AddForeignKey(
            name: "FK_RefreshTokens_AspNetUsers_TenantId_UserId",
            table: "RefreshTokens",
            columns: new[] { "TenantId", "UserId" },
            principalTable: "AspNetUsers",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Cascade);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_RefreshTokens_AspNetUsers_TenantId_UserId",
            table: "RefreshTokens");

        migrationBuilder.DropIndex(
            name: "IX_RefreshTokens_ExpiresAtUtc",
            table: "RefreshTokens");

        migrationBuilder.DropIndex(
            name: "IX_RefreshTokens_RevokedAtUtc",
            table: "RefreshTokens");

        migrationBuilder.DropIndex(
            name: "IX_RefreshTokens_TenantId_UserId",
            table: "RefreshTokens");

        migrationBuilder.AddForeignKey(
            name: "FK_RefreshTokens_AspNetUsers_UserId",
            table: "RefreshTokens",
            column: "UserId",
            principalTable: "AspNetUsers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }
}
