using RPMC_Backup.Shared;

namespace RPMC_Backup.Config;

public static class StatusHelper
{
    private static Icon _green, _blue, _play, _red, _gray;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        using var bmp16 = new Bitmap(16, 16);
        using var g16 = Graphics.FromImage(bmp16);
        g16.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        _green = MakeCircle(Color.Green);
        _blue = MakeCircle(Color.DodgerBlue);
        _red = MakeCircle(Color.Red);
        _gray = MakeCircle(Color.Gray);
        _play = MakePlayIcon();
    }

    private static Icon MakeCircle(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillEllipse(new SolidBrush(color), 1, 1, 14, 14);
        var hIcon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hIcon);
        return (Icon)temp.Clone();
    }

    private static Icon MakePlayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillPolygon(new SolidBrush(Color.Green), new[] { new PointF(3, 2), new PointF(3, 14), new PointF(14, 8) });
        var hIcon = bmp.GetHicon();
        using var temp = Icon.FromHandle(hIcon);
        return (Icon)temp.Clone();
    }

    public static StatusInfo Resolve(ServiceStateInfo state)
    {
        if (state.IsVerifying)
            return new StatusInfo(_blue, "RPMC Backup - Verificando destino...", "Verificando destino...", Color.DodgerBlue);

        if (state.IsSyncing)
            return new StatusInfo(_play, "RPMC Backup - Sincronizando", "Sincronizando", Color.Green);

        switch (state.Status)
        {
            case ServiceStatus.Running:
                return new StatusInfo(_green, "RPMC Backup - Listo", "Listo", Color.Green);
            case ServiceStatus.Error:
                return new StatusInfo(_red, "RPMC Backup - Error", "Error", Color.Red);
            case ServiceStatus.Stopped:
                return new StatusInfo(_red, "RPMC Backup - Detenido", "Detenido", Color.Red);
            default:
                return new StatusInfo(_gray, "RPMC Backup - Servicio no disponible", "Desconocido", Color.Gray);
        }
    }

    public static Icon IconGreen => _green;
    public static Icon IconBlue => _blue;
    public static Icon IconPlay => _play;
    public static Icon IconRed => _red;
    public static Icon IconGray => _gray;
}

public class StatusInfo
{
    public Icon Icon { get; }
    public string TrayText { get; }
    public string LabelText { get; }
    public Color FormColor { get; }

    public StatusInfo(Icon icon, string trayText, string labelText, Color formColor)
    {
        Icon = icon;
        TrayText = trayText;
        LabelText = labelText;
        FormColor = formColor;
    }
}
