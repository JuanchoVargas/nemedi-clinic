using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NemediClinic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRipsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodigoHabilitacionReps",
                table: "Tenants",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoCups",
                table: "Procedures",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalidadConsulta",
                table: "Procedures",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiereRips",
                table: "Procedures",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TipoServicio",
                table: "Procedures",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoEntidad",
                table: "Patients",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MunicipioResidencia",
                table: "Patients",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NombreEntidad",
                table: "Patients",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaisResidencia",
                table: "Patients",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sexo",
                table: "Patients",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoDocumento",
                table: "Patients",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoUsuario",
                table: "Patients",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZonaResidencia",
                table: "Patients",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodigoHabilitacionReps",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CodigoCups",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "FinalidadConsulta",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "RequiereRips",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "TipoServicio",
                table: "Procedures");

            migrationBuilder.DropColumn(
                name: "CodigoEntidad",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MunicipioResidencia",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "NombreEntidad",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "PaisResidencia",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "Sexo",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "TipoDocumento",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "TipoUsuario",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ZonaResidencia",
                table: "Patients");
        }
    }
}
