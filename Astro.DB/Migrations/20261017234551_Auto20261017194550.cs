using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Astro.DB.Migrations
{
    /// <inheritdoc />
    public partial class Auto20261017194550 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GuildNickname",
                table: "Guilds",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuildNickname",
                table: "Guilds");
        }
    }
}
