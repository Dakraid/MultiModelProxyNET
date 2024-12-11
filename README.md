# Notice

This project is in early development, I have only verified its functionality for myself, so it is not guaranteed to work for your environment i.e. non-TabbyAPI main inference
endpoints.

# MultiModelProxyNET

This is an updated .NET 9 port of my previous MultiModelProxy, an OAI-compatible proxy server that facilitates fast Chain of Thought generation thought prompting by using a smaller
model to do the CoT inference before handing it to the (larger) main model.

# Usage

There are several ways to run MultiModelProxyNET:

## Using Pre-compiled Release

1. Download the latest release DLL from the GitHub Actions artifacts
2. Create and configure your `appsettings.json` in the same directory
3. Run using the .NET runtime: `dotnet MultiModelProxy.dll`

## Using Docker

1. Pull the image: `docker pull netrve/multimodelproxy:latest`
2. Create and configure your `appsettings.json`
3. Run the container:

```bash
docker run -d \
  -p 3000:3000 \
  -v /path/to/appsettings.json:/app/appsettings.json \
  netrve/multimodelproxy:latest
```

## From Source

1. Clone the project
2. Run `dotnet restore` to restore dependencies
3. Configure your `appsettings.json`
4. Run with `dotnet run --project MultiModelProxy/MultiModelProxy.csproj`

# License

This project is licensed under AGPLv3.0 (see included LICENSE file).

The following clause applies on top of it and overrides any conflicting clauses: **This project may not be used in a commercial context under any circumstance unless a commercial
license has been granted by the owner. This stipulation applies on top of the
AGPLv3 license.**
