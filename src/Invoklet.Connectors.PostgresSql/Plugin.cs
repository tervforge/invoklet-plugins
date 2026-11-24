using Invoklet.Contracts.Connections;
using Invoklet.Plugins;

namespace Invoklet.Connectors.PostgresSql;

public sealed class Plugin : IConnectionPlugin
{
    public PluginDescriptor Descriptor { get; } = new("invoklet.connector.postgres-sql", "1.0.0");

    public void Register(ConnectionPluginContext context) => context.RegisterDriver(new PostgresSqlConnectionDriver());
}
