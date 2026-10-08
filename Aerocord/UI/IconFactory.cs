using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace Aerocord.UI
{
    public static class IconFactory
    {
        private static Icon _appIcon;
        private static Icon _trayIcon;
        private static Icon _fallback;

        public static Icon AppIcon
        {
            get { return _appIcon ?? (_appIcon = LoadFromAssets("app.ico") ?? CreateFallbackIcon()); }
        }

        public static Icon TrayIcon
        {
            get { return _trayIcon ?? (_trayIcon = LoadFromAssets("tray.ico") ?? AppIcon); }
        }

        private static Icon LoadFromAssets(string fileName)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
                if (!File.Exists(path)) return null;
                byte[] bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                    return new Icon(ms);
            }
            catch
            {
                // Missing, unreadable, or not a valid .ico - fall back gracefully instead
                // of crashing the app over artwork.
                return null;
            }
        }

        private static Icon CreateFallbackIcon()
        {
            if (_fallback != null) return _fallback;

            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                    Color.FromArgb(0x6E, 0xA9, 0xEE), Color.FromArgb(0x0B, 0x3D, 0x91), LinearGradientMode.Vertical))
                {
                    g.FillEllipse(brush, 1, 1, 30, 30);
                }
                using (var pen = new Pen(Color.FromArgb(0x0B, 0x3D, 0x91), 1))
                    g.DrawEllipse(pen, 1, 1, 29, 29);
                using (var dot = new SolidBrush(Color.White))
                {
                    g.FillEllipse(dot, 9, 11, 5, 5);
                    g.FillEllipse(dot, 14, 11, 5, 5);
                    g.FillEllipse(dot, 19, 11, 5, 5);
                }

                IntPtr hIcon = bmp.GetHicon();
                _fallback = Icon.FromHandle(hIcon);
                return _fallback;
            }
        }
    }
}
