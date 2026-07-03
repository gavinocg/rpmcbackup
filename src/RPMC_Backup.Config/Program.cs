using System.Diagnostics;
using System.Security.Principal;
using RPMC_Backup.Config;

Application.ThreadException += (s, e) =>
{
    try
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "RPMC_Backup_Config_Error.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ThreadException: {e.Exception}\n");
    }
    catch { }
};
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    try
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "RPMC_Backup_Config_Error.log"),
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UnhandledException: {e.ExceptionObject}\n");
    }
    catch { }
};

var cmdArgs = Environment.GetCommandLineArgs();

if (cmdArgs.Length > 1 && cmdArgs[1] == "--start-service")
{
    try
    {
        var psi = new ProcessStartInfo("sc", "start rpmc-backup-service")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var p = Process.Start(psi);
        p?.WaitForExit(10000);
    }
    catch { }
    return;
}

if (cmdArgs.Length > 1 && cmdArgs[1] == "--install-service")
{
    try
    {
        var cfgDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        var servicePaths = new[]
        {
            Path.Combine(cfgDir, "service", "RPMC_Backup.Service.exe"),
            Path.Combine(cfgDir, "..", "..", "..", "..", "RPMC_Backup.Service", "bin", "Release", "net8.0", "publish", "RPMC_Backup.Service.exe"),
        };
        var serviceBin = servicePaths.FirstOrDefault(File.Exists);
        if (serviceBin == null) return;
        var createPsi = new ProcessStartInfo("sc", $"create rpmc-backup-service binPath=\"{serviceBin}\" start=auto DisplayName=\"RPMC Backup Service\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var cp = Process.Start(createPsi);
        cp?.WaitForExit(10000);
        if (cp?.ExitCode == 0)
        {
            var startPsi = new ProcessStartInfo("sc", "start rpmc-backup-service")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var sp = Process.Start(startPsi);
            sp?.WaitForExit(10000);
        }
    }
    catch { }
    return;
}

if (cmdArgs.Length > 1 && cmdArgs[1] == "--elevated")
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
    return;
}

if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = Environment.ProcessPath,
            Arguments = "--elevated",
            Verb = "runas",
            UseShellExecute = true
        };
        Process.Start(psi);
    }
    catch { }
    return;
}

ApplicationConfiguration.Initialize();
var mainForm = new MainForm();
if (cmdArgs.Length > 1 && cmdArgs[1] == "--minimized")
    mainForm.WindowState = FormWindowState.Minimized;
Application.Run(mainForm);
