# Invoklet plugins

The connection connectors of [Invoklet](https://github.com/tervforge/invoklet), each a NuGet package:

| Package | Plugin role | Drivers |
| --- | --- | --- |
| `invoklet.connector.http` | connection | `http`, `openapi` |
| `invoklet.connector.grpc` | connection | `grpc` |
| `invoklet.connector.efcore` | library | none: the EF Core reader the database connectors share |
| `invoklet.connector.sqlite` | connection (depends on EF Core) | `sqlite` |
| `invoklet.connector.postgres` | connection (depends on EF Core) | `postgres` |
| `invoklet.connector.sqlserver` | connection (depends on EF Core) | `sqlserver` |

Each package holds the published plugin under `plugin/`: `invoklet-plugin.json`, the assemblies with their `.deps.json`, and the
native libraries for the runtimes Invoklet ships for. The package id and version are read from the manifest
(`PluginPackage.props`), and a plugin the manifest lists in `dependsOn` is a project reference, not a NuGet dependency: Invoklet
installs and orders plugins by their manifests. Invoklet bundles these packages in its releases, and they install through
**Preferences → Plugins** as any other plugin. See the
[plugin authoring guide](https://tervforge.github.io/invoklet-docs/plugin-authoring/) for the interfaces and the package layout.

## Build

The connectors compile against the Invoklet contract packages (`Invoklet.Plugins`, `Invoklet.Contracts`, `Invoklet.Format.MdRun`).
The `invoklet` source of `nuget.config` is the folder `artifacts/contracts`; pack them there from an Invoklet checkout:

```sh
for p in Invoklet.Plugins Invoklet.Contracts Invoklet.Format.MdRun; do
  dotnet pack ../invoklet/src/core/$p -c Release -o artifacts/contracts
done
dotnet pack PluginPackages.slnx -c Release -o artifacts/packages
```

To build against the published contracts instead, point the `invoklet` source at GitHub Packages:

```sh
dotnet nuget update source invoklet --source https://nuget.pkg.github.com/tervforge/index.json \
  --username <user> --password <token with read:packages> --store-password-in-clear-text --configfile nuget.config
```

The `Packages` workflow packs the connectors and runs their unit tests on every pull request and push, against Invoklet's `dev`
branch. A push to `main` then publishes the packages to GitHub Packages. The six connectors are released together under one version. A
published version is never replaced: when a connector changes, raise the version in all six `invoklet-plugin.json` files (and
`InvokletConnectorsVersion` in Invoklet's `build/ConnectorPlugins.targets`); the publish job fails if a changed connector's version
is already taken. Invoklet publishes the contract packages
itself and checks that the connector packages are published before a release build. Give the `invoklet` repository read access
to the connector packages in their GitHub settings.

NuGet keeps a package version once it has restored it: after repacking the same version, delete its folder under
`~/.nuget/packages` or raise the version.

## Tests

The tests compile and run scripts through Invoklet itself, so they need an Invoklet checkout: `../invoklet` by default, or another
one given as `InvokletRoot`.

```sh
dotnet test tests/Invoklet.Connectors.Tests -p:InvokletRoot=/path/to/invoklet/
```

`tests/Invoklet.Connectors.Tests` are unit tests; none reaches a server.

### Integration tests

`tests/Invoklet.Connectors.IntegrationTests` covers real SQLite files, EF Core reverse engineering and
compilation/execution of scripts using database providers, and the packed plugins themselves: it packs the six packages, installs
them into an empty user folder and loads them as Invoklet does after a restart. Run these local scenarios with the .NET 10 SDK:
`dotnet test tests/Invoklet.Connectors.IntegrationTests`. PostgreSQL and SQL Server tests additionally need
the servers below; only those server tests skip themselves unless their environment variable holds a
connection string. The project is built and formatted with the solution but, like
the application's integration tests, not run when the `CI` environment variable is set: CI has no database servers.

To run them locally you need Docker (or Podman) and the .NET 10 SDK.

1. Start the servers. The passwords are throwaway values for a local container; the SQL Server image is
   about 1.5 GB and does not run natively on some ARM hosts (on Apple silicon enable Rosetta emulation in
   Docker Desktop, or use `--platform linux/amd64`).

   ```bash
   docker run -d --name invoklet-pg -p 5432:5432 \
     -e POSTGRES_PASSWORD=invoklet -e POSTGRES_DB=shop postgres:16

   docker run -d --name invoklet-mssql -p 1433:1433 \
     -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Invoklet_Passw0rd!' \
     mcr.microsoft.com/mssql/server:2022-latest
   ```

2. Wait until they accept connections (SQL Server takes up to a minute):

   ```bash
   docker exec invoklet-pg pg_isready -U postgres
   docker exec invoklet-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P 'Invoklet_Passw0rd!' -Q 'SELECT 1'
   ```

3. Point the tests at them and run (PowerShell: `$env:INVOKLET_TEST_POSTGRES = '...'`):

   ```bash
   export INVOKLET_TEST_POSTGRES='Host=localhost;Port=5432;Database=shop;Username=postgres;Password=invoklet'
   export INVOKLET_TEST_SQLSERVER='Server=localhost,1433;Database=master;User Id=sa;Password=Invoklet_Passw0rd!;TrustServerCertificate=true'
   dotnet test tests/Invoklet.Connectors.IntegrationTests
   ```

   Set only one variable to run only that server's tests. The tests create and drop their own schema
   `invoklet_it` in the given database and touch nothing else, but use a disposable database anyway.

4. Stop and remove the containers when done: `docker rm -f invoklet-pg invoklet-mssql`.

If a test fails with a connection error, check that the port is not taken by a local server
(`lsof -i :5432`, `lsof -i :1433`) and that the container is healthy (`docker ps`). Running the tests
against another server works the same way: put its connection string in the variable.

