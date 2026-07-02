using System.Text.Json;
using System.Text;
using RPMC_Backup.Shared;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    Constants.ConfigDir, Constants.ConfigFileName);

if (!File.Exists(configPath))
    return 1;

AppConfig? config;
try
{
    var data = File.ReadAllBytes(configPath);
    if (data.Length < 4 || data[0] != 0x52 || data[1] != 0x50 || data[2] != 0x4D || data[3] != 0x43)
        return 1;
    var json = Encoding.UTF8.GetString(data, 4, data.Length - 4);
    config = JsonSerializer.Deserialize<AppConfig>(json);
}
catch
{
    return 1;
}

ApplicationConfiguration.Initialize();

if (config == null || string.IsNullOrEmpty(config.AdminHash))
{
    if (MessageBox.Show(
            "No se ha configurado una clave de administrador.\n" +
            "¿Desea continuar con la desinstalacion?",
            "RPMC Backup - Desinstalacion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes)
        return 0;
    return 1;
}
using var form = new Form
{
    Text = "RPMC Backup - Desinstalacion",
    Size = new Size(380, 160),
    FormBorderStyle = FormBorderStyle.FixedDialog,
    StartPosition = FormStartPosition.CenterScreen,
    MaximizeBox = false,
    MinimizeBox = false,
    ShowInTaskbar = true,
    TopMost = false
};
var lbl = new Label { Text = "Ingrese la clave de administrador\npara desinstalar:", Location = new Point(15, 15), AutoSize = true };
var txt = new TextBox { Location = new Point(15, 50), Width = 330, UseSystemPasswordChar = true };
var btnOk = new Button { Location = new Point(170, 85), Size = new Size(80, 30), Text = "Aceptar" };
var btnCancel = new Button { Location = new Point(260, 85), Size = new Size(80, 30), Text = "Cancelar" };
form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
form.AcceptButton = btnOk;
form.CancelButton = btnCancel;

btnCancel.Click += (s, e) => { form.Close(); };

bool ok = false;
btnOk.Click += (s, e) =>
{
    if (BCrypt.Net.BCrypt.Verify(txt.Text, config.AdminHash))
    {
        ok = true;
        form.Close();
    }
    else
    {
        MessageBox.Show(form, "Clave incorrecta.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        txt.Clear();
        txt.Focus();
    }
};

Application.Run(form);
return ok ? 0 : 1;
