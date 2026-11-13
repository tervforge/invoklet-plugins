# Invoklet themes

VS Code, Rider and VS 2022 each have a light and a dark palette.
Install a package in **Preferences → Plugins → Install archive…**, restart Invoklet,
and choose a palette in **Preferences → Appearance**.

Build all three packages with the .NET 10 SDK.

```sh
dotnet pack ThemePackages.slnx -c Release -o artifacts
```
