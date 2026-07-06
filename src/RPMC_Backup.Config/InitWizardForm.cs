using System.Diagnostics;
using System.IO;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using RPMC_Backup.Shared;

namespace RPMC_Backup.Config;

public class InitWizardForm : Form
{
    private int _step = 0;
    private Panel _contentPanel;
    private Button _btnBack, _btnNext, _btnCancel;
    private Label _lblTitle, _lblDesc;

    private TextBox _txtAdminPassword, _txtConfirmPassword, _txtAdminEmail;
    private TextBox _txtEndpoint, _txtAccessKey, _txtSecretKey;
    private CheckBox _chkUseSsl;
    private Button _btnTestConnection;
    private Label _lblTestResult;
    private TextBox _txtMachineName, _txtBucketName, _txtMachineUser;
    private CheckedListBox _chkFolders;
    private Button _btnBrowseFolder;
    private TreeView _treeFolders;
    private TextBox _txtFolderPath;
    private Label _lblSummary;

    private string? _testedEndpoint;
    private List<string> _availableBuckets = new();

    public InitWizardForm()
    {
        Text = "RPMC Backup - Configuración Inicial";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;

        _lblTitle = new Label { Location = new Point(20, 15), AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) };
        _lblDesc = new Label { Location = new Point(20, 45), AutoSize = true, ForeColor = Color.Gray };
        _contentPanel = new Panel { Location = new Point(15, 75), Size = new Size(555, 330) };

        _btnBack = new Button { Location = new Point(390, 420), Size = new Size(80, 30), Text = "Atrás", Enabled = false };
        _btnNext = new Button { Location = new Point(480, 420), Size = new Size(90, 30), Text = "Siguiente" };
        _btnCancel = new Button { Location = new Point(295, 420), Size = new Size(80, 30), Text = "Cancelar" };

