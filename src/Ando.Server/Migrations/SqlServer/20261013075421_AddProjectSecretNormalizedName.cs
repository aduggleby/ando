// =============================================================================
// 20261013075421_AddProjectSecretNormalizedName.cs
//
// Summary: SQL Server migration that adds ProjectSecrets.NormalizedName and
// moves the per-project uniqueness of secret names onto it.
//
// Secret names were unique per project on (ProjectId, Name), which is
// case-insensitive only because of SQL Server's default collation. The model
// now enforces uniqueness on (ProjectId, NormalizedName), where NormalizedName
// is the uppercase invariant form of Name, so the rule is identical on SQL
// Server and PostgreSQL.
//
// Design Decisions:
// - NormalizedName uses the binary collation Latin1_General_100_BIN2. The
//   application already normalizes with ToUpperInvariant(), so the unique
//   index and lookups must compare code points exactly (like C# ordinal
//   comparison) instead of applying the database's own case rules again.
// - Staged Up() instead of the scaffolded single step (which would add a
//   NOT NULL column with a '' default, leaving a permanent DEFAULT constraint
//   and making every existing row collide in the new unique index):
//     1. add NormalizedName as nullable (binary collation)
//     2. backfill with UPPER(Name COLLATE Latin1_General_100_CI_AS). UPPER is
//        collation-sensitive: under a Turkish database collation, UPPER('i')
//        is the dotted 'İ' while ToUpperInvariant('i') is 'I'. Forcing a fixed
//        Latin1 collation makes the backfill independent of the database
//        collation and equal to ToUpperInvariant() for the ASCII names the
//        API accepts ([A-Za-z_][A-Za-z0-9_]*)
//     3. audit for names that collapse to the same value within one project
//        (exact, because the column is binary-collated) and THROW with an
//        operator-facing message; THROW aborts the migration transaction, so
//        the added column is rolled back too and the old index stays in place
//     4. make the column NOT NULL
//     5. drop the old (ProjectId, Name) unique index and create the unique
//        (ProjectId, NormalizedName) index
// - Hand-written T-SQL is acceptable here because this migration set is SQL
//   Server only; the PostgreSQL set is generated separately.
// - Rollback caveat: an Ando.Server binary older than this migration does not
//   set NormalizedName and cannot insert secrets once the column is required.
//   Down() exists for development use; operators rolling back a deployment must
//   restore the database backup taken before the upgrade.
// =============================================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ando.Server.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class AddProjectSecretNormalizedName : Migration
    {
        private const string BinaryCollation = "Latin1_General_100_BIN2";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stage 1: nullable column so existing rows are valid before backfill.
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "ProjectSecrets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                collation: BinaryCollation);

            // Stage 2: backfill from the display name with a fixed, non-Turkish
            // collation so UPPER matches ToUpperInvariant() for ASCII names.
            migrationBuilder.Sql(
                "UPDATE [ProjectSecrets] SET [NormalizedName] = UPPER([Name] COLLATE Latin1_General_100_CI_AS);");

            // Stage 3: fail with a clear message if two secrets in one project
            // normalize to the same name, instead of a generic duplicate-key error.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT [ProjectId], [NormalizedName]
                    FROM [ProjectSecrets]
                    GROUP BY [ProjectId], [NormalizedName]
                    HAVING COUNT(*) > 1)
                THROW 50001, N'Ando: duplicate secret names differing only by case exist in one or more projects. Resolve the duplicates, then rerun the upgrade.', 1;
                """);

            // Stage 4: required from now on.
            migrationBuilder.AlterColumn<string>(
                name: "NormalizedName",
                table: "ProjectSecrets",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                collation: BinaryCollation,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true,
                oldCollation: BinaryCollation);

            // Stage 5: move uniqueness from Name to NormalizedName.
            migrationBuilder.DropIndex(
                name: "IX_ProjectSecrets_ProjectId_Name",
                table: "ProjectSecrets");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSecrets_ProjectId_NormalizedName",
                table: "ProjectSecrets",
                columns: new[] { "ProjectId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectSecrets_ProjectId_NormalizedName",
                table: "ProjectSecrets");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSecrets_ProjectId_Name",
                table: "ProjectSecrets",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "ProjectSecrets");
        }
    }
}
