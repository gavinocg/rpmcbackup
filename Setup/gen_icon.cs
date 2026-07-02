using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

var bmp = new Bitmap(64, 64);
using var g = Graphics.FromImage(bmp);
g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

g.FillEllipse(new SolidBrush(Color.FromArgb(0, 48, 100)), 2, 2, 60, 60);

var driveRect = new Rectangle(10, 22, 44, 28);
g.FillRectangle(new SolidBrush(Color.FromArgb(220, 220, 220)), driveRect);
g.DrawRectangle(new Pen(Color.FromArgb(100, 100, 100), 2), driveRect);

for (int y = 30; y <= 44; y += 7)
    g.DrawLine(new Pen(Color.FromArgb(180, 180, 180), 1), 14, y, 50, y);

g.FillRectangle(new SolidBrush(Color.FromArgb(60, 60, 60)), 24, 36, 16, 4);

g.FillEllipse(new SolidBrush(Color.FromArgb(0, 200, 0)), 48, 44, 6, 6);
g.FillEllipse(new SolidBrush(Color.FromArgb(100, 255, 100)), 49, 45, 4, 4);

using var pen = new Pen(Color.White, 2);
g.DrawArc(pen, 38, 5, 20, 20, 0, 360);
var midX = 48; var midY = 15;
g.DrawLine(pen, midX + 8, midY, midX + 4, midY - 4);
g.DrawLine(pen, midX + 8, midY, midX + 4, midY + 4);

var ms32 = new MemoryStream();
using (var bmp32 = new Bitmap(bmp, 32, 32)) { bmp32.Save(ms32, ImageFormat.Png); }
var ms64 = new MemoryStream();
bmp.Save(ms64, ImageFormat.Png);

var icoPath = "C:\\dev\\RPMC_Backup\\Setup\\rpmc_backup_config.ico";
using var fs = File.Create(icoPath);
var bw = new BinaryWriter(fs);
bw.Write((short)0); bw.Write((short)1); bw.Write((short)2);

var d32 = ms32.ToArray();
bw.Write((byte)32); bw.Write((byte)32); bw.Write((byte)0); bw.Write((byte)0);
bw.Write((short)1); bw.Write((short)32);
bw.Write(d32.Length); bw.Write(22 + 16);

var d64 = ms64.ToArray();
bw.Write((byte)64); bw.Write((byte)64); bw.Write((byte)0); bw.Write((byte)0);
bw.Write((short)1); bw.Write((short)32);
bw.Write(d64.Length); bw.Write(22 + 16 + d32.Length);

bw.Write(d32); bw.Write(d64);
bw.Close();
Console.WriteLine($"Icon created: {icoPath}");