        _btnBack.Click += (s, e) => { _step--; RenderStep(); };
        _btnNext.Click += (s, e) => NextStep();
        _btnCancel.Click += (s, e) => { if (MessageBox.Show("¿Cancelar configuración?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) { Close(); } };

        Controls.AddRange(new Control[] { _lblTitle, _lblDesc, _contentPanel, _btnBack, _btnNext, _btnCancel });
        RenderStep();
    }

    private void RenderStep()
    {
        _contentPanel.Controls.Clear();
        _btnBack.Enabled = _step > 0;
        _btnNext.Text = _step == 4 ? "Finalizar" : "Siguiente";

        switch (_step)
        {
            case 0: RenderStep1(); break;
            case 1: RenderStep2(); break;
            case 2: RenderStep3(); break;
            case 3: RenderStep4(); break;
            case 4: RenderStep5(); break;
        }
    }

    private void RenderStep1()
    {
        _lblTitle.Text = "Paso 1: Clave de Administrador";
        _lblDesc.Text = "Configure la clave de acceso y el correo para recuperación.";

        var lblPwd = new Label { Text = "Clave de administrador:", Location = new Point(20, 20), AutoSize = true };
        var lblConfirm = new Label { Text = "Confirmar clave:", Location = new Point(20, 55), AutoSize = true };
        var lblEmail = new Label { Text = "Correo electrónico:", Location = new Point(20, 90), AutoSize = true };
        _txtAdminPassword = new TextBox { Location = new Point(180, 17), Width = 250, UseSystemPasswordChar = true };
        _txtConfirmPassword = new TextBox { Location = new Point(180, 52), Width = 250, UseSystemPasswordChar = true };
        _txtAdminEmail = new TextBox { Location = new Point(180, 87), Width = 250, Text = "gcarranco@rpcayambe.gob.ec" };
        var lblInfo = new Label { Text = "El correo se usará para recuperar la clave vía PIN.", Location = new Point(20, 120), AutoSize = true, ForeColor = Color.Gray };
        _contentPanel.Controls.AddRange(new Control[] { lblPwd, lblConfirm, lblEmail, _txtAdminPassword, _txtConfirmPassword, _txtAdminEmail, lblInfo });
    }

    private void RenderStep2()
    {
        _lblTitle.Text = "Paso 2: Conexión MinIO";
        _lblDesc.Text = "Configure la conexión al servidor MinIO AIStor.";

        var lblEp = new Label { Text = "Endpoint:", Location = new Point(20, 20), AutoSize = true };
        var lblAk = new Label { Text = "Access Key:", Location = new Point(20, 55), AutoSize = true };
        var lblSk = new Label { Text = "Secret Key:", Location = new Point(20, 90), AutoSize = true };
        _txtEndpoint = new TextBox { Location = new Point(120, 17), Width = 300, Text = "192.168.1.201:9000" };
        _txtAccessKey = new TextBox { Location = new Point(120, 52), Width = 300, Text = "sync" };
        _txtSecretKey = new TextBox { Location = new Point(120, 87), Width = 300, UseSystemPasswordChar = true, Text = "Rdp0101gcar1" };
        _chkUseSsl = new CheckBox { Text = "Usar SSL (HTTPS)", Location = new Point(120, 120), AutoSize = true };
        _btnTestConnection = new Button { Location = new Point(120, 150), Size = new Size(140, 30), Text = "Probar Conexión" };
        _lblTestResult = new Label { Location = new Point(270, 155), AutoSize = true };

        _btnTestConnection.Click += async (s, e) =>
        {
            _btnTestConnection.Enabled = false;
            _lblTestResult.Text = "Probando...";
            _lblTestResult.ForeColor = Color.Blue;
            try
            {
                var client = new MinioClient()
                    .WithEndpoint(_txtEndpoint.Text)
                    .WithCredentials(_txtAccessKey.Text, _txtSecretKey.Text);
                if (!_chkUseSsl.Checked) client.WithSSL(false);
                client.Build();
                var buckets = await client.ListBucketsAsync();
                _availableBuckets = buckets.Buckets.Select(b => b.Name).ToList();
                _testedEndpoint = _txtEndpoint.Text;
                _lblTestResult.Text = "✅ Conexión exitosa!";
                _lblTestResult.ForeColor = Color.Green;
            }
            catch (Exception ex)
            {
                _lblTestResult.Text = $"❌ Error: {ex.Message}";
                _lblTestResult.ForeColor = Color.Red;
            }
            _btnTestConnection.Enabled = true;
        };

        _contentPanel.Controls.AddRange(new Control[] { lblEp, lblAk, lblSk, _txtEndpoint, _txtAccessKey, _txtSecretKey, _chkUseSsl, _btnTestConnection, _lblTestResult });
    }

    private void RenderStep3()
    {
        _lblTitle.Text = "Paso 3: Bucket";
        _lblDesc.Text = "Seleccione o cree un bucket para los respaldos de este equipo.";

        _txtMachineName = new TextBox { Location = new Point(150, 20), Width = 280, Text = Environment.MachineName };
        _txtMachineUser = new TextBox { Location = new Point(150, 55), Width = 280, Text = Environment.UserName };
        _txtBucketName = new TextBox { Location = new Point(150, 90), Width = 280, Text = $"rpmc-{Environment.MachineName.ToLowerInvariant()}" };
        var lblAuto = new Label { Text = "Nombre del equipo:", Location = new Point(20, 23), AutoSize = true };
        var lblUser = new Label { Text = "Usuario del equipo:", Location = new Point(20, 58), AutoSize = true };
        var lblBucket = new Label { Text = "Bucket:", Location = new Point(20, 93), AutoSize = true };
        var lblInfo = new Label { Text = "Se creará automáticamente con versioning habilitado.", Location = new Point(20, 120), AutoSize = true, ForeColor = Color.Gray };
        _contentPanel.Controls.AddRange(new Control[] { lblAuto, lblUser, lblBucket, _txtMachineName, _txtMachineUser, _txtBucketName, lblInfo });

        if (_availableBuckets.Count > 0)
        {
            var lblExisting = new Label { Text = "Buckets existentes:", Location = new Point(20, 120), AutoSize = true };
            var listBox = new ListBox { Location = new Point(20, 145), Size = new Size(500, 120) };
            foreach (var b in _availableBuckets) listBox.Items.Add(b);
            listBox.SelectedIndexChanged += (s, e) => {
                if (listBox.SelectedItem != null)
                    _txtBucketName.Text = listBox.SelectedItem.ToString()!;
            };
            _contentPanel.Controls.AddRange(new Control[] { lblExisting, listBox });
        }
    }

    private void RenderStep4()
    {
        _lblTitle.Text = "Paso 4: Carpetas a respaldar";
        _lblDesc.Text = "Seleccione las carpetas que desea sincronizar con MinIO.";

        _chkFolders = new CheckedListBox { Location = new Point(280, 20), Size = new Size(260, 230), CheckOnClick = true };
        var lblTree = new Label { Text = "Navegar carpetas:", Location = new Point(20, 5), AutoSize = true };
        var lblSelected = new Label { Text = "Carpetas seleccionadas:", Location = new Point(280, 5), AutoSize = true };
        _treeFolders = new TreeView { Location = new Point(20, 22), Size = new Size(250, 230) };
        _treeFolders.BeforeExpand += (s, e) => PopulateSubfolders(e.Node);
        _treeFolders.AfterSelect += (s, e) => _txtFolderPath.Text = e.Node.Tag?.ToString() ?? e.Node.FullPath;
        LoadDrives();

        _txtFolderPath = new TextBox { Location = new Point(20, 260), Width = 340 };
        _btnBrowseFolder = new Button { Location = new Point(370, 258), Size = new Size(100, 28), Text = "Agregar" };
        var _btnRemoveFolder = new Button { Location = new Point(480, 258), Size = new Size(60, 28), Text = "Quitar" };
        _txtFolderPath.TextChanged += (s, e) => {
            _btnBrowseFolder.Enabled = Directory.Exists(_txtFolderPath.Text.Trim());
        };
        _btnBrowseFolder.Enabled = false;

        _btnBrowseFolder.Click += (s, e) => {
            var path = _txtFolderPath.Text.Trim();
            if (Directory.Exists(path) && !_chkFolders.Items.Contains(path))
                _chkFolders.Items.Add(path, true);
            _txtFolderPath.Clear();
        };
        _btnRemoveFolder.Click += (s, e) => {
            if (_chkFolders.SelectedItem != null)
                _chkFolders.Items.Remove(_chkFolders.SelectedItem);
        };

        _contentPanel.Controls.AddRange(new Control[] { lblTree, lblSelected, _treeFolders, _chkFolders, _txtFolderPath, _btnBrowseFolder, _btnRemoveFolder });
    }

    private void LoadDrives()
    {
        _treeFolders.Nodes.Clear();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var node = new TreeNode(drive.Name) { Tag = drive.Name };
                node.Nodes.Add(new TreeNode("*")); // dummy for expand
                _treeFolders.Nodes.Add(node);
            }
        }
        catch { }
    }

