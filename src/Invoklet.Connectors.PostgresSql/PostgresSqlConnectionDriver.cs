using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Npgsql;

namespace Invoklet.Connectors.PostgresSql;

public sealed class PostgresSqlConnectionDriver : IConnectionDriver, ICSharpClientSource
{
    private const string ConnectionStringKey = "connectionString";
    private const string ReadOnlyKey = "readOnly";
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres";

    public string Id => "postgres-sql";
    public string DisplayName => "PostgreSQL (SQL)";
    public ConnectionCategory Category => ConnectionCategory.Database;

    public IReadOnlyList<ConnectionParam> Params { get; } =
    [
        new(ConnectionStringKey, "Connection string", ConnectionParamType.ConnectionString, Required: true,
            Default: DefaultConnectionString, Parts:
            [
                new("Host", "Host", ["Server"], "localhost"),
                new("Port", "Port", Example: "5432"),
                new("Database", "Database", ["DB"], "postgres"),
                new("Username", "Username", ["User ID", "UserID", "User Name", "UID", "User"], "postgres"),
                new("Password", "Password", ["PWD", "PSW"], "${secret:name}", IsSecret: true),
            ]),
        new(ReadOnlyKey, "Read only", ConnectionParamType.Boolean, Default: "true"),
    ];

    public IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>
    {
        ["Dapper"] = "2.1.79",
        ["Npgsql"] = "10.0.3",
    };

    public IReadOnlyList<string> Validate(MdRunConnection connection)
    {
        var problems = new List<string>();
        if (!connection.Params.TryGetValue(ConnectionStringKey, out var value) || string.IsNullOrWhiteSpace(value.Text))
            problems.Add("Enter a PostgreSQL connection string.");
        else if (MdRunPlaceholders.Parse(value.Text).Parts.All(part => part.Kind == MdRunPlaceholders.Kind.Text))
        {
            try { _ = new NpgsqlConnectionStringBuilder(value.Text); }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                problems.Add("The PostgreSQL connection string is invalid.");
            }
        }
        if (connection.Params.TryGetValue(ReadOnlyKey, out var mode) && !bool.TryParse(mode.Text, out _))
            problems.Add("Read only must be true or false.");
        return problems;
    }

    public async Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken)
    {
        if (!resolvedParams.TryGetValue(ConnectionStringKey, out var value) || string.IsNullOrWhiteSpace(value.Text))
            return new(false, "Enter a PostgreSQL connection string.");
        try
        {
            await using var database = new NpgsqlConnection(value.Text);
            await database.OpenAsync(cancellationToken);
            return new(true, "Connected to PostgreSQL.");
        }
        catch (Exception ex) when (ex is NpgsqlException or ArgumentException or FormatException or TimeoutException or InvalidOperationException)
        {
            return new(false, "Could not connect to PostgreSQL. Check the server, database and credentials.");
        }
    }

    public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context)
    {
        var readOnly = !connection.Params.TryGetValue(ReadOnlyKey, out var mode) || !bool.TryParse(mode.Text, out var value) || value;
        var connectionString = context.RuntimeText(connection.Params[ConnectionStringKey].Text!.Trim());
        var setup = readOnly
            ? """
                await database.OpenAsync(cancellationToken);
                await using var transaction = await database.BeginTransactionAsync(cancellationToken);
                await using (var readOnlyCommand = new global::Npgsql.NpgsqlCommand("SET TRANSACTION READ ONLY", database, transaction))
                    await readOnlyCommand.ExecuteNonQueryAsync(cancellationToken);
                """
            : "global::Npgsql.NpgsqlTransaction? transaction = null;";
        return new($$"""
            public sealed class {{context.ClassName}}
            {
                internal {{context.ClassName}}() { }

                public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<T>>
                    QueryAsync<T>(string sql, global::System.Func<global::System.Data.Common.DbDataReader, T> map,
                        object? parameters = null, global::System.Threading.CancellationToken cancellationToken = default)
                {
                    global::System.ArgumentNullException.ThrowIfNull(map);
                    await using var database = new global::Npgsql.NpgsqlConnection({{connectionString}});
                    {{setup}}
                    var command = new global::Dapper.CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken);
                    await using var reader = await global::Dapper.SqlMapper.ExecuteReaderAsync(database, command);
                    var rows = new global::System.Collections.Generic.List<T>();
                    while (await reader.ReadAsync(cancellationToken))
                        rows.Add(map(reader));
                    return rows;
                }
            }
            """);
    }
}
