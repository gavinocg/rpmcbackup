using RPMC_Backup.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "rpmc-backup-service";
});
builder.Logging.AddEventLog(settings =>
{
    settings.SourceName = "RPMC Backup Service";
    settings.LogName = "Application";
});
builder.Services.AddHostedService<BackupService>();
builder.Services.AddSingleton<ConfigManager>();
builder.Services.AddSingleton<LogDatabase>();
var host = builder.Build();
host.Run();
