using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using JoelRichPodcast.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// Storage clients: use managed identity in Azure, connection string locally
var storageConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
if (!string.IsNullOrEmpty(storageConnectionString))
{
    builder.Services.AddSingleton(new TableServiceClient(storageConnectionString));
    builder.Services.AddSingleton(new BlobServiceClient(storageConnectionString));
}
else
{
    var accountName = Environment.GetEnvironmentVariable("StorageAccountName")
        ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage__accountName")
        ?? "joelrichst";
    var credential = new DefaultAzureCredential();
    builder.Services.AddSingleton(new TableServiceClient(
        new Uri($"https://{accountName}.table.core.windows.net"), credential));
    builder.Services.AddSingleton(new BlobServiceClient(
        new Uri($"https://{accountName}.blob.core.windows.net"), credential));
}

// HTTP clients
builder.Services.AddHttpClient("TorahMusings", client =>
{
    client.Timeout = TimeSpan.FromMinutes(2);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/135.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd(
        "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
});

builder.Services.AddHttpClient("AudioMetadata", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Torah-dl resolution API (Python function app)
var torahDlApiUrl = Environment.GetEnvironmentVariable("TorahDlApiUrl") ?? "http://localhost:7072";
builder.Services.AddHttpClient("TorahDlApi", client =>
{
    client.BaseAddress = new Uri(torahDlApiUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Services
builder.Services.AddSingleton<TorahDlResolver>();
builder.Services.AddSingleton<TorahMusingsFeedParser>();
builder.Services.AddSingleton<PodcastFeedGenerator>();
builder.Services.AddScoped<PodcastPipeline>();

builder.Build().Run();