    private void PopulateSubfolders(TreeNode parentNode)
    {
        if (parentNode.Nodes.Count != 1 || parentNode.Nodes[0].Text != "*") return;
        parentNode.Nodes.Clear();
        try
        {
            var path = parentNode.Tag?.ToString() ?? parentNode.FullPath;
            foreach (var dir in Directory.GetDirectories(path))
            {
                try
                {
                    var di = new DirectoryInfo(dir);
                    var node = new TreeNode(di.Name) { Tag = dir };
                    try
                    {
                        if (Directory.GetDirectories(dir).Length > 0)
                            node.Nodes.Add(new TreeNode("*"));
                    }
                    catch { }
                    parentNode.Nodes.Add(node);
                }
                catch { }
            }
        }
        catch { }
    }

    private void RenderStep5()
    {
        _lblTitle.Text = "Paso 5: Resumen";
        _lblDesc.Text = "Revise la configuración antes de finalizar.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Resumen de configuración:");
        sb.AppendLine();
        sb.AppendLine($"MinIO: {_txtEndpoint.Text}");
        sb.AppendLine($"Access Key: {_txtAccessKey.Text}");
        sb.AppendLine($"Bucket: {_txtBucketName.Text}");
        sb.AppendLine($"Equipo: {_txtMachineName.Text}");
        sb.AppendLine($"Usuario: {_txtMachineUser.Text}");

        _lblSummary = new Label { Text = sb.ToString(), Location = new Point(20, 10), AutoSize = true, Parent = _contentPanel };
        var yFolders = _lblSummary.Bottom + 10;
        var sbFolders = new System.Text.StringBuilder();
        sbFolders.AppendLine("Carpetas a respaldar:");
        foreach (var item in _chkFolders.CheckedItems)
            sbFolders.AppendLine($"  - {item}");
        var txtFolders = new TextBox { Text = sbFolders.ToString(), Location = new Point(20, yFolders), Width = 440, Height = 110, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
        var lblWarning = new Label { Text = "Los archivos eliminados en origen NO se eliminan en bucket.", Location = new Point(20, yFolders + 115), AutoSize = true, ForeColor = Color.FromArgb(180, 150, 0) };
        _contentPanel.Controls.AddRange(new Control[] { txtFolders, lblWarning });
    }

