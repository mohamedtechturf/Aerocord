using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Aerocord.Core;

namespace Aerocord.UI
{
    public class GroupHeaderControl : Panel
    {
        public bool Expanded { get; private set; } = true;
        public event EventHandler ExpandedChanged;

        private readonly string _title;

        public GroupHeaderControl(string title)
        {
            _title = title;
            Height = 24;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            Click += (s, e) => Toggle();
        }

        public void Toggle()
        {
            Expanded = !Expanded;
            Invalidate();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            MsnTheme.DrawVerticalGradient(g, ClientRectangle, MsnTheme.GroupHeaderBack, Color.White);
            using (var pen = new Pen(MsnTheme.BorderBlue))
                g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

            // Arrow glyph
            var arrowRect = new Rectangle(6, Height / 2 - 4, 8, 8);
            using (var brush = new SolidBrush(MsnTheme.BorderBlue))
            {
                Point[] tri = Expanded
                    ? new[] { new Point(arrowRect.Left, arrowRect.Top), new Point(arrowRect.Right, arrowRect.Top), new Point(arrowRect.Left + arrowRect.Width / 2, arrowRect.Bottom) }
                    : new[] { new Point(arrowRect.Left, arrowRect.Top), new Point(arrowRect.Left, arrowRect.Bottom), new Point(arrowRect.Right, arrowRect.Top + arrowRect.Height / 2) };
                g.FillPolygon(brush, tri);
            }

            using (var textBrush = new SolidBrush(MsnTheme.BorderBlue))
            {
                g.DrawString(_title, MsnTheme.FontGroupHeader, textBrush, new PointF(20, 4));
            }
        }
    }

    public class ContactTileControl : Panel
    {
        public string EntityId; // user id or channel id, consumer-defined
        public PresenceStatus Status = PresenceStatus.Offline;

        private readonly string _name;
        private string _subtitle;
        private bool _hover;
        private bool _selected;

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public string Subtitle
        {
            get { return _subtitle; }
            set { _subtitle = value; Invalidate(); }
        }

        public ContactTileControl(string name, string subtitle)
        {
            _name = name;
            _subtitle = subtitle;
            Height = 40;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        public void SetStatus(PresenceStatus status)
        {
            Status = status;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color back = _selected ? MsnTheme.TileSelected : (_hover ? MsnTheme.TileHover : MsnTheme.PanelBackground);
            using (var b = new SolidBrush(back)) g.FillRectangle(b, ClientRectangle);

            // Avatar placeholder circle with initials
            var avatarRect = new Rectangle(8, 6, 28, 28);
            using (var avBrush = new SolidBrush(Color.FromArgb(0x7A, 0xA7, 0xDE)))
                g.FillEllipse(avBrush, avatarRect);
            string initials = GetInitials(_name);
            using (var textBrush = new SolidBrush(Color.White))
            {
                var sz = g.MeasureString(initials, MsnTheme.FontSmall);
                g.DrawString(initials, MsnTheme.FontSmall, textBrush,
                    avatarRect.X + (avatarRect.Width - sz.Width) / 2,
                    avatarRect.Y + (avatarRect.Height - sz.Height) / 2);
            }

            // Status dot, bottom-right of avatar
            MsnTheme.DrawStatusDot(g, new Rectangle(avatarRect.Right - 9, avatarRect.Bottom - 9, 10, 10), Status);

            using (var nameBrush = new SolidBrush(Color.Black))
                g.DrawString(_name, MsnTheme.FontBold, nameBrush, new PointF(44, 4));

            if (!string.IsNullOrEmpty(_subtitle))
            {
                using (var subBrush = new SolidBrush(Color.DimGray))
                    g.DrawString(_subtitle, MsnTheme.FontSmall, subBrush, new PointF(44, 20));
            }
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
            return ("" + parts[0][0] + parts[1][0]).ToUpperInvariant();
        }
    }
}
