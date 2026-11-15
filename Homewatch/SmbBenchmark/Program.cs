using Homewatch.Contracts.Abstractions.Repositories;
using Homewatch.Contracts.Abstractions.Services;
using Homewatch.Core.Services.RemoteStorage;
using Homewatch.Extensions;
using Homewatch.Infrastructure.Data;
using Homewatch.Infrastructure.Repositories;
using Homewatch.SmbBenchmark;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var webProjectDirectory = FindWebProjectDirectory();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = webProjectDirectory
});

builder.Configuration.AddEnvironmentFile(Path.Combine(webProjectDirectory, ".env"));
builder.Logging.SetMinimumLevel(LogLevel.Warning);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new ArgumentNullException("ConnectionStrings__DefaultConnection is not set in the .env file");

builder.Services.AddDbContextFactory<HomewatchDBContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<IRemoteStorageTargetRepository, RemoteStorageTargetRepository>();
builder.Services.AddSingleton<ISmbConnectionService, SmbConnectionService>();
builder.Services.AddSingleton<BenchmarkSettingsPrompt>();
builder.Services.AddSingleton<UploadBenchmark>();

var configuredStorageRoot = builder.Configuration.GetSection("ServerStorage")["RootPath"];
var keysFolder = builder.Configuration.GetSection("ServerStorage")["DataProtectionKeysFolder"]
    ?? throw new ArgumentNullException("DataProtectionKeysFolder not configured");

// The app runs with ServerStorage__RootPath set to the web project folder, so the data protection keys are read from there.
var storageRoot = string.IsNullOrWhiteSpace(configuredStorageRoot)
    ? webProjectDirectory
    : Path.GetFullPath(configuredStorageRoot, webProjectDirectory);

var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("Homewatch")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storageRoot, keysFolder)));

if (OperatingSystem.IsWindows())
{
    dataProtectionBuilder.ProtectKeysWithDpapi();
}

using var host = builder.Build();
using var cancellationTokenSource = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArguments) =>
{
    eventArguments.Cancel = true;
    cancellationTokenSource.Cancel();
    Console.WriteLine("Stopping after the current uploads finish, then deleting the benchmark files...");
};

await host.Services.GetRequiredService<UploadBenchmark>().RunAsync(cancellationTokenSource.Token);

static string FindWebProjectDirectory()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory != null)
    {
        var webProjectDirectory = Path.Combine(directory.FullName, "Homewatch");

        if (File.Exists(Path.Combine(webProjectDirectory, "Homewatch.csproj")))
        {
            return webProjectDirectory;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException($"Could not find the Homewatch web project folder above {AppContext.BaseDirectory}.");
}
