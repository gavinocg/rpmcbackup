#r "System.Drawing.Common"
using System.Drawing;
using System.Drawing.Imaging;
var bmp = new Bitmap(32, 32);
using var g = Graphics.FromImage(bmp);
g.Clear(Color.DarkBlue);
g.FillRectangle(Brushes.White, 4, 8, 24, 18);
g.DrawString("RB", new Font("Arial", 14, FontStyle.Bold), Brushes.DarkBlue, 5, 10);
var ms = new MemoryStream();
bmp.Save(ms, ImageFormat.Png);
var fs = File.Create("C:\\dev\\RPMC_Backup\\Setup\\rpmc_backup_config.ico");
fs.Write(new byte[] { 0, 0, 1, 0, 1, 0, 32, 32, 0, 0 }, 0, 10);
var len = (int)ms.Length;
fs.Write(BitConverter.GetBytes(len), 0, 4);
fs.Write(BitConverter.GetBytes(22), 0, 4);
fs.Write(ms.ToArray(), 0, len);
fs.Close();
Console.WriteLine("Icon created");
