using System.Collections.Concurrent;
using Invoklet.Connectors.SqliteSql;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Sqlite;

[Trait("Category", "Integration")]
public sealed class SqliteSqlScriptTests
{
    [Fact]
    public async Task QueryAsync_SavedConnection_ReturnsTypedRowsAndHonorsReadOnly()
    {
        var folder = Directory.CreateTempSubdirectory("Invoklet-sqlite-sql");
        var cancel = TestContext.Current.CancellationToken;
        try
        {
            await using (var database = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(folder.FullName, "shop.db"),
                Pooling = false,
            }.ToString()))
            {
                await database.OpenAsync(cancel);
                await using var command = database.CreateCommand();
                command.CommandText = """
                    CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT NOT NULL, city TEXT);
                    INSERT INTO people VALUES (1, 'Ada', NULL), (2, 'Grace', 'NYC');
                    """;
                await command.ExecuteNonQueryAsync(cancel);
            }

            var driver = new SqliteSqlConnectionDriver();
            var registry = new ConnectionDriverRegistry();
            registry.Register(driver);
            var connection = new MdRunConnection
            {
                Name = "shop",
                Driver = driver.Id,
                Params = new(StringComparer.Ordinal) { ["file"] = MdRunValue.Of("shop.db") },
            };
            var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys,
                registry, scriptDirectory: folder.FullName);
            Assert.NotNull(generated);
            Assert.Empty(generated.Errors);

            var outputDirectory = folder.CreateSubdirectory("build").FullName;
            var options = CompiledDotnetLanguageHostOptions.From(new ExecutionRequest
            {
                Platform = new DotnetSdkProvider().Get().First(),
                OutputDirectory = outputDirectory,
                WorkingDirectory = outputDirectory,
                CurrentQueryPath = Path.Combine(folder.FullName, "shop.inv"),
                Packages = driver.Packages,
                Connections = generated,
            }, new CSharpReferenceService().GetExtensionReference());
            await using var host = new CSharpLanguageHost(options, NullLoggerFactory.Instance);
            var output = new ConcurrentQueue<string>();
            var errors = new ConcurrentQueue<string>();
            const string script = """
                var rows = await Connections.Shop.QueryAsync(
                    "SELECT id, name, city FROM people WHERE id >= @minId ORDER BY id",
                    row => new { Id = row.GetInt64(0), Name = row.GetString(1), City = row.IsDBNull(2) ? null : row.GetString(2) },
                    new { minId = 2L });
                Console.WriteLine($"{rows.Single().Id}:{rows.Single().Name}:{rows.Single().City}");
                var missing = await Connections.Shop.QueryAsync(
                    "SELECT id FROM people WHERE name = @name", row => row.GetInt64(0), new { name = "missing" });
                Console.WriteLine($"Missing: {missing.Count}");
                var first = await Connections.Shop.QueryAsync(
                    "SELECT city FROM people WHERE id = 1", row => row.IsDBNull(0) ? null : row.GetString(0));
                Console.WriteLine(first.Single() is null ? "Null kept" : "Null lost");
                try
                {
                    await Connections.Shop.QueryAsync("DELETE FROM people RETURNING id", row => row.GetInt64(0));
                }
                catch (Microsoft.Data.Sqlite.SqliteException error) when (error.SqliteErrorCode == 8)
                {
                    Console.WriteLine("Read-only");
                }
                """;
            var result = await host.RunAsync(script, (data, kind) =>
                    (kind == OutputKind.StandardError ? errors : output).Enqueue(data), cancel)
                .WaitAsync(TimeSpan.FromMinutes(2), cancel);
            Assert.True(result.Succeeded, string.Join('\n', errors.Concat(output)));
            Assert.Equal(["2:Grace:NYC", "Missing: 0", "Null kept", "Read-only"], output.TakeLast(4));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
