# Sitecore Content Transfer GUI

A .NET/C# Windows executable for SitecoreAI

It is built for developers, technical content teams, and administrators who need a fast way to transfer content without hassle, and to set up and explore the Sitecore AI Content Transfer API and Item Transfer API.

This tool uses the Content Transfer API without environment limitations, supports local Docker containers, and enables transfers between different organizations.

See [Using the Sitecore Content Transfer API Locally with Docker](https://stockpick.nl/sitecoreai/sitecore-content-transfer-api-locally-with-docker/) for how to fix local SitecoreAI for Content Transfer API

## Security note

This is a development tool. Please note that API keys/client secrets are currently stored in plain text in the local app configuration.

For safer usage, it is recommended to use `.sitecore/user.json` from the Sitecore CLI. This uses access tokens that are typically valid for only a few hours.

See also: https://github.com/jbluemink/SitecoreCommander

## Local config and presets location

The app stores the last used configuration and presets in `transferconfig.json`.

### Unpackaged run (Project / EXE)

`%AppData%\SitecoreContentTransfer\transferconfig.json`

Typical path:

`C:\Users\<YourUser>\AppData\Roaming\SitecoreContentTransfer\transferconfig.json`

### Packaged run (MSIX)

`%LocalAppData%\Packages\<PackageId>\LocalCache\Roaming\SitecoreContentTransfer\transferconfig.json`

Example:

`C:\Users\<YourUser>\AppData\Local\Packages\7e3af4e6-d88d-4da3-989b-726a40d59f30_kctd1eyqpbn18\LocalCache\Roaming\SitecoreContentTransfer\transferconfig.json`