    private async void NextStep()
    {
        switch (_step)
        {
            case 0:
                if (_txtAdminPassword.Text.Length < 6)
                {
                    MessageBox.Show("La clave debe tener al menos 6 caracteres.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (_txtAdminPassword.Text != _txtConfirmPassword.Text)
                {
                    MessageBox.Show("Las claves no coinciden.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _step++;
                RenderStep();
                break;

            case 1:
                if (string.IsNullOrEmpty(_txtEndpoint.Text) || string.IsNullOrEmpty(_txtAccessKey.Text) || string.IsNullOrEmpty(_txtSecretKey.Text))
                {
                    MessageBox.Show("Complete todos los campos de conexión.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _step++;
                RenderStep();
                break;

            case 2:
                if (string.IsNullOrEmpty(_txtMachineName.Text) || string.IsNullOrEmpty(_txtMachineUser.Text))
                {
                    MessageBox.Show("Complete el nombre del equipo y usuario.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.IsNullOrEmpty(_txtBucketName.Text))
                {
                    MessageBox.Show("Ingrese un nombre de bucket.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _step++;
                RenderStep();
                break;

            case 3:
                if (_chkFolders.CheckedItems.Count == 0)
                {
                    MessageBox.Show("Seleccione al menos una carpeta.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _step++;
                RenderStep();
                break;

            case 4:
                _btnNext.Enabled = false;
                try
                {
                    var config = new AppConfig
                    {
                        AdminHash = BCrypt.Net.BCrypt.HashPassword(_txtAdminPassword.Text),
                        AdminEmail = _txtAdminEmail.Text,
                        MinioEndpoint = _txtEndpoint.Text,
                        MinioAccessKey = _txtAccessKey.Text,
                        MinioSecretKey = _txtSecretKey.Text,
                        MinioUseSsl = _chkUseSsl.Checked,
                        BucketName = _txtBucketName.Text,
                        MachineName = _txtMachineName.Text,
                        MachineUserName = _txtMachineUser.Text,
                        SmtpHost = "smtp.gmail.com",
                        SmtpPort = 587,
                        SmtpUser = "mailingrpmcc@gmail.com",
                        SmtpPass = "vtwlvgserdahpfrx",
                        SmtpUseSsl = true,
                        SmtpFrom = "mailingrpmcc@gmail.com",
                        Folders = new List<FolderConfig>()
                    };
                    foreach (var item in _chkFolders.CheckedItems)
                        config.Folders.Add(new FolderConfig { Path = item.ToString()!, Recursive = true });

                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Constants.ConfigDir);
                    Directory.CreateDirectory(dir);
                    var json = System.Text.Json.JsonSerializer.Serialize(config);
                    var data = new byte[] { 0x52, 0x50, 0x4D, 0x43 }.Concat(System.Text.Encoding.UTF8.GetBytes(json)).ToArray();
                    var configPath = Path.Combine(dir, Constants.ConfigFileName);
                    File.WriteAllBytes(configPath, data);

                    // Test MinIO connection
                    var client = new MinioClient()
                        .WithEndpoint(config.MinioEndpoint)
                        .WithCredentials(config.MinioAccessKey, config.MinioSecretKey);
                    if (!config.MinioUseSsl) client.WithSSL(false);
                    client.Build();
                    var buckets = await client.ListBucketsAsync();

                    var bucketExists = buckets.Buckets.Any(b => b.Name.Equals(config.BucketName, StringComparison.OrdinalIgnoreCase));
                    if (!bucketExists)
                    {
                        await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(config.BucketName));
                    }

                    // Start service
                    var scPsi = new ProcessStartInfo("sc", "start rpmc-backup-service")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    var scProcess = Process.Start(scPsi);
                    scProcess?.WaitForExit(10000);

                    if (scProcess?.ExitCode != 0)
                    {
                        MessageBox.Show(
                            "La configuración se guardó correctamente, pero el servicio no pudo iniciar.\n\n" +
                            "Puede intentar iniciarlo manualmente desde el Administrador de Servicios (services.msc)\n" +
                            "o reiniciar el equipo para que el servicio arranque automáticamente.\n\n" +
                            "Una vez iniciado el servicio, ejecute RPMC Backup desde el icono del escritorio.",
                            "Atención - Servicio no iniciado",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        DialogResult = DialogResult.OK;
                        Close();
                        return;
                    }

                    // Register auto-start in Registry (tray mode)
                    try
                    {
                        var reg = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                        if (reg != null)
                        {
                            reg.SetValue("RPMC Backup",
                                $"\"{Application.ExecutablePath}\" --tray");
                            reg.Dispose();
                        }
                    }
                    catch { }

                    // Launch tray
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Application.ExecutablePath,
                            Arguments = "--tray",
                            UseShellExecute = true
                        });
                    }
                    catch { }

                    MessageBox.Show("Configuración completada correctamente.\n\n" +
                        "El servicio de respaldo está funcionando. Puede acceder a la configuración\n" +
                        "desde el icono en la bandeja del sistema (system tray).",
                        "RPMC Backup",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (MinioException ex)
                {
                    MessageBox.Show(
                        "No se pudo conectar al servidor MinIO.\n\n" +
                        $"Endpoint: {_txtEndpoint.Text}\n" +
                        $"Bucket: {_txtBucketName.Text}\n" +
                        $"Error: {ex.Message}\n\n" +
                        "Verifique que:\n" +
                        "• El servidor MinIO esté encendido y accesible\n" +
                        "• Las credenciales (Access Key / Secret Key) sean correctas\n" +
                        "• La dirección IP y puerto sean los correctos",
                        "Error de conexión",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _btnNext.Enabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar configuración: {ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _btnNext.Enabled = true;
                }
                break;
        }
    }
}
