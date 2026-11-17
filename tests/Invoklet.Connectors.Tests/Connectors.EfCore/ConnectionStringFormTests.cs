using Invoklet.Connectors.Postgres;
using Invoklet.Connectors.SqlServer;
using Invoklet.Connectors.Tests.Connectors.EfCore.Fixtures;
using Invoklet.Windows.Models;
using Xunit;

namespace Invoklet.Connectors.Tests.Connectors.EfCore;

// The connection form of the database drivers: a field per setting of the connection string and their ways of signing in.
public sealed class ConnectionStringFormTests
{
    [Fact]
    public void Constructor_APostgresStringWithAPassword_SignsInWithThePasswordAndHidesThePgpassFile()
    {
        // Arrange
        var fixture = new ConnectionStringFormFixture();

        // Act
        var sut = fixture.CreateSut(fixture.Postgres, "Host=db;Username=app;Password=${secret:shop}");

        // Assert
        Assert.Equal("Password", sut.SelectedAuthMode!.Label);
        Assert.True(ConnectionStringFormFixture.Shown(sut, "Password"));
        Assert.False(ConnectionStringFormFixture.Shown(sut, "Passfile"));
    }

    // The usual pgpass file of this system goes into its empty field, and the password goes.
    [Fact]
    public void SelectedAuthMode_PgpassChosen_WritesTheSystemsFileAndShowsItsField()
    {
        // Arrange
        var fixture = new ConnectionStringFormFixture();
        var sut = fixture.CreateSut(fixture.Postgres, "Host=db;Username=app;Password=${secret:shop}");

        // Act
        sut.SelectedAuthMode = sut.AuthModes.Single(m => m.Label == "pgpass file");

        // Assert
        Assert.Equal("Host=db;Username=app;Passfile=" + PostgresConnectionDriver.DefaultPassfile, sut.Text);
        Assert.False(ConnectionStringFormFixture.Shown(sut, "Password"));
        Assert.True(ConnectionStringFormFixture.Shown(sut, "Passfile"));
        Assert.True(sut.HasAuthHint);
    }

    [Fact]
    public void Text_IntegratedSecurityWritten_SignsInWithKerberos()
    {
        // Arrange
        var fixture = new ConnectionStringFormFixture();
        var sut = fixture.CreateSut(fixture.Postgres, "Host=db;Username=app;Password=${secret:shop}");

        // Act
        sut.Text = "Host=db;Integrated Security=true";

        // Assert
        Assert.Equal("Kerberos (GSSAPI)", sut.SelectedAuthMode!.Label);
        Assert.False(ConnectionStringFormFixture.Shown(sut, "Password"));
    }

    // A new connection starts with the server's own defaults written in, not with examples to copy.
    [Fact]
    public void Constructor_ANewPostgresConnection_StartsWithTheServersDefaults()
    {
        // Arrange
        var fixture = new ConnectionStringFormFixture();

        // Act
        var sut = fixture.CreateSut(fixture.Postgres);

        // Assert
        Assert.Equal("Host=localhost;Port=5432;Database=postgres;Username=postgres", sut.Text);
        Assert.Equal(["localhost", "5432", "postgres", "postgres", "", ""], sut.Parts.Select(p => p.Value));
        Assert.Equal("Password", sut.SelectedAuthMode!.Label);
    }

    [Fact]
    public void Constructor_ANewSqlServerConnection_StartsWithTheServersDefaults()
    {
        // Arrange
        var fixture = new ConnectionStringFormFixture();

        // Act
        var sut = fixture.CreateSut(fixture.SqlServer);

        // Assert
        Assert.Equal("Server=localhost;Database=master;User Id=sa;TrustServerCertificate=true", sut.Text);
    }
}
