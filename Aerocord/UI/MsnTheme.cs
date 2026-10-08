using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Aerocord.Core;

namespace Aerocord.UI
{
    public static class MsnTheme
    {
        public static readonly Color HeaderTop = Color.FromArgb(0x6E, 0xA9, 0xEE);
        public static readonly Color HeaderBottom = Color.FromArgb(0x0B, 0x3D, 0x91);
        public static readonly Color WindowBackground = Color.FromArgb(0xEF, 0xF5, 0xFD);
        public static readonly Color PanelBackground = Color.White;
        public static readonly Color TileHover = Color.FromArgb(0xD9, 0xEA, 0xFC);
        public static readonly Color TileSelected = Color.FromArgb(0xBB, 0xD8, 0xF7);
        public static readonly Color GroupHeaderBack = Color.FromArgb(0xCE, 0xE4, 0xFA);
        public static readonly Color BorderBlue = Color.FromArgb(0x3B, 0x6E, 0xA5);
        public static readonly Color BubbleOther = Color.FromArgb(0xE9, 0xF1, 0xFB);
        public static readonly Color BubbleSelf = Color.FromArgb(0xD3, 0xEA, 0xD3);
        public static readonly Color LinkBlue = Color.FromArgb(0x0B, 0x3D, 0x91);

        public static readonly Font FontNormal = new Font("Segoe UI", 9f, FontStyle.Regular);
        public static readonly Font FontBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font FontSmall = new Font("Segoe UI", 8f, FontStyle.Regular);
        public static readonly Font FontGroupHeader = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font FontTitle = new Font("Segoe UI", 11f, FontStyle.Bold);

        public static void DrawVerticalGradient(Graphics g, Rectangle rect, Color top, Color bottom)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (var brush = new LinearGradientBrush(rect, top, bottom, LinearGradientMode.Vertical))
            {
                g.FillRectangle(brush, rect);
            }
        }

        public static Color StatusColor(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.Online: return Color.FromArgb(0x3F, 0xB9, 0x50);
                case PresenceStatus.Idle: return Color.FromArgb(0xE3, 0xB4, 0x1E);
                case PresenceStatus.DoNotDisturb: return Color.FromArgb(0xE0, 0x3B, 0x3B);
                case PresenceStatus.Invisible: return Color.FromArgb(0xA0, 0xA8, 0xB0);
                default: return Color.FromArgb(0xA0, 0xA8, 0xB0);
            }
        }

        public static string StatusLabel(PresenceStatus status)
        {
            switch (status)
            {
                case PresenceStatus.Online: return "Online";
                case PresenceStatus.Idle: return "Away";
                case PresenceStatus.DoNotDisturb: return "Busy";
                case PresenceStatus.Invisible: return "Appear Offline";
                default: return "Offline";
            }
        }

        public static void DrawStatusDot(Graphics g, Rectangle bounds, PresenceStatus status)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(StatusColor(status)))
            using (var pen = new Pen(Color.FromArgb(80, Color.Black), 1))
            {
                g.FillEllipse(brush, bounds);
                g.DrawEllipse(pen, bounds);
            }
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void StyleWindow(Form f)
        {
            f.BackColor = WindowBackground;
            f.Font = FontNormal;
        }

        public static void StyleFlatButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = BorderBlue;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = TileHover;
            b.BackColor = Color.FromArgb(0xDF, 0xEC, 0xFB);
            b.Font = FontBold;
            b.Cursor = Cursors.Hand;
        }
    }
}
