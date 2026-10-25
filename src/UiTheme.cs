using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PortalAutoLogin
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(243, 243, 243);
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);
        public static readonly Color Text = Color.FromArgb(27, 27, 27);
        public static readonly Color SubText = Color.FromArgb(95, 95, 95);
        public static readonly Color Faint = Color.FromArgb(140, 140, 140);
        public static readonly Color Accent = Color.FromArgb(15, 108, 189);
        public static readonly Color AccentHover = Color.FromArgb(26, 122, 204);
        public static readonly Color AccentDown = Color.FromArgb(11, 90, 158);
        public static readonly Color Border = Color.FromArgb(228, 228, 228);
        public static readonly Color InputBorder = Color.FromArgb(209, 209, 209);
        public static readonly Color Ok = Color.FromArgb(15, 123, 15);
        public static readonly Color Warn = Color.FromArgb(157, 93, 0);
        public static readonly Color Bad = Color.FromArgb(196, 43, 28);
        public static readonly Color Idle = Color.FromArgb(138, 138, 138);

        public static Font UI(float size)
        {
            return new Font("Microsoft YaHei UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        public static Font UI(float size, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class CardPanel : Panel
    {
        public int Radius = 12;
        public Color FillColor = Theme.Surface;
        public Color BorderColor = Theme.Border;
        public bool DrawBorder = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(rect, Radius))
            {
                using (var brush = new SolidBrush(FillColor)) e.Graphics.FillPath(brush, path);
                if (DrawBorder)
                {
                    using (var pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
                }
            }
        }
    }

    public class RoundedButton : Button
    {
        public int Radius = 8;
        public Color FillColor = Theme.Accent;
        public Color HoverColor = Theme.AccentHover;
        public Color DownColor = Theme.AccentDown;
        public Color BorderColor = Color.Empty;
        public Color TextColor = Color.White;

        private bool _hover;
        private bool _down;

        protected override bool ShowFocusCues
        {
            get { return false; }
        }

        public RoundedButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            Font = Theme.UI(9.5f);
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            _down = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill;
            if (!Enabled) fill = Color.FromArgb(232, 232, 232);
            else if (_down) fill = DownColor;
            else if (_hover) fill = HoverColor;
            else fill = FillColor;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundRect(rect, Radius))
            {
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                if (BorderColor != Color.Empty)
                {
                    using (var pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
                }
            }

            Color textColor = Enabled ? TextColor : Theme.Idle;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    public class ToggleSwitch : CheckBox
    {
        private bool _hover;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            AutoSize = false;
            Size = new Size(44, 24);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Height - 2;
            if (h < 12) h = Height;
            int w = Width - 2;
            var track = new Rectangle(1, (Height - h) / 2, w, h);
            Color trackColor;
            if (Checked) trackColor = _hover ? Color.FromArgb(30, 130, 210) : Theme.Accent;
            else trackColor = _hover ? Color.FromArgb(170, 170, 170) : Color.FromArgb(190, 190, 190);

            using (var path = Theme.RoundRect(track, h / 2))
            {
                using (var brush = new SolidBrush(trackColor)) e.Graphics.FillPath(brush, path);
            }

            int knob = h - 6;
            int x = Checked ? track.Right - knob - 3 : track.Left + 3;
            int y = track.Top + 3;
            using (var brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, x, y, knob, knob);
            }
        }
    }

    public class Dot : Control
    {
        public Color DotColor = Theme.Idle;

        public Dot()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(12, 12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Math.Min(Width, Height) - 2;
            using (var glow = new SolidBrush(Color.FromArgb(40, DotColor)))
            {
                e.Graphics.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
            }
            using (var brush = new SolidBrush(DotColor))
            {
                e.Graphics.FillEllipse(brush, 1, 1, d, d);
            }
        }
    }
}
