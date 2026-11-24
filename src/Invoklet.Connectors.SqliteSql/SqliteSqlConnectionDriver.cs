using Invoklet.Contracts.Connections;
using Invoklet.Format.MdRun;
using Microsoft.Data.Sqlite;

namespace Invoklet.Connectors.SqliteSql;

public sealed class SqliteSqlConnectionDriver : IConnectionDriver, ICSharpClientSource
{
    private const string FileKey = "file";
    private const string ReadOnlyKey = "readOnly";

    public string Id => "sqlite-sql";
    public string DisplayName => "SQLite (SQL)";
    public ConnectionCategory Category => ConnectionCategory.Database;

    public IReadOnlyList<ConnectionParam> Params { get; } =
    [
        new(FileKey, "Database file", ConnectionParamType.File, Required: true, Example: "data.db",
            Description: "Use an existing SQLite file. In a saved script, relative paths start at its folder."),
        new(ReadOnlyKey, "Read only", ConnectionParamType.Boolean, Default: "true"),
    ];

    public IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>
    {
        ["Dapper"] = "2.1.79",
        ["Microsoft.Data.Sqlite"] = "10.0.13",
    };

    public IReadOnlyList<string> Validate(MdRunConnection connection)
    {
        var problems = new List<string>();
        if (!connection.Params.TryGetValue(FileKey, out var file) || string.IsNullOrWhiteSpace(file.Text) || file.Text.Trim() == ":memory:")
            problems.Add("Choose an existing SQLite database file.");
        if (connection.Params.TryGetValue(ReadOnlyKey, out var mode) && !bool.TryParse(mode.Text, out _))
            problems.Add("Read only must be true or false.");
        return problems;
    }

    public async Task<ConnectionTestResult> TestAsync(IReadOnlyDictionary<string, MdRunValue> resolvedParams, CancellationToken cancellationToken)
    {
        if (!resolvedParams.TryGetValue(FileKey, out var file) || string.IsNullOrWhiteSpace(file.Text))
            return new(false, "Choose an existing SQLite database file.");
        var path = file.Text.Trim();
        if (!Path.IsPathFullyQualified(path))
            return new(false, "Use an absolute database path to test in Connections. A saved script can use a relative path.");

        try
        {
            await using var database = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            await database.OpenAsync(cancellationToken);
            await using var command = database.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_schema";
            await command.ExecuteScalarAsync(cancellationToken);
            return new(true, "The SQLite database is readable.");
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, "The SQLite database could not be read. Check the file and its permissions.");
        }
    }

    public ConnectionClientCode CreateClient(MdRunConnection connection, ConnectionCodeContext context)
    {
        var readOnly = !connection.Params.TryGetValue(ReadOnlyKey, out var mode) || !bool.TryParse(mode.Text, out var value) || value;
        var file = context.RuntimeText(connection.Params[FileKey].Text!.Trim());
        return new($$"""
            public sealed class {{context.ClassName}}
            {
                internal {{context.ClassName}}() { }

                public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<T>>
                    QueryAsync<T>(string sql, global::System.Func<global::System.Data.Common.DbDataReader, T> map,
                        object? parameters = null, global::System.Threading.CancellationToken cancellationToken = default)
                {
                    global::System.ArgumentNullException.ThrowIfNull(map);
                    var file = {{file}};
                    if (global::System.String.IsNullOrWhiteSpace(file) || file == ":memory:")
                        throw new global::System.InvalidOperationException("Choose an existing SQLite database file.");
                    if (!global::System.IO.Path.IsPathFullyQualified(file))
                        file = global::System.IO.Path.GetFullPath(file,
                            global::System.IO.Path.GetDirectoryName(global::Util.CurrentQueryPath)
                            ?? throw new global::System.InvalidOperationException("Save the script before using a relative database path."));

                    await using var database = new global::Microsoft.Data.Sqlite.SqliteConnection(
                        new global::Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                        {
                            DataSource = file,
                            Mode = global::Microsoft.Data.Sqlite.SqliteOpenMode.{{(readOnly ? "ReadOnly" : "ReadWrite")}},
                            Pooling = false,
                        }.ToString());
                    var command = new global::Dapper.CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
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
