using FluentAssertions;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Enums;

namespace GymForge.Application.Tests.Handlers;

public class MemberCsvParserTests
{
    [Fact]
    public void Parse_CommaSeparated_ReadsAllFields()
    {
        const string csv =
            "nombre,apellido,dni,email,telefono,nacimiento\n" +
            "Ana,García,30123456,ana@test.com,1144556677,20/05/1990\n";

        var result = MemberCsvParser.Parse(csv);

        result.Errors.Should().BeEmpty();
        result.Rows.Should().HaveCount(1);
        var row = result.Rows[0];
        row.FirstName.Should().Be("Ana");
        row.LastName.Should().Be("García");
        row.DocumentNumber.Should().Be("30123456");
        row.Email.Should().Be("ana@test.com");
        row.Mobile.Should().Be("1144556677");
        row.BirthDate.Should().Be(new DateOnly(1990, 5, 20));
    }

    [Fact]
    public void Parse_SemicolonDelimiter_AndAccentedHeaders_Works()
    {
        const string csv =
            "Apellido;Nombre;DNI;Teléfono\n" +
            "Pérez;Juan;28999111;1133224455\n";

        var result = MemberCsvParser.Parse(csv);

        result.Rows.Should().HaveCount(1);
        result.Rows[0].FirstName.Should().Be("Juan");
        result.Rows[0].LastName.Should().Be("Pérez");
        result.Rows[0].Mobile.Should().Be("1133224455");
    }

    [Fact]
    public void Parse_GenderColumn_MapsValues()
    {
        const string csv =
            "nombre,apellido,dni,genero\n" +
            "Ana,García,1,F\n" +
            "Luis,Pérez,2,masculino\n" +
            "Sam,Test,3,\n";

        var result = MemberCsvParser.Parse(csv);

        result.Rows[0].Gender.Should().Be(Gender.Female);
        result.Rows[1].Gender.Should().Be(Gender.Male);
        result.Rows[2].Gender.Should().Be(Gender.PreferNotToSay);
    }

    [Fact]
    public void Parse_MissingRequiredHeader_ReturnsErrorNoRows()
    {
        const string csv = "nombre,email\nAna,ana@test.com\n";

        var result = MemberCsvParser.Parse(csv);

        result.Rows.Should().BeEmpty();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("nombre, apellido y dni");
    }

    [Fact]
    public void Parse_RowMissingDni_IsReportedAndSkipped()
    {
        const string csv =
            "nombre,apellido,dni\n" +
            "Ana,García,30123456\n" +
            "Sin,Documento,\n";

        var result = MemberCsvParser.Parse(csv);

        result.Rows.Should().HaveCount(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("se omite");
    }

    [Fact]
    public void Parse_QuotedFieldWithDelimiter_StaysOneField()
    {
        const string csv =
            "apellido,nombre,dni\n" +
            "\"García, de la\",Ana,30123456\n";

        var result = MemberCsvParser.Parse(csv);

        result.Rows.Should().HaveCount(1);
        result.Rows[0].LastName.Should().Be("García, de la");
    }

    [Fact]
    public void Parse_Empty_ReturnsError()
    {
        MemberCsvParser.Parse("").Errors.Should().NotBeEmpty();
        MemberCsvParser.Parse(null).Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Parse_InvalidBirthDate_ImportsRowWithoutDate()
    {
        const string csv =
            "nombre,apellido,dni,nacimiento\n" +
            "Ana,García,30123456,no-es-fecha\n";

        var result = MemberCsvParser.Parse(csv);

        result.Rows.Should().HaveCount(1);
        result.Rows[0].BirthDate.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Should().Contain("fecha de nacimiento inválida");
    }
}
