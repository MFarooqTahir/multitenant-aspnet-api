using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Multitenant.Infrastructure.Persistence.Migrations;

/// <summary>
/// Records the model's use of PostgreSQL's existing <c>xmin</c> system column as
/// an optimistic-concurrency token for projects and tasks. No DDL is required:
/// every PostgreSQL table already has <c>xmin</c>.
/// </summary>
public partial class OptimisticConcurrencyTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
