using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using Minio;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Config;

public class MainForm : Form
{
    private TabControl _tabControl;
    private TabPage _tabHome, _tabFolders, _tabFileLogs, _tabSysLogs, _tabSecurity;
    private NotifyIcon _trayIcon;
    private ContextMenuStrip _trayMenu;
    private System.Windows.Forms.Timer _statusTimer;
    private Label _lblServiceStatus, _lblLastSync, _lblErrors, _lblPending;
    private Button _btnPause, _btnResume, _btnStop;
    private ListView _foldersList;
    private Button _btnAddFolder, _btnRemoveFolder;
    private DataGridView _logGrid;
    private ComboBox _cmbLogLevel;
    private TextBox _txtSearch;
    private Button _btnFilter, _btnRetry;
    private RichTextBox _txtLogDetail;
    private TextBox _txtOldPassword, _txtNewPassword, _txtConfirmPassword;
    private Button _btnChangePassword;
    private Panel _statusIndicator;
    private bool _closingToTray = true;
    private ToolStripMenuItem _trayToggleItem;
    private TextBox _txtConnEndpoint, _txtConnAccessKey, _txtConnSecretKey, _txtConnMachineName, _txtConnRegion;
    private ComboBox _cmbConnBucket;
    private DataGridView _sysLogGrid;
    private ComboBox _cmbSysLogLevel;
    private CheckBox _chkConnSsl, _chkAutoStart;
    private Button _btnConnTest;
    private Label _lblConnResult;
    private TextBox _txtSmtpHost, _txtSmtpPort, _txtSmtpUser, _txtSmtpPass, _txtSmtpFrom, _txtAdminEmailConfig;
    private CheckBox _chkSmtpSsl;
    private Button _btnSaveSmtp, _btnTestEmail;
    private FlowLayoutPanel _foldersProgressPanel;

    public MainForm()
    {
        Text = "RPMC Backup - Configuración";
        Size = new Size(900, 650);
        StartPosition = FormStartPosition.CenterScreen;
        try { using var s = typeof(MainForm).Assembly.GetManifestResourceStream("RPMC_Backup.Config.app_icon.ico"); if (s != null) Icon = new Icon(s); } catch { }
        FormClosing += MainForm_FormClosing;
        Resize += MainForm_Resize;

        CreateTrayIcon();
        CreateTabControl();
        CreateParametrosTab();
        CreateOrigenesTab();
        CreateFileLogsTab();
        CreateSysLogsTab();
        CreateSecurityTab();
        Load += MainForm_Load;
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new ContextMenuStrip();
        _trayMenu.Items.Add("Abrir Configuración", null, (s, e) => { if (PromptAdminPassword("Abrir Configuración", topMost: true)) { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; } });
        _trayToggleItem = new ToolStripMenuItem("Detener servicio");
        _trayToggleItem.Click += (s, e) => {
            if (_trayToggleItem.Text == "Detener servicio")
            {
                if (!PromptAdminPassword("Detener servicio")) return;
                SendIpc(Constants.CmdStop);
                _trayToggleItem.Text = "Iniciar servicio";
            }
            else
            {
                SendIpc(Constants.CmdResume);
                _trayToggleItem.Text = "Detener servicio";
            }
        };
        _trayMenu.Items.Add(_trayToggleItem);
        _trayMenu.Items.Add("Sincronizar ahora", null, (s, e) => { SendIpc(Constants.CmdSyncNow); });
        _trayMenu.Items.Add("Salir", null, (s, e) => {
            if (PromptAdminPassword("Salir del aplicativo"))
            {
                _closingToTray = false;
                Application.Exit();
            }
        });

        _trayIcon = new NotifyIcon
        {
            Text = "RPMC Backup",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (s, e) => { if (PromptAdminPassword("Abrir Configuración", topMost: true)) { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; } };
        SetTrayIcon(ServiceStatus.Unknown);
    }

    private void SetTrayIcon(ServiceStatus status)
    {
        var color = status switch
        {
            ServiceStatus.Running => Color.Green,
            ServiceStatus.Paused or ServiceStatus.Degraded => Color.Orange,
            ServiceStatus.Error or ServiceStatus.Stopped => Color.Red,
            _ => Color.Gray
        };
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillEllipse(new SolidBrush(color), 1, 1, 14, 14);
        var hIcon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hIcon);
        _trayIcon.Icon = (Icon)temp.Clone();
    }

    private void CreateTabControl()
    {
        _tabControl = new TabControl { Dock = DockStyle.Fill };
        _tabHome = new TabPage("Parámetros");
        _tabFolders = new TabPage("Orígenes");
        _tabFileLogs = new TabPage("Logs Archivos");
        _tabSysLogs = new TabPage("Logs Sistema");
        _tabSecurity = new TabPage("Seguridad");
        _tabControl.TabPages.AddRange(new[] { _tabHome, _tabFolders, _tabFileLogs, _tabSysLogs, _tabSecurity });
        Controls.Add(_tabControl);
    }

