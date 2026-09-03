using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailCore.Infrastructure.Postgres.Migrations;

/// <summary>
/// Applies row level security (TEN-02) and the immutability triggers (CMP-04).
/// </summary>
/// <remarks>
/// The SQL lives in an embedded file rather than inline strings, because it is the part of
/// the schema most likely to be read by somebody who is not reading the C#, and it deserves
/// to be readable as SQL. PLAN.md section 5 makes migrations forward only: correcting any of
/// this means a new migration, never an edit to this one.
/// </remarks>
public partial class RowLevelSecurityAndImmutability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ReadEmbeddedSql("RowLevelSecurity.sql"));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately not reversible in a useful sense. Dropping tenant isolation and the
        // immutability triggers on a live multi-tenant database is not an operation that
        // should be one command away; restore from a backup instead.
        throw new NotSupportedException(
            "Row level security and the immutability triggers are not rolled back automatically. " +
            "Restore from a backup, or drop the policies deliberately by hand.");
    }

    private static string ReadEmbeddedSql(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith(name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded SQL resource '{name}' was not found.");

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
