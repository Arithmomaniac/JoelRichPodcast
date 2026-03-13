# Joel Rich Audio Roundup Podcast

Converts Joel Rich's [Audio Roundup](https://www.torahmusings.com/category/audio/) picks from Torah Musings into a subscribable podcast RSS feed.

## Architecture

```
Torah Musings RSS → Parse Audio Roundup → Resolve URIs (torah-dl) → Table Storage → RSS feed → Blob static website
```

- **.NET 10 Azure Functions** (isolated worker) — timer trigger (every 6 hours) + HTTP trigger (manual)
- **[torah-dl](https://github.com/SoferAi/torah-dl)** — resolves audio URLs from 15+ Torah sites (YUTorah, TorahAnytime, etc.)
- **Azure Table Storage** — durable episode store (source of truth)
- **Azure Blob Storage** static website — hosts `feed.xml`
- **Managed identity** — no connection strings in Azure
- **Bicep IaC** — Flex Consumption Function App + Storage + App Insights
- **GitHub Actions CI/CD** — OIDC deployment on push to `master`

### Reference

This project's architecture is modeled on [hadashon-podcast](https://github.com/Arithmomaniac/hadashon-podcast).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
- [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) (local Storage emulator)
- [Python 3.10+](https://www.python.org/downloads/)
- `pip install torah-dl` (for local URI resolution)

## Local Development

```bash
# Start Azurite (in a separate terminal)
azurite --skipApiVersionCheck

# Install Python dependencies
pip install torah-dl

# Run the function app
cd src/JoelRichPodcast.Functions
func start
```

### Manual test

```bash
# Trigger a manual scrape
curl http://localhost:7071/api/scrape

# Trigger the timer function manually
curl -X POST http://localhost:7071/admin/functions/ScrapeAndPublish \
  -H "Content-Type: application/json" -d "{}"
```

## Deployment

### One-time setup

1. Deploy infrastructure: run the `Deploy Infrastructure` workflow (or use Azure CLI)
2. Enable static website on the Storage account (done automatically by the workflow)
3. Create an OIDC service principal for GitHub Actions
4. Set GitHub Secrets:
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`
5. Set GitHub Variable:
   - `AZURE_FUNCTIONAPP_NAME` = `joelrichpodcast-func`

### Continuous deployment

Pushes to `master` trigger the `Deploy to Azure` workflow, which:
1. Builds the .NET project
2. Bundles torah-dl Python dependencies into the deployment package
3. Deploys to Azure Functions via OIDC

## How It Works

1. **Parse RSS** — Fetches the latest Audio Roundup post from the Torah Musings RSS feed
2. **Extract links** — Parses the HTML content with AngleSharp to find all `<li><a>` shiur links
3. **Resolve URLs** — Uses [torah-dl](https://github.com/SoferAi/torah-dl) (via Python subprocess) to resolve page URLs to direct audio download URLs
4. **Enrich metadata** — HTTP HEAD on each audio URL to get content type, length, and last-modified date
5. **Store episodes** — Upserts to Azure Table Storage (deduplication by date + title slug)
6. **Generate feed** — Builds RSS 2.0 + iTunes XML from all stored episodes
7. **Publish** — Uploads `feed.xml` to the `$web` blob container (static website)
