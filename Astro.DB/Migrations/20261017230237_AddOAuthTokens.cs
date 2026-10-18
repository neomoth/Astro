using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astro.DB.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OAuthTokens",
                columns: table => new
                {
                    Id = table.Column<ulong>(type: "INTEGER", nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    RefreshToken = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthTokens", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OAuthTokens");
        }
    }
}
