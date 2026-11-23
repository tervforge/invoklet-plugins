using System.Collections.Concurrent;
using Invoklet.Connectors.PostgresSql;
using Invoklet.Contracts.Connections;
using Invoklet.Contracts.Models;
using Invoklet.Format.MdRun;
using Invoklet.Languages.CSharp;
using Invoklet.Languages.CSharp.IntelliSense;
using Invoklet.Languages.DotnetCommon;
using Invoklet.Languages.DotnetCommon.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Invoklet.Connectors.IntegrationTests.Connectors.Postgres;

[Trait("Category", "Integration")]
public sealed class PostgresSqlScriptTests
{
    private const string Script = """
        var rows = await Connections.Shop.QueryAsync(
            "SELECT @id::integer, NULL::text, current_setting('transaction_read_only')",
            row => new { Id = row.GetInt32(0), City = row.IsDBNull(1) ? null : row.GetString(1), Mode = row.GetString(2) },
            new { id = 7 });
        var person = rows.Single();
        Console.WriteLine($"{person.Id}:{person.City ?? "null"}:{person.Mode}");
        try
        {
            await Connections.Shop.QueryAsync("CREATE TEMP TABLE invoklet_read_only_check (id integer)", row => row.GetInt32(0));
        }
        catch (Npgsql.PostgresException error) when (error.SqlState == "25006")
        {
            Console.WriteLine("Read-only");
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task QueryAsync_GeneratedClient_CompilesWithoutEmbeddingCredentials(bool readOnly) =>
        CheckScriptAsync("Host=localhost;Database=postgres;Username=postgres;Password=compile-only-marker", readOnly, execute: false);

    [Fact]
    public Task QueryAsync_ReadOnlyServerConnection_ReturnsTypedRowsAndRejectsWrite()
    {
        var connectionString = Environment.GetEnvironmentVariable("INVOKLET_TEST_POSTGRES");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString), "INVOKLET_TEST_POSTGRES names no server.");
        return CheckScriptAsync(connectionString!, readOnly: true, execute: true);
    }

    private static async Task CheckScriptAsync(string connectionString, bool readOnly, bool execute)
    {
        var folder = Directory.CreateTempSubdirectory("Invoklet-postgres-sql");
        var cancel = TestContext.Current.CancellationToken;
        try
        {
            var driver = new PostgresSqlConnectionDriver();
            var registry = new ConnectionDriverRegistry();
            registry.Register(driver);
            var connection = new MdRunConnection
            {
                Name = "shop",
                Driver = driver.Id,
                Params = new(StringComparer.Ordinal)
                {
                    ["connectionString"] = MdRunValue.Of(connectionString),
                    ["readOnly"] = MdRunValue.Of(readOnly ? "true" : "false"),
                },
            };
            var generated = GeneratedConnections.For([connection], MdRunLanguage.CSharp, driver.Packages.Keys, registry);
            Assert.NotNull(generated);
            Assert.Empty(generated.Errors);
            Assert.Equal(connectionString, Assert.Single(generated.StartupValues));
            Assert.DoesNotContain(connectionString, generated.Source);
            Assert.All(generated.Files, file => Assert.DoesNotContain(connectionString, file.Text));

            var options = CompiledDotnetLanguageHostOptions.From(new ExecutionRequest
            {
                Platform = new DotnetSdkProvider().Get().First(),
                OutputDirectory = folder.FullName,
                WorkingDirectory = folder.FullName,
                CurrentQueryPath = Path.Combine(folder.FullName, "shop.inv"),
                Packages = driver.Packages,
                Connections = generated,
            }, new CSharpReferenceService().GetExtensionReference());
            await using var host = new CSharpLanguageHost(options, NullLoggerFactory.Instance);
            if (!execute)
            {
                var compilation = await host.CompileAsync(Script, cancel).WaitAsync(TimeSpan.FromMinutes(2), cancel);
                Assert.True(compilation.Success, string.Join('\n', compilation.Diagnostics.Select(diagnostic => diagnostic.Message)));
                return;
            }

            var output = new ConcurrentQueue<string>();
            var errors = new ConcurrentQueue<string>();
            var run = await host.RunAsync(Script, (data, kind) =>
                    (kind == OutputKind.StandardError ? errors : output).Enqueue(data), cancel)
                .WaitAsync(TimeSpan.FromMinutes(2), cancel);
            Assert.True(run.Succeeded, string.Join('\n', errors.Concat(output)));
            Assert.Equal(["7:null:on", "Read-only"], output.TakeLast(2));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
