Create a release:
dotnet clean .\SitecoreContentTransfer.csproj -c Release
dotnet build .\SitecoreContentTransfer.csproj -c Release -p:Platform=x64 -p:DebugSymbols=false -p:DebugType=None

take: .\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\