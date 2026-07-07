using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Config;

public class TrayApplicationContext : ApplicationContext
{
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _menu;
    private ToolStripMenuItem _toggleItem;
    private System.Windows.Forms.Timer _statusTimer;
    private readonly System.Windows.Forms.Timer _startupRetryTimer;

    public TrayApplicationContext()
    {
        StatusHelper.Initialize();

        _menu = new ContextMenuStrip();
        _menu.Items.Add("Abrir Configuración", null, (s, e) => LaunchConfig());
        _menu.Items.Add("Sincronizar ahora", null, (s, e) => SendIpc(Constants.CmdSyncNow));
        _toggleItem = new ToolStripMenuItem("Detener servicio");
        _toggleItem.Click += (s, e) =>
        {
            if (_toggleItem.Text == "Detener servicio")
            {
                if (!PromptAdminPassword("Detener servicio")) return;
                SendIpc(Constants.CmdStop);
            }
            else
            {
                SendIpc(Constants.CmdResume);
            }
        };
        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Salir", null, (s, e) => RunWithPassword("Salir del aplicativo"));

        _trayIcon = new NotifyIcon
        {
            Text = "RPMC Backup",
            ContextMenuStrip = _menu,
            Visible = true,
            Icon = StatusHelper.IconGray
        };
        _trayIcon.DoubleClick += (s, e) => LaunchConfig();

        TryStartService();

        _statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _statusTimer.Tick += (s, e) => PollService();
        _statusTimer.Start();

        _startupRetryTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _startupRetryTimer.Tick += (s, e) => TryStartService();
        _startupRetryTimer.Start();
    }

    private void TryStartService()
    {
        try
        {
            using var svc = new System.ServiceProcess.ServiceController(Constants.ServiceName);
            if (svc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
            {
                svc.Start();
                svc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
            }
            _startupRetryTimer?.Stop();
            _startupRetryTimer?.Dispose();
        }
        catch { }
    }

    private void PollService()
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient("127.0.0.1", Constants.IpcPort);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new IpcRequest { Command = Constants.CmdGetStatus }));
            using var reader = new StreamReader(stream);
            var responseJson = reader.ReadLine();
            if (responseJson != null)
            {
                var response = JsonSerializer.Deserialize<IpcResponse>(responseJson);
                if (response?.Success == true && response.State != null)
                {
                    UpdateTray(response.State);
                    return;
                }
            }
        }
        catch { }
        _trayIcon.Icon = StatusHelper.IconGray;
        _trayIcon.Text = "RPMC Backup - Servicio no disponible";
    }

    private void UpdateTray(ServiceStateInfo state)
    {
        var info = StatusHelper.Resolve(state);
        _trayIcon.Icon = info.Icon;
        _trayIcon.Text = info.TrayText;

        _toggleItem.Text = state.Status == ServiceStatus.Stopped
            ? "Iniciar servicio"
            : "Detener servicio";
    }

    private void SendIpc(string command, string payload = "")
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient("127.0.0.1", Constants.IpcPort);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new IpcRequest { Command = command, Payload = payload }));
        }
        catch { }
    }

    private static void LaunchConfig()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "RPMC_Backup.Config.exe",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void RunWithPassword(string action)
    {
        if (!PromptAdminPassword(action))
            return;
        Application.Exit();
    }

    private bool PromptAdminPassword(string action)
    {
        var cfg = LoadConfig();
        if (cfg == null || string.IsNullOrEmpty(cfg.AdminHash))
            return true;
        if (!BCrypt.Net.BCrypt.Verify(ShowPasswordDialog(action), cfg.AdminHash))
        {
            MessageBox.Show("Clave incorrecta.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private static string ShowPasswordDialog(string action)
    {
        using var form = new Form
        {
            Text = $"RPMC Backup - {action}",
            Size = new Size(380, 170),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true
        };
        var lbl = new Label { Text = $"Ingrese clave de administrador para\n{action}:", Location = new Point(20, 20), AutoSize = true };
        var txt = new TextBox { Location = new Point(20, 55), Width = 320, UseSystemPasswordChar = true };
        var btnOk = new Button { Location = new Point(120, 90), Size = new Size(100, 30), Text = "Aceptar", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Location = new Point(230, 90), Size = new Size(100, 30), Text = "Cancelar", DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK)
            return txt.Text;
        return string.Empty;
    }

    private static AppConfig? LoadConfig()
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Constants.ConfigDir, Constants.ConfigFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 4 || data[0] != 0x52 || data[1] != 0x50 || data[2] != 0x4D || data[3] != 0x43) return null;
            var json = System.Text.Encoding.UTF8.GetString(data, 4, data.Length - 4);
            return JsonSerializer.Deserialize<AppConfig>(json);
        }
        catch { return null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer?.Stop();
            _statusTimer?.Dispose();
            _startupRetryTimer?.Stop();
            _startupRetryTimer?.Dispose();
            if (_trayIcon != null) _trayIcon.Visible = false;
            _trayIcon?.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
