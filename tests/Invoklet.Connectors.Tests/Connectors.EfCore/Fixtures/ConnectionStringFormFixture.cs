using Invoklet.Connectors.Postgres;
using Invoklet.Connectors.SqlServer;
using Invoklet.Contracts.Connections;
using Invoklet.Windows.Models;

namespace Invoklet.Connectors.Tests.Connectors.EfCore.Fixtures;

// The connection string parameters of the PostgreSQL and SQL Server drivers, edited as Invoklet's connection form edits them.
internal sealed class ConnectionStringFormFixture
{
    public ConnectionParam Postgres { get; } = new PostgresConnectionDriver().Params.Single(p => p.Type == ConnectionParamType.ConnectionString);

    public ConnectionParam SqlServer { get; } = new SqlServerConnectionDriver().Params.Single(p => p.Type == ConnectionParamType.ConnectionString);

    public ConnectionFieldViewModel CreateSut(ConnectionParam param, string? text = null) =>
        new(param, text is null ? null : Invoklet.Format.MdRun.MdRunValue.Of(text), ["shop", "other"]);

    public static bool Shown(ConnectionFieldViewModel sut, string key) => sut.Parts.Single(p => p.Part.Key == key).IsVisible;
}