    private void CreateParametrosTab()
    {
        var statusGroup = new GroupBox { Text = "Estado del servicio", Location = new Point(15, 10), Size = new Size(850, 65) };
        _statusIndicator = new Panel { Location = new Point(15, 20), Size = new Size(14, 14) };
        _lblServiceStatus = new Label { Location = new Point(35, 18), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        _lblPending = new Label { Location = new Point(220, 18), AutoSize = true, ForeColor = Color.Gray };
        _lblLastSync = new Label { Location = new Point(35, 38), AutoSize = true, ForeColor = Color.Gray };
        _lblErrors = new Label { Location = new Point(430, 18), AutoSize = true, ForeColor = Color.Gray };
        _btnStop = new Button { Location = new Point(630, 15), Size = new Size(60, 22), Text = "Detener" };
        _btnPause = new Button { Location = new Point(700, 15), Size = new Size(60, 22), Text = "Pausar" };
        _btnResume = new Button { Location = new Point(770, 15), Size = new Size(65, 22), Text = "Reanudar", Enabled = false };
        _btnStop.Click += (s, e) => { SendIpc(Constants.CmdStop); RefreshStatus(); };
        _btnPause.Click += (s, e) => { SendIpc(Constants.CmdPause); RefreshStatus(); };
        _btnResume.Click += (s, e) => { SendIpc(Constants.CmdResume); RefreshStatus(); };
        statusGroup.Controls.AddRange(new Control[] { _statusIndicator, _lblServiceStatus, _lblPending, _lblLastSync, _lblErrors, _btnStop, _btnPause, _btnResume });

        var groupInfo = new GroupBox { Text = "Configuración del servidor S3", Location = new Point(15, 85), Size = new Size(850, 295) };
        var lblEp = new Label { Text = "Endpoint:", Location = new Point(15, 25), AutoSize = true };
        var lblAk = new Label { Text = "Access Key:", Location = new Point(15, 55), AutoSize = true };
        var lblSk = new Label { Text = "Secret Key:", Location = new Point(15, 85), AutoSize = true };
        var lblBk = new Label { Text = "Bucket:", Location = new Point(15, 115), AutoSize = true };
        var lblRg = new Label { Text = "Región:", Location = new Point(15, 145), AutoSize = true };
        var lblMc = new Label { Text = "Equipo:", Location = new Point(15, 175), AutoSize = true };
        _txtConnEndpoint = new TextBox { Location = new Point(120, 22), Width = 400 };
        _txtConnAccessKey = new TextBox { Location = new Point(120, 52), Width = 400 };
        _txtConnSecretKey = new TextBox { Location = new Point(120, 82), Width = 400, UseSystemPasswordChar = true };
        _cmbConnBucket = new ComboBox { Location = new Point(120, 112), Width = 400, DropDownStyle = ComboBoxStyle.DropDown };
        _txtConnRegion = new TextBox { Location = new Point(120, 142), Width = 400 };
        _txtConnMachineName = new TextBox { Location = new Point(120, 172), Width = 400, ReadOnly = true };
        _chkConnSsl = new CheckBox { Text = "Usar SSL (HTTPS)", Location = new Point(120, 200), AutoSize = true };
        _chkAutoStart = new CheckBox { Text = "Iniciar con Windows", Location = new Point(120, 220), AutoSize = true, Checked = true };
        _btnConnTest = new Button { Location = new Point(120, 250), Size = new Size(130, 28), Text = "Probar Conexión" };
        _lblConnResult = new Label { Location = new Point(260, 255), AutoSize = true };
        _btnConnTest.Click += async (s, e) =>
        {
            _btnConnTest.Enabled = false;
            _lblConnResult.Text = "Probando...";
            _lblConnResult.ForeColor = Color.Blue;
            try
            {
                var client = new MinioClient()
                    .WithEndpoint(_txtConnEndpoint.Text)
                    .WithCredentials(_txtConnAccessKey.Text, _txtConnSecretKey.Text);
                if (!_chkConnSsl.Checked) client.WithSSL(false);
                client.Build();
                var buckets = await client.ListBucketsAsync();
                _cmbConnBucket.Items.Clear();
                foreach (var b in buckets.Buckets)
                    _cmbConnBucket.Items.Add(b.Name);
                if (_cmbConnBucket.Items.Count > 0)
                    _cmbConnBucket.SelectedIndex = 0;
                _lblConnResult.Text = $"Conexión exitosa ({buckets.Buckets.Count} buckets)";
                _lblConnResult.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                _cmbConnBucket.Items.Clear();
                _cmbConnBucket.Text = "sin Conexión";
                _lblConnResult.Text = $"Error: {ex.Message}";
                _lblConnResult.ForeColor = Color.Red;
            }
            _btnConnTest.Enabled = true;
        };
        groupInfo.Controls.AddRange(new Control[] { lblEp, lblAk, lblSk, lblBk, lblRg, lblMc,
            _txtConnEndpoint, _txtConnAccessKey, _txtConnSecretKey, _cmbConnBucket, _txtConnRegion, _txtConnMachineName,
            _chkConnSsl, _chkAutoStart, _btnConnTest, _lblConnResult
        });

        var btnSave = new Button { Text = "Guardar Cambios", Size = new Size(130, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnSave.Left = _tabHome.Width - btnSave.Width - 15;
        btnSave.Top = _tabHome.Height - btnSave.Height - 15;
        btnSave.Click += (s, e) =>
        {
            if (!PromptAdminPassword("Guardar Configuración")) return;
            var cfg = LoadConfig();
            if (cfg == null) return;
            cfg.MinioEndpoint = _txtConnEndpoint.Text;
            cfg.MinioAccessKey = _txtConnAccessKey.Text;
            cfg.MinioSecretKey = _txtConnSecretKey.Text;
            cfg.MinioUseSsl = _chkConnSsl.Checked;
            cfg.BucketName = _cmbConnBucket.Text;
            cfg.MachineName = _txtConnMachineName.Text;
            cfg.S3Region = _txtConnRegion.Text;
            SaveConfig(cfg);
            try
            {
                var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (reg != null)
                {
                    if (_chkAutoStart.Checked)
                        reg.SetValue("RPMC Backup", $"\"{Environment.ProcessPath}\" --minimized");
                    else
                        reg.DeleteValue("RPMC Backup", false);
                    reg.Dispose();
                }
            }
            catch { }
            SendIpc(Constants.CmdReconfig);
            MessageBox.Show("Configuración guardada.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        _tabHome.Controls.AddRange(new Control[] { statusGroup, groupInfo, btnSave });
    }
    private void CreateOrigenesTab()
    {
        var lblTree = new Label { Text = "Navegar carpetas:", Location = new Point(10, 10), AutoSize = true };
        var treeFolders = new TreeView { Location = new Point(10, 30), Size = new Size(300, 310), ShowNodeToolTips = true };
        PopulateDriveNodes(treeFolders);
        treeFolders.BeforeExpand += (s, e) => PopulateSubfolders(e.Node);

        _foldersList = new ListView { Location = new Point(320, 30), Size = new Size(530, 310), View = View.Details, FullRowSelect = true };
        _foldersList.Columns.Add("Carpeta", 350);
        _foldersList.Columns.Add("Recursivo", 80);

        var txtFolderPath = new TextBox { Location = new Point(10, 350), Width = 400, ReadOnly = true };
        treeFolders.AfterSelect += (s, e) => txtFolderPath.Text = e.Node.Tag?.ToString() ?? e.Node.FullPath;

        _btnAddFolder = new Button { Location = new Point(420, 348), Size = new Size(100, 28), Text = "Agregar >>" };
        _btnAddFolder.Click += (s, e) => {
            var path = txtFolderPath.Text.Trim();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            var cfg = LoadConfig();
            if (cfg != null && !cfg.Folders.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                cfg.Folders.Add(new FolderConfig { Path = path, Recursive = true });
                SaveConfig(cfg);
                RefreshFolders();
                SendIpc(Constants.CmdReconfig);
            }
        };

        _btnRemoveFolder = new Button { Location = new Point(530, 348), Size = new Size(100, 28), Text = "Quitar" };
        _btnRemoveFolder.Click += (s, e) => {
            if (_foldersList.SelectedItems.Count > 0)
            {
                var cfg = LoadConfig();
                if (cfg != null)
                {
                    var path = _foldersList.SelectedItems[0].Text;
                    cfg.Folders.RemoveAll(f => f.Path == path);
                    SaveConfig(cfg);
                    RefreshFolders();
                    SendIpc(Constants.CmdReconfig);
                }
            }
        };

        var btnSaveOrig = new Button { Text = "Guardar Cambios", Size = new Size(130, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnSaveOrig.Left = _tabFolders.Width - btnSaveOrig.Width - 15;
        btnSaveOrig.Top = _tabFolders.Height - btnSaveOrig.Height - 15;
        btnSaveOrig.Click += (s, e) => { if (!PromptAdminPassword("Guardar origenes")) return; SendIpc(Constants.CmdReconfig); MessageBox.Show("Configuración de origenes guardada.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information); };

        var progGroup = new GroupBox { Text = "Progreso de sincronización", Location = new Point(10, 385), Size = new Size(500, 120) };
        _foldersProgressPanel = new FlowLayoutPanel { Location = new Point(10, 20), Size = new Size(480, 90), AutoScroll = true };
        progGroup.Controls.Add(_foldersProgressPanel);

        _tabFolders.Controls.AddRange(new Control[] { lblTree, treeFolders, _foldersList, txtFolderPath, _btnAddFolder, _btnRemoveFolder, progGroup, btnSaveOrig });
    }

    private void PopulateDriveNodes(TreeView tree)
    {
        tree.Nodes.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var node = new TreeNode(drive.Name.TrimEnd('\\')) { Tag = drive.Name };
            try { node.Nodes.Add(new TreeNode("*")); } catch { }
            tree.Nodes.Add(node);
        }
    }

    private void PopulateSubfolders(TreeNode parentNode)
    {
        if (parentNode.Nodes.Count != 1 || parentNode.Nodes[0].Text != "*") return;
        parentNode.Nodes.Clear();
        var path = parentNode.Tag?.ToString() ?? parentNode.FullPath;
        foreach (var dir in Directory.GetDirectories(path))
        {
            try
            {
                var node = new TreeNode(Path.GetFileName(dir)) { Tag = dir };
                try { if (Directory.GetDirectories(dir).Length > 0) node.Nodes.Add(new TreeNode("*")); } catch { }
                parentNode.Nodes.Add(node);
            }
            catch { }
        }
    }

    private void CreateFileLogsTab()
    {
        var filterPanel = new Panel { Location = new Point(10, 10), Size = new Size(850, 40) };
        _cmbLogLevel = new ComboBox { Location = new Point(0, 5), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbLogLevel.Items.AddRange(new[] { "Todos", "Info", "Warn", "Error" });
        _cmbLogLevel.SelectedIndex = 0;
        _txtSearch = new TextBox { Location = new Point(130, 5), Width = 200, PlaceholderText = "Buscar archivo..." };
        _btnFilter = new Button { Location = new Point(340, 3), Size = new Size(80, 25), Text = "Filtrar" };
        _btnRetry = new Button { Location = new Point(430, 3), Size = new Size(80, 25), Text = "Reintentar" };
        var _btnExclude = new Button { Location = new Point(520, 3), Size = new Size(80, 25), Text = "Excluir" };
        var _btnClear = new Button { Location = new Point(610, 3), Size = new Size(90, 25), Text = "Borrar logs" };
        _btnClear.Click += (s, e) => { if (MessageBox.Show("Borrar logs de archivos?", "", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { SendIpc(Constants.CmdClearLogs); RefreshFileLogs(); } };

        _logGrid = new DataGridView {
            Location = new Point(10, 55), Size = new Size(850, 280),
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, MultiSelect = false
        };
        _logGrid.Columns.Add("ts", "Fecha"); _logGrid.Columns.Add("level", "Nivel");
        _logGrid.Columns.Add("folder", "Carpeta"); _logGrid.Columns.Add("file", "Archivo");
        _logGrid.Columns.Add("msg", "Mensaje");
        _logGrid.Columns[0].Width = 140; _logGrid.Columns[1].Width = 50;
        _logGrid.Columns[2].Width = 120; _logGrid.Columns[3].Width = 350;
        _logGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _logGrid.SelectionChanged += (s, e) => ShowLogDetail();

        _txtLogDetail = new RichTextBox { Location = new Point(10, 340), Size = new Size(850, 120), ReadOnly = true, BackColor = Color.White };

        _btnFilter.Click += (s, e) => RefreshFileLogs();
        _btnRetry.Click += (s, e) => {
            if (_logGrid.SelectedRows.Count > 0) {
                var file = _logGrid.SelectedRows[0].Cells[3].Value?.ToString() ?? "";
                SendIpc(Constants.CmdRetry, file);
                MessageBox.Show("Archivo encolado para reintentar.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        _btnExclude.Click += (s, e) => {
            if (_logGrid.SelectedRows.Count > 0) {
                var file = _logGrid.SelectedRows[0].Cells[3].Value?.ToString() ?? "";
                var cfg = LoadConfig();
                if (cfg != null && cfg.ExcludedFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                {
                    cfg.ExcludedFiles.RemoveAll(f => f.Equals(file, StringComparison.OrdinalIgnoreCase));
                    SendIpc(Constants.CmdIncludeFile, file);
                }
                else
                {
                    cfg?.ExcludedFiles.Add(file);
                    SendIpc(Constants.CmdExcludeFile, file);
                }
                if (cfg != null) SaveConfig(cfg);
                RefreshFileLogs();
            }
        };
        filterPanel.Controls.AddRange(new Control[] { _cmbLogLevel, _txtSearch, _btnFilter, _btnRetry, _btnExclude, _btnClear });
        _tabFileLogs.Controls.AddRange(new Control[] { filterPanel, _logGrid, _txtLogDetail });
    }

    private void CreateSysLogsTab()
    {
        var panel = new Panel { Location = new Point(10, 10), Size = new Size(850, 40) };
        _cmbSysLogLevel = new ComboBox { Location = new Point(0, 5), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbSysLogLevel.Items.AddRange(new[] { "Todos", "Info", "Warn", "Error" }); _cmbSysLogLevel.SelectedIndex = 0;
        var btnFilter = new Button { Location = new Point(130, 3), Size = new Size(80, 25), Text = "Filtrar" };
        var _btnClear = new Button { Location = new Point(220, 3), Size = new Size(90, 25), Text = "Borrar logs" };
        _btnClear.Click += (s, e) => { if (MessageBox.Show("Borrar logs de sistema?", "", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { SendIpc(Constants.CmdClearSysLogs); RefreshSysLogs(); } };

        _sysLogGrid = new DataGridView {
            Location = new Point(10, 55), Size = new Size(850, 280),
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
        };
        _sysLogGrid.Columns.Add("ts", "Fecha"); _sysLogGrid.Columns.Add("level", "Nivel");
        _sysLogGrid.Columns.Add("src", "Origen"); _sysLogGrid.Columns.Add("msg", "Mensaje");
        _sysLogGrid.Columns[0].Width = 140; _sysLogGrid.Columns[1].Width = 50; _sysLogGrid.Columns[2].Width = 100;

        var detail = new RichTextBox { Location = new Point(10, 340), Size = new Size(850, 120), ReadOnly = true, BackColor = Color.White };
        _sysLogGrid.SelectionChanged += (s, e) => {
            if (_sysLogGrid.SelectedRows.Count == 0) { detail.Clear(); return; }
            var r = _sysLogGrid.SelectedRows[0];
            detail.Clear();
            detail.AppendText($"{r.Cells[1].Value} - {r.Cells[0].Value}\n");
            detail.AppendText($"Origen: {r.Cells[2].Value}\n");
            detail.AppendText($"Mensaje: {r.Cells[3].Value}\n");
        };
        btnFilter.Click += (s, e) => {
            var levelIdx = _cmbSysLogLevel.SelectedIndex;
            RefreshSysLogs(levelIdx > 0 ? (levelIdx - 1).ToString() : null);
        };
        panel.Controls.AddRange(new Control[] { _cmbSysLogLevel, btnFilter, _btnClear });
        _tabSysLogs.Controls.AddRange(new Control[] { panel, _sysLogGrid, detail });
    }
    private void CreateSecurityTab()
    {
        var groupBox = new GroupBox { Text = "Cambiar clave de administrador", Location = new Point(15, 15), Size = new Size(420, 160) };
        var lblOld = new Label { Text = "Clave actual:", Location = new Point(15, 30), AutoSize = true };
        var lblNew = new Label { Text = "Nueva clave:", Location = new Point(15, 60), AutoSize = true };
        var lblConfirm = new Label { Text = "Confirmar:", Location = new Point(15, 90), AutoSize = true };
        _txtOldPassword = new TextBox { Location = new Point(130, 27), Width = 250, UseSystemPasswordChar = true };
        _txtNewPassword = new TextBox { Location = new Point(130, 57), Width = 250, UseSystemPasswordChar = true };
        _txtConfirmPassword = new TextBox { Location = new Point(130, 87), Width = 250, UseSystemPasswordChar = true };
        _btnChangePassword = new Button { Location = new Point(130, 120), Size = new Size(120, 30), Text = "Cambiar Clave" };
        _btnChangePassword.Click += (s, e) => ChangePassword();
        groupBox.Controls.AddRange(new Control[] { lblOld, lblNew, lblConfirm, _txtOldPassword, _txtNewPassword, _txtConfirmPassword, _btnChangePassword });

        var groupSmtp = new GroupBox { Text = "Configuración SMTP (recuperación de clave)", Location = new Point(15, 185), Size = new Size(420, 280) };
        var lblEmail = new Label { Text = "Correo admin:", Location = new Point(15, 25), AutoSize = true };
        var lblHost = new Label { Text = "Servidor SMTP:", Location = new Point(15, 55), AutoSize = true };
        var lblPort = new Label { Text = "Puerto:", Location = new Point(15, 85), AutoSize = true };
        var lblUser = new Label { Text = "Usuario:", Location = new Point(15, 115), AutoSize = true };
        var lblPass = new Label { Text = "Contraseña:", Location = new Point(15, 145), AutoSize = true };
        var lblFrom = new Label { Text = "Remitente:", Location = new Point(15, 175), AutoSize = true };
        _txtAdminEmailConfig = new TextBox { Location = new Point(140, 22), Width = 250 };
        _txtSmtpHost = new TextBox { Location = new Point(140, 52), Width = 250, Text = "smtp.gmail.com" };
        _txtSmtpPort = new TextBox { Location = new Point(140, 82), Width = 80, Text = "587" };
        _txtSmtpUser = new TextBox { Location = new Point(140, 112), Width = 250, Text = "mailingrpmcc@gmail.com" };
        _txtSmtpPass = new TextBox { Location = new Point(140, 142), Width = 250, UseSystemPasswordChar = true, Text = "vtwlvgserdahpfrx" };
        _txtSmtpFrom = new TextBox { Location = new Point(140, 172), Width = 250, Text = "mailingrpmcc@gmail.com" };
        _chkSmtpSsl = new CheckBox { Text = "Usar SSL", Location = new Point(140, 200), AutoSize = true, Checked = true };
        _btnSaveSmtp = new Button { Location = new Point(140, 230), Size = new Size(100, 30), Text = "Guardar" };
        _btnTestEmail = new Button { Location = new Point(250, 230), Size = new Size(130, 30), Text = "Probar envío" };
        _btnSaveSmtp.Click += (s, e) => SaveSmtpConfig();
        _btnTestEmail.Click += (s, e) => TestEmail();
        groupSmtp.Controls.AddRange(new Control[] { lblEmail, lblHost, lblPort, lblUser, lblPass, lblFrom,
            _txtAdminEmailConfig, _txtSmtpHost, _txtSmtpPort, _txtSmtpUser, _txtSmtpPass, _txtSmtpFrom, _chkSmtpSsl, _btnSaveSmtp, _btnTestEmail });

        _tabSecurity.Controls.AddRange(new Control[] { groupBox, groupSmtp });
    }

    private void SaveSmtpConfig()
    {
        if (string.IsNullOrWhiteSpace(_txtAdminEmailConfig.Text) ||
            string.IsNullOrWhiteSpace(_txtSmtpHost.Text) ||
            string.IsNullOrWhiteSpace(_txtSmtpPort.Text) ||
            string.IsNullOrWhiteSpace(_txtSmtpUser.Text) ||
            string.IsNullOrWhiteSpace(_txtSmtpPass.Text) ||
            string.IsNullOrWhiteSpace(_txtSmtpFrom.Text))
        {
            MessageBox.Show("Todos los campos son obligatorios.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var cfg = LoadConfig();
        if (cfg == null) return;
        cfg.AdminEmail = _txtAdminEmailConfig.Text;
        cfg.SmtpHost = _txtSmtpHost.Text;
        cfg.SmtpPort = int.TryParse(_txtSmtpPort.Text, out var p) ? p : 587;
        cfg.SmtpUser = _txtSmtpUser.Text;
        cfg.SmtpPass = _txtSmtpPass.Text;
        cfg.SmtpFrom = _txtSmtpFrom.Text;
        cfg.SmtpUseSsl = _chkSmtpSsl.Checked;
        SaveConfig(cfg);
        MessageBox.Show("Configuración SMTP guardada.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void TestEmail()
    {
        var cfg = LoadConfig();
        if (cfg == null || string.IsNullOrEmpty(cfg.AdminEmail))
        { MessageBox.Show("Configure primero el correo del administrador.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        try
        {
            SaveSmtpConfig();
            cfg = LoadConfig();
            using var smtp = new System.Net.Mail.SmtpClient(cfg.SmtpHost, cfg.SmtpPort);
            smtp.EnableSsl = cfg.SmtpUseSsl;
            smtp.Credentials = new System.Net.NetworkCredential(cfg.SmtpUser, cfg.SmtpPass);
            smtp.Timeout = 10000;
            var msg = new System.Net.Mail.MailMessage(cfg.SmtpFrom, cfg.AdminEmail, "RPMC Backup - Prueba", "Este es un mensaje de prueba SMTP.");
            smtp.Send(msg);
            MessageBox.Show("Correo de prueba enviado correctamente.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al enviar: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SendAlertEmail(string subject, string body)
    {
        try
        {
            var cfg = LoadConfig();
            if (cfg == null || string.IsNullOrEmpty(cfg.AdminEmail) || string.IsNullOrEmpty(cfg.SmtpHost))
                return;
            using var smtp = new System.Net.Mail.SmtpClient(cfg.SmtpHost, cfg.SmtpPort);
            smtp.EnableSsl = cfg.SmtpUseSsl;
            smtp.Credentials = new System.Net.NetworkCredential(cfg.SmtpUser, cfg.SmtpPass);
            smtp.Timeout = 10000;
            using var msg = new System.Net.Mail.MailMessage(cfg.SmtpFrom, cfg.AdminEmail, subject, body);
            smtp.Send(msg);
        }
        catch { }
    }

    private bool PromptAdminPassword(string action, bool topMost = false)
    {
        var config = LoadConfig();
        if (config == null || string.IsNullOrEmpty(config.AdminHash))
            return true;

        using var form = new Form
        {
            Text = $"RPMC Backup - {action}",
            Size = new Size(380, 210),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = topMost
        };
        var lbl = new Label { Text = $"Ingrese clave de administrador para\n{action}:", Location = new Point(20, 20), AutoSize = true };
        var txt = new TextBox { Location = new Point(20, 55), Width = 320, UseSystemPasswordChar = true };
        var btnOk = new Button { Location = new Point(120, 90), Size = new Size(100, 30), Text = "Aceptar" };
        var btnCancel = new Button { Location = new Point(230, 90), Size = new Size(100, 30), Text = "Cancelar", DialogResult = DialogResult.Cancel };
        var lnkForgot = new LinkLabel { Text = "Olvidó su clave?", Location = new Point(20, 130), AutoSize = true };
        form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel, lnkForgot });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        bool ok = false;
        btnOk.Click += (s, ev) =>
        {
            if (BCrypt.Net.BCrypt.Verify(txt.Text, config.AdminHash))
            {
                ok = true;
                form.Close();
            }
            else
            {
                MessageBox.Show("Clave incorrecta.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txt.Clear();
                txt.Focus();
            }
        };

        lnkForgot.LinkClicked += (s, ev) =>
        {
            form.Hide();
            ResetPasswordViaEmail(config);
            form.Close();
        };

        form.ShowDialog(this);
        return ok;
    }

    private void ResetPasswordViaEmail(AppConfig config)
    {
        if (string.IsNullOrEmpty(config.AdminEmail))
        {
            MessageBox.Show("No hay correo electrónico configurado. Contacte al administrador de TICS.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var pin = new Random().Next(100000, 999999).ToString();
        var expires = DateTime.UtcNow.AddMinutes(Constants.PinExpiryMinutes);
        var pinData = $"{pin}|{expires:O}";
        var pinDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir);
        Directory.CreateDirectory(pinDir);
        File.WriteAllText(Path.Combine(pinDir, Constants.PinFileName), pinData);

        try
        {
            using var smtp = new System.Net.Mail.SmtpClient(config.SmtpHost, config.SmtpPort);
            smtp.EnableSsl = config.SmtpUseSsl;
            smtp.Credentials = new System.Net.NetworkCredential(config.SmtpUser, config.SmtpPass);
            smtp.Timeout = 10000;
            var msg = new System.Net.Mail.MailMessage(
                config.SmtpFrom,
                config.AdminEmail,
                "RPMC Backup - Código de recuperación de clave",
                $"Su código de verificación es: {pin}\n\n" +
                $"Emitido: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (hora local Ecuador)\n" +
                $"Válido hasta: {expires.ToLocalTime():yyyy-MM-dd HH:mm:ss} (hora local Ecuador)\n" +
                $"Válido por {Constants.PinExpiryMinutes} minutos.\n\n" +
                $"Si no solicitó este cambio, ignore este mensaje."
            );
            smtp.Send(msg);
            MessageBox.Show($"Se ha enviado un código de verificación a {config.AdminEmail}.\nEmitido: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo enviar el correo: {ex.Message}\n\nSu código de verificación es: {pin}\nEmitido: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nVálido hasta: {expires.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n(Anote este código)", "Error de envío", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        using var pinForm = new Form
        {
            Text = "RPMC Backup - Recuperar clave",
            Size = new Size(380, 200),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        };
        var lblPin = new Label { Text = $"Ingrese el código enviado a\n{config.AdminEmail}:", Location = new Point(20, 20), AutoSize = true };
        var txtPin = new TextBox { Location = new Point(20, 55), Width = 320 };
        var lblNew = new Label { Text = "Nueva clave:", Location = new Point(20, 90), AutoSize = true };
        var txtNew = new TextBox { Location = new Point(120, 87), Width = 220, UseSystemPasswordChar = true };
        var btnReset = new Button { Location = new Point(120, 125), Size = new Size(120, 30), Text = "Restablecer" };
        pinForm.Controls.AddRange(new Control[] { lblPin, txtPin, lblNew, txtNew, btnReset });

        btnReset.Click += (s, ev) =>
        {
            var stored = File.ReadAllText(Path.Combine(pinDir, Constants.PinFileName));
            var parts = stored.Split('|');
            if (parts.Length < 2) { MessageBox.Show("Error al leer código.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (DateTime.TryParse(parts[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var exp) && DateTime.UtcNow > exp)
            {
                MessageBox.Show($"El código ha expirado. (Emitido: {exp.ToLocalTime():yyyy-MM-dd HH:mm:ss}, expiró: {exp.ToLocalTime():yyyy-MM-dd HH:mm:ss}). Solicite uno nuevo.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (parts[0] != txtPin.Text.Trim())
            {
                MessageBox.Show("Código incorrecto.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (txtNew.Text.Length < 6)
            {
                MessageBox.Show("La clave debe tener al menos 6 caracteres.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            config.AdminHash = BCrypt.Net.BCrypt.HashPassword(txtNew.Text);
            SaveConfig(config);
            File.Delete(Path.Combine(pinDir, Constants.PinFileName));
            MessageBox.Show("Clave restablecida correctamente.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            pinForm.Close();
        };

        pinForm.ShowDialog(this);
    }

    private void MainForm_Load(object? sender, EventArgs e)
    {
        if (!CheckConfigExists())
        {
            using var wizard = new InitWizardForm();
            wizard.StartPosition = FormStartPosition.CenterScreen;
            wizard.TopMost = true;
            if (wizard.ShowDialog() == DialogResult.OK)
            {
                ShowInTaskbar = false;
                Hide();
                _trayIcon.ShowBalloonTip(3000, "RPMC Backup", "Configuración inicial completada. La aplicacion se ejecuta en segundo plano.", ToolTipIcon.Info);
                _statusTimer = new System.Windows.Forms.Timer { Interval = Constants.TrayPollIntervalMs };
                _statusTimer.Tick += async (s, ev) => await Task.Run(() => RefreshStatus());
                _statusTimer.Start();
                RefreshFolders();
                RefreshFileLogs();
                RefreshSysLogs();
                RefreshStatus();
                LoadConnectionConfig();
                LoadSmtpConfig();
                try
                {
                    var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    if (reg?.GetValue("RPMC Backup") == null)
                        reg?.SetValue("RPMC Backup", $"\"{Environment.ProcessPath}\" --minimized");
                    reg?.Dispose();
                }
                catch { }
                return;
            }
            _closingToTray = false;
            Close();
            return;
        }
        var cmdArgs = Environment.GetCommandLineArgs();
        if (cmdArgs.Length > 1 && (cmdArgs[1] == "--start-service" || cmdArgs[1] == "--install-service"))
        {
        }
        else
        {
            try
            {
                var psi = new ProcessStartInfo("sc", "start rpmc-backup-service")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }
        }
        _statusTimer = new System.Windows.Forms.Timer { Interval = Constants.TrayPollIntervalMs };
        _statusTimer.Tick += async (s, ev) => await Task.Run(() => RefreshStatus());
        _statusTimer.Start();
        RefreshFolders();
        RefreshFileLogs();
        RefreshSysLogs();
        RefreshStatus();
        LoadConnectionConfig();
        LoadSmtpConfig();
    }

    private void LoadConnectionConfig()
    {
        var cfg = LoadConfig();
        if (cfg == null) return;
        _txtConnEndpoint.Text = cfg.MinioEndpoint;
        _txtConnAccessKey.Text = cfg.MinioAccessKey;
        _txtConnSecretKey.Text = cfg.MinioSecretKey;
        _chkConnSsl.Checked = cfg.MinioUseSsl;
        _cmbConnBucket.Text = cfg.BucketName;
        _txtConnMachineName.Text = cfg.MachineName;
        _txtConnRegion.Text = cfg.S3Region;
        try
        {
            var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            _chkAutoStart.Checked = key?.GetValue("RPMC Backup") != null;
            key?.Dispose();
        }
        catch { }
    }

    private void LoadSmtpConfig()
    {
        var cfg = LoadConfig();
        if (cfg == null) return;
        _txtAdminEmailConfig.Text = cfg.AdminEmail;
        _txtSmtpHost.Text = cfg.SmtpHost;
        _txtSmtpPort.Text = cfg.SmtpPort.ToString();
        _txtSmtpUser.Text = cfg.SmtpUser;
        _txtSmtpPass.Text = cfg.SmtpPass;
        _txtSmtpFrom.Text = cfg.SmtpFrom;
        _chkSmtpSsl.Checked = cfg.SmtpUseSsl;
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closingToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
            _trayIcon.ShowBalloonTip(2000, "RPMC Backup", "La aplicación continúa ejecutándose en segundo plano.", ToolTipIcon.Info);
        }
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private bool CheckConfigExists()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir, Constants.ConfigFileName);
        return File.Exists(path);
    }

    private AppConfig? LoadConfig()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir, Constants.ConfigFileName);
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

    private void SaveConfig(AppConfig config)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir, Constants.ConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(config);
        var data = new byte[] { 0x52, 0x50, 0x4D, 0x43 }.Concat(System.Text.Encoding.UTF8.GetBytes(json)).ToArray();
        File.WriteAllBytes(path, data);
    }

    private void RefreshStatus()
    {
        ServiceStateInfo? state = null;
        try
        {
            using var pipe = new NamedPipeClientStream(".", Constants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            var request = new IpcRequest { Command = Constants.CmdGetStatus };
            writer.WriteLine(JsonSerializer.Serialize(request));
            using var reader = new StreamReader(pipe);
            var responseJson = reader.ReadLine();
            if (responseJson != null)
            {
                var response = JsonSerializer.Deserialize<IpcResponse>(responseJson);
                if (response?.Success == true && response.State != null)
                    state = response.State;
            }
        }
        catch { }

        if (state == null)
            state = new ServiceStateInfo { Status = ServiceStatus.Error, Errors24h = -1 };

        var captured = state;
        if (IsHandleCreated && !Disposing)
            Invoke(() => UpdateStatusUI(captured));
    }

    private void DrawPlayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillPolygon(new SolidBrush(Color.Green), new[] { new PointF(3, 2), new PointF(3, 14), new PointF(14, 8) });
        var hIcon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hIcon);
        _trayIcon.Icon = (Icon)temp.Clone();
    }

    private void DrawVerifyingIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillEllipse(new SolidBrush(Color.DodgerBlue), 1, 1, 14, 14);
        var hIcon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hIcon);
        _trayIcon.Icon = (Icon)temp.Clone();
    }
    private string _lastDataError = string.Empty;
    private string _lastConnectionError = string.Empty;
    private bool _lastDegraded;

    private void UpdateStatusUI(ServiceStateInfo state)
    {
        var color = state.IsVerifying ? Color.DodgerBlue : state.Status switch
        {
            ServiceStatus.Running => Color.Green,
            ServiceStatus.Paused or ServiceStatus.Degraded => Color.Orange,
            ServiceStatus.Error or ServiceStatus.Stopped => Color.Red,
            _ => Color.Gray
        };
        _statusIndicator.BackColor = color;

        if (state.IsVerifying)
            DrawVerifyingIcon();
        else if (state.IsSyncing)
            DrawPlayIcon();
        else
            SetTrayIcon(state.Status);

        var statusText = state.IsVerifying ? "Verificando destino..." : state.Status switch
        {
            ServiceStatus.Running => state.IsSyncing ? "Sincronizando" : "Ejecutando",
            ServiceStatus.Paused => "Pausado",
            ServiceStatus.Degraded => "Atención",
            ServiceStatus.Error => "Error",
            ServiceStatus.Stopped => "Detenido",
            _ => "Desconocido"
        };
        _lblServiceStatus.Text = statusText;
        _lblLastSync.Text = !string.IsNullOrEmpty(state.LastSyncTime) ? $"Última sincronización: {state.LastSyncTime}" : "Sin sincronizaciones";
        _lblErrors.Text = state.Errors24h >= 0 ? $"Errores (24h): {state.Errors24h}" : "Servicio no disponible";
        _lblPending.Text = $"Archivos encolados: {state.PendingFiles} | Total: {FormatBytes(state.TotalBytesUploaded)} ({state.TotalFilesUploaded} archivos)";

        _btnStop.Enabled = state.Status == ServiceStatus.Running && !state.IsVerifying;
        _btnPause.Enabled = state.Status == ServiceStatus.Running && !state.IsVerifying;
        _btnResume.Enabled = state.Status is ServiceStatus.Stopped or ServiceStatus.Paused or ServiceStatus.Degraded;
        _trayToggleItem.Text = state.Status is ServiceStatus.Stopped or ServiceStatus.Paused ? "Iniciar servicio" : "Detener servicio";

        _trayIcon.Text = state.IsVerifying ? "RPMC Backup - Verificando destino..." : state.Status switch
        {
            ServiceStatus.Running => state.IsSyncing ? "RPMC Backup - Sincronizando" : "RPMC Backup - Listo",
            ServiceStatus.Paused => "RPMC Backup - Pausado",
            ServiceStatus.Degraded => "RPMC Backup - Requiere atención",
            ServiceStatus.Error => "RPMC Backup - Error",
            ServiceStatus.Stopped => "RPMC Backup - Detenido",
            _ => "RPMC Backup"
        };


        UpdateFoldersProgress(state);

        if (!string.IsNullOrEmpty(state.DataError) && state.DataError != _lastDataError)
        {
            _lastDataError = state.DataError;
            var cfg = LoadConfig();
            var machine = cfg?.MachineName ?? Environment.MachineName;
            var user = cfg?.MachineUserName ?? Environment.UserName;
            var dirs = string.Join("\n- ", state.DataError.Split(new[] { ", " }, StringSplitOptions.None));
            var body = $"Equipo: {machine}\nUsuario: {user}\n\nEl sistema ha detectado que los siguientes orígenes de datos no están accesibles:\n\n- {dirs}";
            MessageBox.Show(body, "RPMC Backup - Error crítico", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendAlertEmail($"RPMC Backup - Error {machine}/{user}", body);
        }
        if (string.IsNullOrEmpty(state.DataError))
            _lastDataError = string.Empty;

        if (!string.IsNullOrEmpty(state.ConnectionError) && state.ConnectionError != _lastConnectionError)
        {
            _lastConnectionError = state.ConnectionError;
            var cfg = LoadConfig();
            var machine = cfg?.MachineName ?? Environment.MachineName;
            var user = cfg?.MachineUserName ?? Environment.UserName;
            var body = $"Equipo: {machine}\nUsuario: {user}\n\nEl sistema no puede conectar con el servidor S3 en {state.ConnectionError}.\nVerifique que el servidor esté encendido y accesible en la red.";
            MessageBox.Show(body, "RPMC Backup - Error crítico", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SendAlertEmail($"RPMC Backup - Error {machine}/{user}", body);
        }
        if (string.IsNullOrEmpty(state.ConnectionError))
            _lastConnectionError = string.Empty;

        if (state.Status == ServiceStatus.Degraded && !_lastDegraded)
        {
            _lastDegraded = true;
            var cfg = LoadConfig();
            var machine = cfg?.MachineName ?? Environment.MachineName;
            var user = cfg?.MachineUserName ?? Environment.UserName;
            string ip;
            try
            {
                ip = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                        && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                        && !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
                        && !n.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                        && !n.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .FirstOrDefault(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !System.Net.IPAddress.IsLoopback(u.Address)
                        && u.Address.GetAddressBytes()[0] != 169)
                    ?.Address.ToString() ?? "N/A";
            }
            catch { ip = "N/A"; }
            var body = $"Equipo: {machine}\nUsuario: {user}\nIP: {ip}\n\nEl servicio de respaldo ha entrado en estado de Atención. Revise los logs del sistema para más detalles.";
            SendAlertEmail($"RPMC Backup - Atención {machine}/{user}", body);
        }
        if (state.Status != ServiceStatus.Degraded)
            _lastDegraded = false;

        if (state.IsSyncing)
        {
            DrawPlayIcon();
        }
        else
        {
            SetTrayIcon(state.Status);
        }
    }

    private void RefreshFolders()
    {
        _foldersList.Items.Clear();
        var cfg = LoadConfig();
        if (cfg != null)
        {
            foreach (var f in cfg.Folders)
            {
                var item = new ListViewItem(f.Path);
                item.SubItems.Add(f.Recursive ? "Sí" : "No");
                _foldersList.Items.Add(item);
            }
            _foldersList.Invalidate();
        }
    }

    private void UpdateFoldersProgress(ServiceStateInfo state)
    {
        if (_foldersProgressPanel == null) return;
        if (state == null || state.FoldersProgress == null || state.FoldersProgress.Count == 0)
        {
            _foldersProgressPanel.Controls.Clear();
            return;
        }

        var seen = new HashSet<string>();
        var barWidth = _foldersProgressPanel.ClientSize.Width - 20;

        foreach (var fp in state.FoldersProgress)
        {
            var folderName = Path.GetFileName(fp.Folder.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(folderName)) folderName = fp.Folder;
            var pct = fp.Total > 0 ? Math.Min(100 * fp.Completed / fp.Total, 100) : 100;
            seen.Add(fp.Folder);

            // Find existing panel for this folder
            Panel? existingPnl = null;
            foreach (Control c in _foldersProgressPanel.Controls)
            {
                if (c is Panel p && p.Tag?.ToString() == fp.Folder)
                { existingPnl = p; break; }
            }

            if (existingPnl != null)
            {
                var lbl = existingPnl.Controls[0] as Label;
                var bar = existingPnl.Controls[1] as ProgressBar;
                if (lbl != null) lbl.Text = $"{folderName}: {fp.Completed}/{fp.Total} ({pct}%)";
                if (bar != null) bar.Value = pct;
            }
            else
            {
                var pnl = new Panel { Width = barWidth, Height = 28, Margin = new Padding(0, 0, 4, 2), Tag = fp.Folder };
                var lbl = new Label { Text = $"{folderName}: {fp.Completed}/{fp.Total} ({pct}%)", Location = new Point(0, 0), AutoSize = true };
                var bar = new ProgressBar { Value = pct, Location = new Point(0, 16), Width = barWidth, Height = 12, Style = ProgressBarStyle.Continuous };
                pnl.Controls.Add(lbl);
                pnl.Controls.Add(bar);
                _foldersProgressPanel.Controls.Add(pnl);
            }
        }

        // Remove panels for folders no longer in state
        for (int i = _foldersProgressPanel.Controls.Count - 1; i >= 0; i--)
        {
            if (_foldersProgressPanel.Controls[i] is Panel p && p.Tag != null && !seen.Contains(p.Tag.ToString()!))
                _foldersProgressPanel.Controls.RemoveAt(i);
        }
    }

    private void RefreshFileLogs()
    {
        _logGrid.Rows.Clear();
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir, Constants.LogsDbName);
        if (!File.Exists(dbPath)) return;
        var excluded = LoadConfig()?.ExcludedFiles ?? new();
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            var sql = "SELECT timestamp, level, folder, filename, message FROM sync_logs WHERE 1=1";
            var levelIdx = _cmbLogLevel.SelectedIndex;
            if (levelIdx > 0) sql += $" AND level = {levelIdx - 1}";
            if (!string.IsNullOrEmpty(_txtSearch.Text))
                sql += $" AND (filename LIKE '%{_txtSearch.Text.Replace("'", "''")}%' OR message LIKE '%{_txtSearch.Text.Replace("'", "''")}%')";
            sql += " ORDER BY id DESC LIMIT 500";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var ts = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var level = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var folder = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var file = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var msg = reader.IsDBNull(4) ? "" : reader.GetString(4);
                var levelStr = level switch { 0 => "I", 1 => "W", 2 => "E", 3 => "F", _ => "?" };
                if (ts.Length > 19) ts = ts.Substring(0, 19).Replace("T", " ");
                _logGrid.Rows.Add(ts, levelStr, Path.GetFileName(folder.TrimEnd('\\')), file, msg);
                var row = _logGrid.Rows[_logGrid.Rows.Count - 1];
                row.DefaultCellStyle.ForeColor = level >= 2 ? Color.Red : level == 1 ? Color.Orange : Color.Black;
                if (excluded.Contains(file, StringComparer.OrdinalIgnoreCase))
                    row.DefaultCellStyle.BackColor = Color.Yellow;
            }
        }
        catch { }
    }

    private void RefreshSysLogs(string? levelFilter = null)
    {
        if (_sysLogGrid == null) return;
        _sysLogGrid.Rows.Clear();
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir, Constants.LogsDbName);
        if (!File.Exists(dbPath)) return;
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            var sql = "SELECT timestamp, level, source, message FROM system_logs WHERE 1=1";
            if (!string.IsNullOrEmpty(levelFilter))
                sql += $" AND level = {levelFilter}";
            sql += " ORDER BY id DESC LIMIT 500";
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var ts = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var level = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var src = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var msg = reader.IsDBNull(3) ? "" : reader.GetString(3);
                var levelStr = level switch { 0 => "I", 1 => "W", 2 => "E", _ => "?" };
                if (ts.Length > 19) ts = ts.Substring(0, 19).Replace("T", " ");
                _sysLogGrid.Rows.Add(ts, levelStr, src, msg);
                var row = _sysLogGrid.Rows[_sysLogGrid.Rows.Count - 1];
                row.DefaultCellStyle.ForeColor = level >= 2 ? Color.Red : level == 1 ? Color.Orange : Color.Black;
            }
        }
        catch { }
    }

    private void ShowLogDetail()
    {
        if (_logGrid.SelectedRows.Count == 0) { _txtLogDetail.Clear(); return; }
        var r = _logGrid.SelectedRows[0];
        _txtLogDetail.Clear();
        _txtLogDetail.AppendText($"{r.Cells[1].Value} - {r.Cells[0].Value}\n");
        _txtLogDetail.AppendText($"Archivo: {r.Cells[3].Value}\n");
        _txtLogDetail.AppendText($"Carpeta: {r.Cells[2].Value}\n");
        _txtLogDetail.AppendText($"Mensaje: {r.Cells[4].Value}\n");
    }

    private void SendIpc(string command, string payload = "")
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Constants.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(Constants.PipeConnectTimeoutMs);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new IpcRequest { Command = command, Payload = payload }));
        }
        catch { }
    }

    private void ChangePassword()
    {
        var old = _txtOldPassword.Text;
        var newPwd = _txtNewPassword.Text;
        var confirm = _txtConfirmPassword.Text;

        if (string.IsNullOrEmpty(old) || string.IsNullOrEmpty(newPwd) || string.IsNullOrEmpty(confirm))
        {
            MessageBox.Show("Todos los campos son obligatorios.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (newPwd != confirm)
        {
            MessageBox.Show("Las claves nuevas no coinciden.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (newPwd.Length < 6)
        {
            MessageBox.Show("La clave debe tener al menos 6 caracteres.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var cfg = LoadConfig();
        if (cfg == null)
        {
            MessageBox.Show("No hay Configuración. Ejecute la Configuración inicial.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!BCrypt.Net.BCrypt.Verify(old, cfg.AdminHash))
        {
            MessageBox.Show("Clave actual incorrecta.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        cfg.AdminHash = BCrypt.Net.BCrypt.HashPassword(newPwd);
        SaveConfig(cfg);
        MessageBox.Show("Clave cambiada correctamente.", "RPMC Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _txtOldPassword.Clear();
        _txtNewPassword.Clear();
        _txtConfirmPassword.Clear();
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
}




