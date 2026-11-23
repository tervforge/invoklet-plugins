using Invoklet.Contracts.Connections;
using Invoklet.Plugins;

namespace Invoklet.Connectors.SqliteSql;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.sqlite-sql", "1.0.0");

    public void Register(ConnectionPluginContext context) => context.RegisterDriver(new SqliteSqlConnectionDriver());
}
