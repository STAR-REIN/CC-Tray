using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CCTray
{
    internal enum ServiceState
    {
        Checking,
        Running,
        Stopped,
        NotInstalled,
        Unavailable,
        Unknown
    }

    internal sealed class WslResult
    {
        public int ExitCode;
        public string Output;
    }

    internal sealed class LanguageChoice
    {
        public string Code;
        public string Name;

        public LanguageChoice(string code, string name)
        {
            Code = code;
            Name = name;
        }
    }

    internal static class TrayPalette
    {
        public static readonly Color Surface = Color.FromArgb(255, 246, 245, 242);
        public static readonly Color SurfaceRaised = Color.FromArgb(255, 239, 238, 235);
        public static readonly Color Selection = Color.FromArgb(255, 230, 229, 226);
        public static readonly Color Text = Color.FromArgb(255, 33, 34, 36);
        public static readonly Color MutedText = Color.FromArgb(255, 139, 142, 148);
        public static readonly Color Border = Color.FromArgb(255, 218, 218, 216);
        public static readonly Color Separator = Color.FromArgb(255, 228, 228, 226);
        public static readonly Color Accent = Color.FromArgb(255, 44, 142, 90);
    }

    internal static class TrayGraphics
    {
        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal enum FluentRowKind
    {
        Status,
        Action,
        Toggle,
        Separator
    }

    internal sealed class FluentMenuRow
    {
        public FluentRowKind Kind;
        public Func<string> Text;
        public Func<string> SecondaryText;
        public Func<bool> IsChecked;
        public Func<bool> IsEnabled;
        public Func<Color> DotColor;
        public Action Click;
        public bool DismissAfterClick;
        public bool ShowsArrow;
    }

    internal sealed class FluentMenuPopup : Form
    {
        private const int MenuWidth = 224;
        private const int WideMenuWidth = 264;
        private const int ExtraWideMenuWidth = 292;
        private const int EdgePadding = 6;
        private const int RowHeight = 30;
        private const int StatusHeight = 31;
        private const int SeparatorHeight = 7;
        private const int CheckColumn = 28;
        private const int TextLeft = 34;
        private const int StatusDotSize = 15;
        private const int DropShadowClassStyle = 0x00020000;
        private const int CornerPreferenceAttribute = 33;
        private const int RoundCornerPreference = 2;

        private readonly List<FluentMenuRow> rows = new List<FluentMenuRow>();
        private readonly Font secondaryFont;
        private int preferredWidth = MenuWidth;
        private int hoverIndex = -1;

        public event Action OpeningMenu;
        public bool HideOnDeactivate = true;

        public FluentMenuPopup()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = TrayPalette.Surface;
            DoubleBuffered = true;
            Font = CreateMenuFont();
            secondaryFont = CreateSecondaryMenuFont();
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            Opacity = 0.9;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Padding = Padding.Empty;
            Size = new Size(preferredWidth, 1);

            Deactivate += delegate
            {
                if (HideOnDeactivate)
                {
                    Hide();
                }
            };
            MouseLeave += delegate
            {
                hoverIndex = -1;
                Invalidate();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= DropShadowClassStyle;
                return parameters;
            }
        }

        public void AddStatus(Func<string> text, Func<Color> dotColor)
        {
            rows.Add(NewRow(FluentRowKind.Status, text, null, null, dotColor));
        }

        public void SetPreferredWidth(int width)
        {
            preferredWidth = Math.Max(MenuWidth, width);
        }

        public void ClearRows()
        {
            rows.Clear();
            hoverIndex = -1;
        }

        public void AddAction(string text, Func<bool> enabled, Action click)
        {
            AddAction(text, enabled, click, true);
        }

        public void AddAction(string text, Func<bool> enabled, Action click, bool dismissAfterClick)
        {
            AddAction(delegate { return text; }, enabled, click, dismissAfterClick);
        }

        public void AddAction(Func<string> text, Func<bool> enabled, Action click)
        {
            AddAction(text, enabled, click, true);
        }

        public void AddAction(Func<string> text, Func<bool> enabled, Action click, bool dismissAfterClick)
        {
            FluentMenuRow row = NewRow(FluentRowKind.Action, text, enabled, click, null);
            row.DismissAfterClick = dismissAfterClick;
            rows.Add(row);
        }

        public void AddToggle(string text, Func<bool> isChecked, Action click)
        {
            AddToggle(text, isChecked, null, click);
        }

        public void AddToggle(string text, Func<bool> isChecked, Func<bool> enabled, Action click)
        {
            AddToggle(delegate { return text; }, isChecked, enabled, click);
        }

        public void AddToggle(Func<string> text, Func<bool> isChecked, Func<bool> enabled, Action click)
        {
            FluentMenuRow row = NewRow(FluentRowKind.Toggle, text, null, click, null);
            row.IsEnabled = enabled;
            row.IsChecked = isChecked;
            row.DismissAfterClick = false;
            rows.Add(row);
        }

        public void AddSubmenuAction(string text, Action click)
        {
            AddSubmenuAction(delegate { return text; }, null, click);
        }

        public void AddSubmenuAction(Func<string> text, Func<string> secondaryText, Action click)
        {
            FluentMenuRow row = NewRow(FluentRowKind.Action, text, delegate { return true; }, click, null);
            row.SecondaryText = secondaryText;
            row.DismissAfterClick = false;
            row.ShowsArrow = true;
            rows.Add(row);
        }

        public void AddSeparator()
        {
            rows.Add(NewRow(FluentRowKind.Separator, null, null, null, null));
        }

        public void OpenAt(Point anchor)
        {
            if (OpeningMenu != null)
            {
                OpeningMenu();
            }

            RefreshView();
            Rectangle workArea = Screen.FromPoint(anchor).WorkingArea;
            int x = Math.Min(anchor.X - Width + 22, workArea.Right - Width - 8);
            int y = anchor.Y - Height - 8;
            if (y < workArea.Top + 8)
            {
                y = Math.Min(anchor.Y + 8, workArea.Bottom - Height - 8);
            }
            x = Math.Max(workArea.Left + 8, x);
            Location = new Point(x, y);

            if (!Visible)
            {
                Show();
            }
            Activate();
            BringToFront();
        }

        public void OpenBeside(Point anchor)
        {
            RefreshView();
            Rectangle workArea = Screen.FromPoint(anchor).WorkingArea;
            int x = anchor.X + 4;
            int y = anchor.Y - RowHeight / 2 - EdgePadding;
            if (x + Width > workArea.Right - 8)
            {
                x = anchor.X - Width - 4;
            }
            y = Math.Max(workArea.Top + 8, Math.Min(y, workArea.Bottom - Height - 8));
            Location = new Point(Math.Max(workArea.Left + 8, x), y);

            if (!Visible)
            {
                Show();
            }
            Activate();
            BringToFront();
        }

        public Point CurrentRowSubmenuAnchor()
        {
            int index = hoverIndex < 0 ? 0 : hoverIndex;
            int y = EdgePadding;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                FluentMenuRow row = rows[rowIndex];
                int rowHeight = RowHeightFor(row);
                if (rowIndex == index)
                {
                    return new Point(Right - 2, Top + y + rowHeight / 2);
                }
                y += rowHeight;
            }
            return new Point(Right - 2, Top + EdgePadding + RowHeight / 2);
        }

        public void RefreshView()
        {
            int height = EdgePadding * 2;
            foreach (FluentMenuRow row in rows)
            {
                height += RowHeightFor(row);
            }

            Size = new Size(preferredWidth, height);
            UpdateRoundedRegion();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int preference = RoundCornerPreference;
                DwmSetWindowAttribute(Handle, CornerPreferenceAttribute, ref preference, sizeof(int));
            }
            catch
            {
            }
            UpdateRoundedRegion();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush surface = new SolidBrush(TrayPalette.Surface))
            using (Pen border = new Pen(TrayPalette.Border))
            using (GraphicsPath outline = TrayGraphics.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            {
                e.Graphics.FillPath(surface, outline);
                e.Graphics.DrawPath(border, outline);
            }

            int y = EdgePadding;
            for (int index = 0; index < rows.Count; index++)
            {
                FluentMenuRow row = rows[index];
                Rectangle rowBounds = new Rectangle(EdgePadding, y, Width - EdgePadding * 2, RowHeightFor(row));
                DrawRow(e.Graphics, index, row, rowBounds);
                y += rowBounds.Height;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index != hoverIndex)
            {
                hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            int index = HitTest(e.Location);
            if (index < 0)
            {
                return;
            }

            FluentMenuRow row = rows[index];
            if (IsClickable(row))
            {
                if (row.DismissAfterClick)
                {
                    Hide();
                }
                row.Click();
                if (!row.DismissAfterClick && Visible)
                {
                    RefreshView();
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && secondaryFont != null)
            {
                secondaryFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void DrawRow(Graphics graphics, int index, FluentMenuRow row, Rectangle bounds)
        {
            if (row.Kind == FluentRowKind.Separator)
            {
                int separatorY = bounds.Top + bounds.Height / 2;
                using (Pen separator = new Pen(TrayPalette.Separator))
                {
                    graphics.DrawLine(separator, bounds.Left + 8, separatorY, bounds.Right - 8, separatorY);
                }
                return;
            }

            if (index == hoverIndex && IsClickable(row))
            {
                Rectangle hover = new Rectangle(bounds.Left + 2, bounds.Top + 1, bounds.Width - 4, bounds.Height - 2);
                using (SolidBrush selected = new SolidBrush(TrayPalette.Selection))
                using (GraphicsPath path = TrayGraphics.RoundedPath(hover, 5))
                {
                    graphics.FillPath(selected, path);
                }
            }

            Color textColor = IsEnabled(row) || row.Kind == FluentRowKind.Status ? TrayPalette.Text : TrayPalette.MutedText;
            if (row.Kind == FluentRowKind.Status && row.DotColor != null)
            {
                using (SolidBrush dot = new SolidBrush(row.DotColor()))
                {
                    graphics.FillEllipse(dot, bounds.Left + 11, bounds.Top + (bounds.Height - StatusDotSize) / 2, StatusDotSize, StatusDotSize);
                }
            }
            else if (row.Kind == FluentRowKind.Toggle && row.IsChecked != null && row.IsChecked())
            {
                DrawCheck(graphics, bounds);
            }

            if (row.ShowsArrow)
            {
                DrawArrow(graphics, bounds, textColor);
            }

            int rightInset = row.ShowsArrow ? 26 : 10;
            Rectangle textBounds = new Rectangle(bounds.Left + TextLeft, bounds.Top, bounds.Width - TextLeft - rightInset, bounds.Height);
            string text = row.Text == null ? "" : row.Text();
            string secondary = row.SecondaryText == null ? "" : row.SecondaryText();
            if (secondary.Length == 0)
            {
                TextRenderer.DrawText(
                    graphics,
                    text,
                    Font,
                    textBounds,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                );
            }
            else
            {
                Size primarySize = TextRenderer.MeasureText(
                    graphics,
                    text,
                    Font,
                    new Size(textBounds.Width, textBounds.Height),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                );
                int secondaryX = Math.Min(textBounds.Left + primarySize.Width + 2, textBounds.Right - 44);
                Rectangle primaryBounds = new Rectangle(textBounds.Left, textBounds.Top, Math.Max(20, secondaryX - textBounds.Left - 2), textBounds.Height);
                Rectangle secondaryBounds = new Rectangle(secondaryX, textBounds.Top + 1, textBounds.Right - secondaryX, textBounds.Height);

                TextRenderer.DrawText(
                    graphics,
                    text,
                    Font,
                    primaryBounds,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                );
                TextRenderer.DrawText(
                    graphics,
                    secondary,
                    secondaryFont,
                    secondaryBounds,
                    TrayPalette.MutedText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix
                );
            }
        }

        private static void DrawArrow(Graphics graphics, Rectangle bounds, Color color)
        {
            using (Pen arrow = new Pen(color, 1.55f))
            {
                arrow.StartCap = LineCap.Round;
                arrow.EndCap = LineCap.Round;
                int x = bounds.Right - 20;
                int y = bounds.Top + bounds.Height / 2;
                graphics.DrawLines(arrow, new Point[]
                {
                    new Point(x, y - 5),
                    new Point(x + 5, y),
                    new Point(x, y + 5)
                });
            }
        }

        private static void DrawCheck(Graphics graphics, Rectangle bounds)
        {
            using (Pen check = new Pen(Color.FromArgb(255, 75, 78, 84), 1.65f))
            {
                check.StartCap = LineCap.Round;
                check.EndCap = LineCap.Round;
                int x = bounds.Left + 13;
                int y = bounds.Top + bounds.Height / 2;
                graphics.DrawLines(check, new Point[]
                {
                    new Point(x, y),
                    new Point(x + 4, y + 4),
                    new Point(x + 11, y - 5)
                });
            }
        }

        private int HitTest(Point point)
        {
            int y = EdgePadding;
            for (int index = 0; index < rows.Count; index++)
            {
                FluentMenuRow row = rows[index];
                Rectangle bounds = new Rectangle(EdgePadding, y, Width - EdgePadding * 2, RowHeightFor(row));
                if (bounds.Contains(point))
                {
                    return IsClickable(row) ? index : -1;
                }
                y += bounds.Height;
            }
            return -1;
        }

        private static FluentMenuRow NewRow(FluentRowKind kind, Func<string> text, Func<bool> enabled, Action click, Func<Color> dotColor)
        {
            FluentMenuRow row = new FluentMenuRow();
            row.Kind = kind;
            row.Text = text;
            row.IsEnabled = enabled;
            row.Click = click;
            row.DotColor = dotColor;
            row.DismissAfterClick = true;
            return row;
        }

        private static bool IsEnabled(FluentMenuRow row)
        {
            return row.IsEnabled == null || row.IsEnabled();
        }

        private static bool IsClickable(FluentMenuRow row)
        {
            return (row.Kind == FluentRowKind.Action || row.Kind == FluentRowKind.Toggle) && row.Click != null && IsEnabled(row);
        }

        private static int RowHeightFor(FluentMenuRow row)
        {
            if (row.Kind == FluentRowKind.Status)
            {
                return StatusHeight;
            }
            if (row.Kind == FluentRowKind.Separator)
            {
                return SeparatorHeight;
            }
            return RowHeight;
        }

        private void UpdateRoundedRegion()
        {
            if (Width < 2 || Height < 2)
            {
                return;
            }

            using (GraphicsPath path = TrayGraphics.RoundedPath(new Rectangle(0, 0, Width, Height), 12))
            {
                Region oldRegion = Region;
                Region = new Region(path);
                if (oldRegion != null)
                {
                    oldRegion.Dispose();
                }
            }
        }

        private static Font CreateMenuFont()
        {
            try
            {
                return new Font("Segoe UI Variable Text", 10.8f, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MenuFont.FontFamily, 10.8f, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        private static Font CreateSecondaryMenuFont()
        {
            try
            {
                return new Font("Segoe UI Variable Text", 9.2f, FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font(SystemFonts.MenuFont.FontFamily, 9.2f, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int valueSize);
    }

    internal sealed class WslUserConfig
    {
        public string Distro;
        public string User;
        public bool IsDefault;
        public static string LastDetectError = "";

        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(Distro) && !String.IsNullOrWhiteSpace(User); }
        }

        public string DisplayText(string defaultLabel)
        {
            string label = Distro + " / " + User;
            return IsDefault ? label + "  " + defaultLabel : label;
        }

        public static WslUserConfig Detect()
        {
            List<WslUserConfig> configs = DetectAll();
            return configs.Count == 0 ? new WslUserConfig() : configs[0];
        }

        public static List<WslUserConfig> DetectAll()
        {
            LastDetectError = "";
            List<WslUserConfig> configs = new List<WslUserConfig>();
            List<WslDistroInfo> distros = DetectDistros();
            if (distros.Count == 0 && LastDetectError.Length == 0)
            {
                LastDetectError = "No WSL distributions returned.";
            }

            foreach (WslDistroInfo distro in distros)
            {
                WslResult whoami = DetectUserForDistro(distro.Name.Trim());
                if (whoami.ExitCode != 0 || String.IsNullOrWhiteSpace(whoami.Output))
                {
                    LastDetectError = "whoami failed: " + ShortError(whoami.Output);
                    continue;
                }

                string user = FirstUsefulLine(whoami.Output).Trim();
                if (user.Length == 0)
                {
                    continue;
                }

                WslUserConfig config = new WslUserConfig();
                config.Distro = distro.Name.Trim();
                config.User = user;
                config.IsDefault = distro.IsDefault;
                configs.Add(config);
            }

            configs.Sort(delegate(WslUserConfig left, WslUserConfig right)
            {
                if (left.IsDefault == right.IsDefault)
                {
                    return String.Compare(left.Distro, right.Distro, StringComparison.OrdinalIgnoreCase);
                }
                return left.IsDefault ? -1 : 1;
            });
            return configs;
        }

        private static WslResult DetectUserForDistro(string distro)
        {
            WslResult result = TrayContext.RunWslExecForConfig(distro, "", "whoami");
            if (result.ExitCode == 0 && !String.IsNullOrWhiteSpace(result.Output))
            {
                return result;
            }

            WslResult idResult = TrayContext.RunWslExecForConfig(distro, "", "id", "-un");
            if (idResult.ExitCode == 0 && !String.IsNullOrWhiteSpace(idResult.Output))
            {
                return idResult;
            }

            WslResult shellResult = TrayContext.RunWslForConfig(distro, "", "whoami");
            if (shellResult.ExitCode == 0 && !String.IsNullOrWhiteSpace(shellResult.Output))
            {
                return shellResult;
            }

            if (idResult.ExitCode != 0 || !String.IsNullOrWhiteSpace(idResult.Output))
            {
                return idResult;
            }
            if (shellResult.ExitCode != 0 || !String.IsNullOrWhiteSpace(shellResult.Output))
            {
                return shellResult;
            }
            return result;
        }

        private static string DetectDefaultDistro()
        {
            List<WslDistroInfo> distros = DetectDistros();
            foreach (WslDistroInfo distro in distros)
            {
                if (distro.IsDefault)
                {
                    return distro.Name;
                }
            }
            return distros.Count == 0 ? "" : distros[0].Name;
        }

        private static List<WslDistroInfo> DetectDistros()
        {
            List<WslDistroInfo> distros = new List<WslDistroInfo>();
            WslResult result = TrayContext.RunRawWsl("--list --verbose");
            if (result.ExitCode != 0)
            {
                LastDetectError = "wsl list failed: " + ShortError(result.Output);
                result = TrayContext.RunRawWsl("--list --quiet");
            }
            if (result.ExitCode != 0)
            {
                LastDetectError = "wsl list failed: " + ShortError(result.Output);
                return distros;
            }

            string[] lines = (result.Output ?? "").Replace("\0", "").Replace("\r", "").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim('\uFEFF', ' ', '\t');
                if (line.Length == 0 || line.StartsWith("NAME", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                bool isDefault = line.StartsWith("*", StringComparison.Ordinal);
                string[] parts = line.TrimStart('*').Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    WslDistroInfo distro = new WslDistroInfo();
                    distro.Name = parts[0];
                    distro.IsDefault = isDefault;
                    distros.Add(distro);
                }
            }
            return distros;
        }

        private static string FirstUsefulLine(string text)
        {
            string[] lines = (text ?? "").Replace("\0", "").Replace("\r", "").Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length > 0)
                {
                    return line;
                }
            }
            return "";
        }

        private static string ShortError(string text)
        {
            string line = FirstUsefulLine(text);
            if (line.Length == 0)
            {
                return "empty output";
            }
            return line.Length > 80 ? line.Substring(0, 80) : line;
        }

        private sealed class WslDistroInfo
        {
            public string Name;
            public bool IsDefault;
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private const int StatusIntervalMs = 8000;
        private const int AutoStartCooldownMs = 30000;
        private const int CommandTimeoutMs = 30000;
        private const string SettingsPath = @"Software\CC-Tray";
        private const string LegacySettingsPath = @"Software\CCConnectTray";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "CC-Tray";
        private const string LegacyRunValueName = "CCConnectTray";
        private static readonly LanguageChoice[] LanguageChoices = new LanguageChoice[]
        {
            new LanguageChoice("EN", "English"),
            new LanguageChoice("ZH", "中文"),
            new LanguageChoice("KO", "한국어"),
            new LanguageChoice("JA", "日本語"),
            new LanguageChoice("FR", "Français")
        };

        private readonly Control dispatcher;
        private readonly NotifyIcon trayIcon;
        private readonly FluentMenuPopup menu;
        private readonly FluentMenuPopup userMenu;
        private readonly FluentMenuPopup languageMenu;
        private readonly System.Windows.Forms.Timer statusTimer;

        private ServiceState state = ServiceState.Checking;
        private string configuredDistro;
        private string configuredUser;
        private string languageCode;
        private List<WslUserConfig> detectedUsers = new List<WslUserConfig>();
        private bool usersDetected;
        private bool keepRunning;
        private bool startWithWindows;
        private bool stopOnExit;
        private bool statusBusy;
        private bool commandBusy;
        private bool exiting;
        private bool keepMainMenuAfterSubmenuClose;
        private DateTime nextAutoStartUtc = DateTime.MinValue;

        public TrayContext()
        {
            dispatcher = new Control();
            IntPtr handle = dispatcher.Handle;

            configuredDistro = ReadStringSetting("WslDistro", "");
            configuredUser = ReadStringSetting("WslUser", "");
            languageCode = NormalizeLanguage(ReadStringSetting("Language", "EN"));
            keepRunning = ReadBoolSetting("KeepRunning", false);
            startWithWindows = ReadBoolSetting("StartWithWindows", false);
            stopOnExit = ReadBoolSetting("StopOnExit", false);
            if (!IsUserConfigured())
            {
                keepRunning = false;
                startWithWindows = false;
                stopOnExit = false;
            }
            UpdateStartupRegistration(startWithWindows);

            menu = new FluentMenuPopup();
            menu.SetPreferredWidth(MainMenuWidth());
            menu.OpeningMenu += delegate
            {
                UpdateMenu();
                if (IsUserConfigured())
                {
                    RefreshStatus();
                }
            };
            menu.AddStatus(CurrentStatusText, CurrentStatusColor);
            menu.AddSeparator();
            menu.AddSubmenuAction(delegate { return T("ConfigureUser"); }, null, ShowUserConfigMenu);
            menu.AddSubmenuAction(delegate { return T("Language"); }, delegate { return "• " + languageCode; }, ShowLanguageMenu);
            menu.AddSeparator();
            menu.AddToggle(delegate { return T("KeepRunning"); }, delegate { return IsUserConfigured() && keepRunning; }, CanUseConfiguredFeatures, ToggleKeepRunning);
            menu.AddToggle(delegate { return T("StartWithWindows"); }, delegate { return IsUserConfigured() && startWithWindows; }, CanUseConfiguredFeatures, ToggleStartWithWindows);
            menu.AddToggle(delegate { return T("StopOnExit"); }, delegate { return IsUserConfigured() && stopOnExit; }, CanUseConfiguredFeatures, ToggleStopOnExit);
            menu.AddSeparator();
            menu.AddAction(delegate { return T("InstallDaemon"); }, CanInstall, delegate { RunManualCommand(T("Install"), "cc-connect daemon install --work-dir \"$HOME/.cc-connect\"", true); });
            menu.AddAction(delegate { return T("Start"); }, CanStart, delegate { RunManualCommand(T("Start"), "cc-connect daemon start", true); });
            menu.AddAction(delegate { return T("Restart"); }, CanRestart, delegate { RunManualCommand(T("Restart"), "cc-connect daemon restart", true); });
            menu.AddAction(delegate { return T("Stop"); }, CanStop, delegate { RunManualCommand(T("Stop"), "cc-connect daemon stop", false); });
            menu.AddAction(delegate { return T("RefreshStatus"); }, CanRefresh, delegate { RefreshStatus(); }, false);
            menu.AddSeparator();
            menu.AddAction(delegate { return T("Exit"); }, delegate { return true; }, delegate { ExitThread(); });

            userMenu = new FluentMenuPopup();
            userMenu.SetPreferredWidth(380);
            userMenu.OpeningMenu += delegate { BuildUserConfigMenu(false); };
            userMenu.VisibleChanged += delegate
            {
                if (!userMenu.Visible)
                {
                    menu.HideOnDeactivate = true;
                    if (keepMainMenuAfterSubmenuClose)
                    {
                        keepMainMenuAfterSubmenuClose = false;
                    }
                    else if (!menu.Bounds.Contains(Cursor.Position))
                    {
                        menu.Hide();
                    }
                }
            };

            languageMenu = new FluentMenuPopup();
            languageMenu.SetPreferredWidth(264);
            languageMenu.OpeningMenu += delegate { BuildLanguageMenu(); };
            languageMenu.VisibleChanged += delegate
            {
                if (!languageMenu.Visible)
                {
                    menu.HideOnDeactivate = true;
                    if (keepMainMenuAfterSubmenuClose)
                    {
                        keepMainMenuAfterSubmenuClose = false;
                    }
                    else if (!menu.Bounds.Contains(Cursor.Position))
                    {
                        menu.Hide();
                    }
                }
            };

            trayIcon = new NotifyIcon();
            trayIcon.Icon = LoadTrayIcon();
            trayIcon.Text = ShortTooltip(T("UnconfiguredUser"));
            trayIcon.Visible = true;
            trayIcon.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button == MouseButtons.Left || args.Button == MouseButtons.Right)
                {
                    if (menu.Visible)
                    {
                        userMenu.Hide();
                        languageMenu.Hide();
                        menu.Hide();
                    }
                    else
                    {
                        menu.OpenAt(Cursor.Position);
                    }
                }
            };

            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = StatusIntervalMs;
            statusTimer.Tick += delegate { RefreshStatus(); };
            statusTimer.Start();

            UpdateMenu();
            if (IsUserConfigured())
            {
                RefreshStatus();
            }
        }

        private void ShowUserConfigMenu()
        {
            languageMenu.Hide();
            menu.HideOnDeactivate = false;
            BuildUserConfigMenu(false);
            userMenu.OpenBeside(menu.CurrentRowSubmenuAnchor());
        }

        private void ShowLanguageMenu()
        {
            userMenu.Hide();
            menu.HideOnDeactivate = false;
            BuildLanguageMenu();
            languageMenu.OpenBeside(menu.CurrentRowSubmenuAnchor());
        }

        private void BuildLanguageMenu()
        {
            languageMenu.ClearRows();
            languageMenu.AddStatus(CurrentLanguageText, delegate { return TrayPalette.Accent; });
            languageMenu.AddSeparator();
            foreach (LanguageChoice choice in LanguageChoices)
            {
                LanguageChoice candidate = choice;
                languageMenu.AddToggle(
                    delegate { return candidate.Code + "  " + candidate.Name; },
                    delegate { return String.Equals(languageCode, candidate.Code, StringComparison.OrdinalIgnoreCase); },
                    delegate { return true; },
                    delegate { SelectLanguage(candidate.Code); }
                );
            }
        }

        private string CurrentLanguageText()
        {
            return T("Language") + ": " + languageCode;
        }

        private void SelectLanguage(string code)
        {
            languageCode = NormalizeLanguage(code);
            WriteStringSetting("Language", languageCode);
            keepMainMenuAfterSubmenuClose = true;
            languageMenu.Hide();
            UpdateMenu();
        }

        private void BuildUserConfigMenu(bool forceDetect)
        {
            if (forceDetect || !usersDetected)
            {
                detectedUsers = WslUserConfig.DetectAll();
                usersDetected = true;
            }

            userMenu.ClearRows();
            userMenu.AddStatus(CurrentUserConfigText, CurrentUserConfigColor);
            userMenu.AddSeparator();

            if (detectedUsers.Count == 0)
            {
                userMenu.AddAction(delegate { return DetectFailureText(); }, delegate { return false; }, null, false);
            }
            else
            {
                foreach (WslUserConfig detected in detectedUsers)
                {
                    WslUserConfig candidate = detected;
                    userMenu.AddToggle(
                        delegate { return candidate.DisplayText(T("Default")); },
                        delegate { return IsSameUserConfig(candidate); },
                        delegate { return true; },
                        delegate { SaveUserConfig(candidate); }
                    );
                }
            }

            userMenu.AddSeparator();
            userMenu.AddAction(delegate { return T("Redetect"); }, delegate { return true; }, delegate
            {
                BuildUserConfigMenu(true);
                userMenu.RefreshView();
            }, false);
            userMenu.AddAction(delegate { return T("ClearConfig"); }, delegate { return IsUserConfigured(); }, delegate
            {
                ClearUserConfig();
                BuildUserConfigMenu(false);
                userMenu.RefreshView();
            }, false);
        }

        private string CurrentUserConfigText()
        {
            return IsUserConfigured() ? T("Current") + ": " + configuredDistro + " / " + configuredUser : T("UnconfiguredUser");
        }

        private string DetectFailureText()
        {
            string error = WslUserConfig.LastDetectError;
            if (String.IsNullOrWhiteSpace(error))
            {
                return T("NoWslUser");
            }
            return T("DetectFailed") + ": " + error;
        }

        private Color CurrentUserConfigColor()
        {
            return IsUserConfigured() ? TrayPalette.Accent : Color.FromArgb(255, 218, 132, 36);
        }

        private bool IsSameUserConfig(WslUserConfig config)
        {
            return IsUserConfigured()
                && String.Equals(configuredDistro, config.Distro, StringComparison.OrdinalIgnoreCase)
                && String.Equals(configuredUser, config.User, StringComparison.Ordinal);
        }

        private void SaveUserConfig(WslUserConfig config)
        {
            if (config == null || !config.IsConfigured)
            {
                return;
            }

            configuredDistro = config.Distro.Trim();
            configuredUser = config.User.Trim();
            WriteStringSetting("WslDistro", configuredDistro);
            WriteStringSetting("WslUser", configuredUser);
            state = ServiceState.Checking;
            UpdateMenu();
            userMenu.RefreshView();
            RefreshStatus();
        }

        private void ClearUserConfig()
        {
            configuredDistro = "";
            configuredUser = "";
            keepRunning = false;
            startWithWindows = false;
            stopOnExit = false;
            state = ServiceState.Unknown;
            statusBusy = false;
            WriteStringSetting("WslDistro", "");
            WriteStringSetting("WslUser", "");
            WriteBoolSetting("KeepRunning", false);
            WriteBoolSetting("StartWithWindows", false);
            WriteBoolSetting("StopOnExit", false);
            UpdateStartupRegistration(false);
            UpdateMenu();
        }

        private void ToggleKeepRunning()
        {
            if (!IsUserConfigured())
            {
                return;
            }
            keepRunning = !keepRunning;
            WriteBoolSetting("KeepRunning", keepRunning);
            if (keepRunning)
            {
                RefreshStatus();
            }
            UpdateMenu();
        }

        private void ToggleStartWithWindows()
        {
            if (!IsUserConfigured())
            {
                return;
            }
            startWithWindows = !startWithWindows;
            WriteBoolSetting("StartWithWindows", startWithWindows);
            UpdateStartupRegistration(startWithWindows);
            UpdateMenu();
        }

        private void ToggleStopOnExit()
        {
            if (!IsUserConfigured())
            {
                return;
            }
            stopOnExit = !stopOnExit;
            WriteBoolSetting("StopOnExit", stopOnExit);
            UpdateMenu();
        }

        private void RefreshStatus()
        {
            if (!IsUserConfigured())
            {
                statusBusy = false;
                state = ServiceState.Unknown;
                UpdateMenu();
                return;
            }

            if (exiting || statusBusy || commandBusy)
            {
                return;
            }

            statusBusy = true;
            UpdateMenu();
            RunWslAsync("cc-connect daemon status", delegate(WslResult result)
            {
                statusBusy = false;
                state = ClassifyStatus(result.ExitCode, result.Output);
                UpdateMenu();
                MaybeAutoStart();
            });
        }

        private void MaybeAutoStart()
        {
            if (!IsUserConfigured() || !keepRunning || commandBusy || state != ServiceState.Stopped || DateTime.UtcNow < nextAutoStartUtc)
            {
                return;
            }

            nextAutoStartUtc = DateTime.UtcNow.AddMilliseconds(AutoStartCooldownMs);
            RunCommand(T("AutoStart"), "cc-connect daemon start", false);
        }

        private void RunManualCommand(string label, string command, bool enableKeepRunning)
        {
            if (!IsUserConfigured())
            {
                return;
            }

            keepRunning = enableKeepRunning;
            WriteBoolSetting("KeepRunning", keepRunning);
            RunCommand(label, command, true);
        }

        private void RunCommand(string label, string command, bool showFailure)
        {
            if (!IsUserConfigured() || exiting || commandBusy)
            {
                return;
            }

            commandBusy = true;
            UpdateMenu();
            RunWslAsync(command, delegate(WslResult result)
            {
                commandBusy = false;
                if (result.ExitCode != 0 && showFailure)
                {
                    ShowError(label, LastUsefulLine(result.Output));
                }
                state = ServiceState.Checking;
                UpdateMenu();
                RefreshStatus();
            });
        }

        private void UpdateMenu()
        {
            menu.SetPreferredWidth(MainMenuWidth());
            string label = IsUserConfigured() ? StateLabel(state) : T("UnconfiguredUser");
            if (commandBusy)
            {
                label += T("BusySeparator") + T("Processing");
            }
            else if (statusBusy && IsUserConfigured())
            {
                label += T("BusySeparator") + T("Refreshing");
            }

            trayIcon.Text = ShortTooltip(label);
            menu.RefreshView();
        }

        private int MainMenuWidth()
        {
            switch (languageCode)
            {
                case "ZH":
                case "JA":
                    return 224;
                case "KO":
                    return 264;
                case "FR":
                    return 292;
                default:
                    return 264;
            }
        }

        private string CurrentStatusText()
        {
            if (!IsUserConfigured())
            {
                return T("UnconfiguredUser");
            }

            string label = StateLabel(state);
            if (commandBusy)
            {
                label += T("BusySeparator") + T("Processing");
            }
            else if (statusBusy)
            {
                label += T("BusySeparator") + T("Refreshing");
            }
            return T("Status") + ": " + label;
        }

        private Color CurrentStatusColor()
        {
            if (!IsUserConfigured())
            {
                return Color.FromArgb(255, 218, 132, 36);
            }

            if (state == ServiceState.Running)
            {
                return TrayPalette.Accent;
            }
            if (state == ServiceState.Stopped)
            {
                return Color.FromArgb(255, 214, 72, 86);
            }
            if (state == ServiceState.NotInstalled)
            {
                return Color.FromArgb(255, 48, 126, 198);
            }
            if (state == ServiceState.Unavailable)
            {
                return Color.FromArgb(255, 196, 71, 82);
            }
            return TrayPalette.MutedText;
        }

        private bool CanInstall()
        {
            return IsUserConfigured() && !commandBusy && (state == ServiceState.NotInstalled || state == ServiceState.Unknown);
        }

        private bool CanStart()
        {
            return IsUserConfigured() && !commandBusy && (state == ServiceState.Stopped || state == ServiceState.Unknown);
        }

        private bool CanRestart()
        {
            return IsUserConfigured() && !commandBusy && state == ServiceState.Running;
        }

        private bool CanStop()
        {
            return IsUserConfigured() && !commandBusy && state == ServiceState.Running;
        }

        private bool CanRefresh()
        {
            return IsUserConfigured() && !commandBusy && !statusBusy;
        }

        private bool CanUseConfiguredFeatures()
        {
            return IsUserConfigured() && !commandBusy;
        }

        private bool IsUserConfigured()
        {
            return !String.IsNullOrWhiteSpace(configuredDistro) && !String.IsNullOrWhiteSpace(configuredUser);
        }

        private void RunWslAsync(string command, Action<WslResult> completed)
        {
            string distro = configuredDistro;
            string user = configuredUser;
            ThreadPool.QueueUserWorkItem(delegate
            {
                WslResult result = RunWslForConfig(distro, user, command);
                if (exiting || dispatcher.IsDisposed)
                {
                    return;
                }

                try
                {
                    dispatcher.BeginInvoke(new MethodInvoker(delegate
                    {
                        if (!exiting)
                        {
                            completed(result);
                        }
                    }));
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        internal static WslResult RunWslForConfig(string distro, string user, string command)
        {
            return RunProcess(ResolveWslExe(), BuildWslArguments(command, distro, user));
        }

        internal static WslResult RunWslExecForConfig(string distro, string user, params string[] commandParts)
        {
            return RunProcess(ResolveWslExe(), BuildWslExecArguments(distro, user, commandParts));
        }

        internal static WslResult RunRawWsl(string arguments)
        {
            return RunProcess(ResolveWslExe(), arguments);
        }

        private static WslResult RunProcess(string fileName, string arguments)
        {
            Process process = null;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = fileName;
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                process = Process.Start(startInfo);
                byte[] stdoutBytes = ReadAllBytes(process.StandardOutput.BaseStream);
                byte[] stderrBytes = ReadAllBytes(process.StandardError.BaseStream);
                if (!process.WaitForExit(CommandTimeoutMs))
                {
                    process.Kill();
                    return NewResult(-1, "WSL command timed out.");
                }

                string stdout = DecodeProcessOutput(stdoutBytes);
                string stderr = DecodeProcessOutput(stderrBytes);
                return NewResult(process.ExitCode, (stdout + Environment.NewLine + stderr).Trim());
            }
            catch (Exception error)
            {
                return NewResult(-1, error.Message);
            }
            finally
            {
                if (process != null)
                {
                    process.Dispose();
                }
            }
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    memory.Write(buffer, 0, read);
                }
                return memory.ToArray();
            }
        }

        private static string DecodeProcessOutput(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return "";
            }

            if (bytes.Length >= 2)
            {
                if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    return Encoding.Unicode.GetString(bytes);
                }
                if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                {
                    return Encoding.BigEndianUnicode.GetString(bytes);
                }
            }

            int oddZeros = 0;
            int evenZeros = 0;
            int pairs = bytes.Length / 2;
            for (int index = 0; index + 1 < bytes.Length; index += 2)
            {
                if (bytes[index] == 0)
                {
                    evenZeros++;
                }
                if (bytes[index + 1] == 0)
                {
                    oddZeros++;
                }
            }

            if (pairs > 0 && oddZeros > pairs / 2)
            {
                return Encoding.Unicode.GetString(bytes);
            }
            if (pairs > 0 && evenZeros > pairs / 2)
            {
                return Encoding.BigEndianUnicode.GetString(bytes);
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static WslResult NewResult(int exitCode, string output)
        {
            WslResult result = new WslResult();
            result.ExitCode = exitCode;
            result.Output = output ?? "";
            return result;
        }

        private static string BuildWslArguments(string command, string distro, string user)
        {
            StringBuilder args = new StringBuilder();
            if (!String.IsNullOrWhiteSpace(distro))
            {
                args.Append("--distribution ");
                args.Append(WslToken(distro.Trim()));
                args.Append(" ");
            }
            if (!String.IsNullOrWhiteSpace(user))
            {
                args.Append("--user ");
                args.Append(WslToken(user.Trim()));
                args.Append(" ");
            }

            args.Append("--exec bash -lc ");
            args.Append(QuoteArgument("cd \"$HOME\" && " + command));
            return args.ToString();
        }

        private static string BuildWslExecArguments(string distro, string user, string[] commandParts)
        {
            StringBuilder args = new StringBuilder();
            if (!String.IsNullOrWhiteSpace(distro))
            {
                args.Append("--distribution ");
                args.Append(WslToken(distro.Trim()));
                args.Append(" ");
            }
            if (!String.IsNullOrWhiteSpace(user))
            {
                args.Append("--user ");
                args.Append(WslToken(user.Trim()));
                args.Append(" ");
            }

            args.Append("--exec");
            foreach (string part in commandParts)
            {
                if (!String.IsNullOrWhiteSpace(part))
                {
                    args.Append(" ");
                    args.Append(WslToken(part.Trim()));
                }
            }
            return args.ToString();
        }

        private static string WslToken(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.IndexOfAny(new char[] { ' ', '\t', '"' }) < 0)
            {
                return trimmed;
            }
            return QuoteArgument(trimmed);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static ServiceState ClassifyStatus(int exitCode, string output)
        {
            string text = (output ?? "").ToLowerInvariant();
            if (ContainsAny(text, "service is not installed", "not installed"))
            {
                return ServiceState.NotInstalled;
            }
            if (ContainsAny(text, "failed to start", "not recognized", "no such file", "not found", "failed to connect to bus", "systemd user session not available", "operation not permitted"))
            {
                return ServiceState.Unavailable;
            }
            if (ContainsAny(text, "inactive", "dead", "not running", "stopped", "could not be found"))
            {
                return ServiceState.Stopped;
            }
            if (exitCode == 0 && ContainsAny(text, "status:    running", "active (running)", "is running", "running", "active:"))
            {
                return ServiceState.Running;
            }
            if (exitCode == 0)
            {
                return ServiceState.Running;
            }
            return ServiceState.Unknown;
        }

        private static bool ContainsAny(string text, params string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (text.Contains(token))
                {
                    return true;
                }
            }
            return false;
        }

        private string StateLabel(ServiceState value)
        {
            switch (value)
            {
                case ServiceState.Running:
                    return T("Running");
                case ServiceState.Stopped:
                    return T("Stopped");
                case ServiceState.NotInstalled:
                    return T("NotInstalled");
                case ServiceState.Unavailable:
                    return T("WslUnavailable");
                case ServiceState.Unknown:
                    return T("Unknown");
                default:
                    return T("Checking");
            }
        }

        private string T(string key)
        {
            switch (languageCode)
            {
                case "ZH":
                    return TextZh(key);
                case "KO":
                    return TextKo(key);
                case "JA":
                    return TextJa(key);
                case "FR":
                    return TextFr(key);
                default:
                    return TextEn(key);
            }
        }

        private static string TextEn(string key)
        {
            switch (key)
            {
                case "Status": return "Status";
                case "UnconfiguredUser": return "User not configured";
                case "ConfigureUser": return "Configure user";
                case "Language": return "Language";
                case "KeepRunning": return "Keep running";
                case "StartWithWindows": return "Start with Win";
                case "StopOnExit": return "Stop on exit";
                case "InstallDaemon": return "Install daemon";
                case "Start": return "Start";
                case "Restart": return "Restart";
                case "Stop": return "Stop";
                case "RefreshStatus": return "Refresh";
                case "Exit": return "Exit";
                case "Current": return "Current";
                case "NoWslUser": return "No WSL user found";
                case "DetectFailed": return "Detect failed";
                case "Redetect": return "Detect again";
                case "ClearConfig": return "Clear config";
                case "Default": return "Default";
                case "Processing": return "processing";
                case "Refreshing": return "refreshing";
                case "BusySeparator": return ", ";
                case "Running": return "Running";
                case "Stopped": return "Stopped";
                case "NotInstalled": return "Not installed";
                case "WslUnavailable": return "WSL unavailable";
                case "Unknown": return "Unknown";
                case "Checking": return "Checking";
                case "AutoStart": return "Auto start";
                case "Install": return "Install";
                case "FailureSuffix": return " failed";
                case "ErrorHelp": return "Check cc-connect daemon status in WSL.";
                default: return key;
            }
        }

        private static string TextZh(string key)
        {
            switch (key)
            {
                case "Status": return "状态";
                case "UnconfiguredUser": return "未配置用户";
                case "ConfigureUser": return "配置用户";
                case "Language": return "语言";
                case "KeepRunning": return "保持运行";
                case "StartWithWindows": return "开机自启";
                case "StopOnExit": return "退出后停止服务";
                case "InstallDaemon": return "安装 daemon";
                case "Start": return "启动";
                case "Restart": return "重启";
                case "Stop": return "停止";
                case "RefreshStatus": return "刷新状态";
                case "Exit": return "退出";
                case "Current": return "当前";
                case "NoWslUser": return "未检测到 WSL 用户";
                case "DetectFailed": return "检测失败";
                case "Redetect": return "重新检测";
                case "ClearConfig": return "清空配置";
                case "Default": return "默认";
                case "Processing": return "处理中";
                case "Refreshing": return "刷新中";
                case "BusySeparator": return "，";
                case "Running": return "运行中";
                case "Stopped": return "已停止";
                case "NotInstalled": return "未安装";
                case "WslUnavailable": return "WSL 不可用";
                case "Unknown": return "未知";
                case "Checking": return "检查中";
                case "AutoStart": return "自动启动";
                case "Install": return "安装";
                case "FailureSuffix": return "失败";
                case "ErrorHelp": return "查看 WSL 中的 cc-connect daemon 状态。";
                default: return key;
            }
        }

        private static string TextKo(string key)
        {
            switch (key)
            {
                case "Status": return "상태";
                case "UnconfiguredUser": return "사용자 미설정";
                case "ConfigureUser": return "사용자 설정";
                case "Language": return "언어";
                case "KeepRunning": return "계속 실행";
                case "StartWithWindows": return "시작 시 실행";
                case "StopOnExit": return "종료 시 중지";
                case "InstallDaemon": return "daemon 설치";
                case "Start": return "시작";
                case "Restart": return "재시작";
                case "Stop": return "중지";
                case "RefreshStatus": return "새로고침";
                case "Exit": return "종료";
                case "Current": return "현재";
                case "NoWslUser": return "WSL 사용자를 찾지 못함";
                case "DetectFailed": return "감지 실패";
                case "Redetect": return "다시 감지";
                case "ClearConfig": return "설정 지우기";
                case "Default": return "기본";
                case "Processing": return "처리 중";
                case "Refreshing": return "새로고침 중";
                case "BusySeparator": return ", ";
                case "Running": return "실행 중";
                case "Stopped": return "중지됨";
                case "NotInstalled": return "설치되지 않음";
                case "WslUnavailable": return "WSL 사용 불가";
                case "Unknown": return "알 수 없음";
                case "Checking": return "확인 중";
                case "AutoStart": return "자동 시작";
                case "Install": return "설치";
                case "FailureSuffix": return " 실패";
                case "ErrorHelp": return "WSL에서 cc-connect daemon 상태를 확인하세요.";
                default: return key;
            }
        }

        private static string TextJa(string key)
        {
            switch (key)
            {
                case "Status": return "状態";
                case "UnconfiguredUser": return "ユーザー未設定";
                case "ConfigureUser": return "ユーザー設定";
                case "Language": return "言語";
                case "KeepRunning": return "実行を維持";
                case "StartWithWindows": return "起動時に開始";
                case "StopOnExit": return "終了時に停止";
                case "InstallDaemon": return "daemon をインストール";
                case "Start": return "開始";
                case "Restart": return "再起動";
                case "Stop": return "停止";
                case "RefreshStatus": return "更新";
                case "Exit": return "終了";
                case "Current": return "現在";
                case "NoWslUser": return "WSL ユーザー未検出";
                case "DetectFailed": return "検出失敗";
                case "Redetect": return "再検出";
                case "ClearConfig": return "設定をクリア";
                case "Default": return "既定";
                case "Processing": return "処理中";
                case "Refreshing": return "更新中";
                case "BusySeparator": return "、";
                case "Running": return "実行中";
                case "Stopped": return "停止中";
                case "NotInstalled": return "未インストール";
                case "WslUnavailable": return "WSL 利用不可";
                case "Unknown": return "不明";
                case "Checking": return "確認中";
                case "AutoStart": return "自動開始";
                case "Install": return "インストール";
                case "FailureSuffix": return "失敗";
                case "ErrorHelp": return "WSL の cc-connect daemon 状態を確認してください。";
                default: return key;
            }
        }

        private static string TextFr(string key)
        {
            switch (key)
            {
                case "Status": return "État";
                case "UnconfiguredUser": return "Utilisateur non configuré";
                case "ConfigureUser": return "Config utilisateur";
                case "Language": return "Langue";
                case "KeepRunning": return "Garder actif";
                case "StartWithWindows": return "Au démarrage";
                case "StopOnExit": return "Stop à la sortie";
                case "InstallDaemon": return "Installer daemon";
                case "Start": return "Démarrer";
                case "Restart": return "Redémarrer";
                case "Stop": return "Arrêter";
                case "RefreshStatus": return "Actualiser";
                case "Exit": return "Quitter";
                case "Current": return "Actuel";
                case "NoWslUser": return "Aucun utilisateur WSL";
                case "DetectFailed": return "Détection échouée";
                case "Redetect": return "Redétecter";
                case "ClearConfig": return "Effacer config";
                case "Default": return "Défaut";
                case "Processing": return "traitement";
                case "Refreshing": return "actualisation";
                case "BusySeparator": return ", ";
                case "Running": return "En cours";
                case "Stopped": return "Arrêté";
                case "NotInstalled": return "Non installé";
                case "WslUnavailable": return "WSL indisponible";
                case "Unknown": return "Inconnu";
                case "Checking": return "Vérification";
                case "AutoStart": return "Démarrage auto";
                case "Install": return "Installation";
                case "FailureSuffix": return " échoué";
                case "ErrorHelp": return "Vérifiez l'état du daemon cc-connect dans WSL.";
                default: return key;
            }
        }

        private static string ShortTooltip(string label)
        {
            string text = "CC-Tray: " + label;
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private static string NormalizeLanguage(string value)
        {
            string code = String.IsNullOrWhiteSpace(value) ? "EN" : value.Trim().ToUpperInvariant();
            foreach (LanguageChoice choice in LanguageChoices)
            {
                if (String.Equals(choice.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return choice.Code;
                }
            }
            return "EN";
        }

        private void ShowError(string title, string detail)
        {
            trayIcon.BalloonTipTitle = title + T("FailureSuffix");
            trayIcon.BalloonTipText = String.IsNullOrWhiteSpace(detail) ? T("ErrorHelp") : detail;
            trayIcon.BalloonTipIcon = ToolTipIcon.Error;
            trayIcon.ShowBalloonTip(5000);
        }

        private static string LastUsefulLine(string text)
        {
            string[] lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int index = lines.Length - 1; index >= 0; index--)
            {
                string line = lines[index].Trim();
                if (line.Length > 0)
                {
                    return line.Length > 180 ? line.Substring(0, 180) : line;
                }
            }
            return "";
        }

        private static Icon LoadTrayIcon()
        {
            Icon pngIcon = LoadPngTrayIcon();
            if (pngIcon != null)
            {
                return pngIcon;
            }

            string looseIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.ico");
            if (File.Exists(looseIcon))
            {
                return new Icon(looseIcon);
            }

            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        private static Icon LoadPngTrayIcon()
        {
            Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("CCTray.logo.png");
            if (resource != null)
            {
                using (resource)
                {
                    return BuildIconFromPng(resource);
                }
            }

            string loosePng = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            if (File.Exists(loosePng))
            {
                using (FileStream stream = File.OpenRead(loosePng))
                {
                    return BuildIconFromPng(stream);
                }
            }
            return null;
        }

        private static Icon BuildIconFromPng(Stream stream)
        {
            using (Bitmap source = new Bitmap(stream))
            using (Bitmap iconBitmap = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (Graphics graphics = Graphics.FromImage(iconBitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, iconBitmap.Width, iconBitmap.Height));

                IntPtr handle = iconBitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static string EnvironmentValue(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string ResolveWslExe()
        {
            string overridePath = Environment.GetEnvironmentVariable("CC_CONNECT_WSL_EXE");
            if (!String.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath.Trim();
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (String.IsNullOrWhiteSpace(windowsDirectory))
            {
                windowsDirectory = EnvironmentValue("WINDIR", @"C:\Windows");
            }

            string nativePath = Path.Combine(windowsDirectory, Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32", "wsl.exe");
            if (File.Exists(nativePath))
            {
                return nativePath;
            }

            string system32Path = Path.Combine(windowsDirectory, "System32", "wsl.exe");
            if (File.Exists(system32Path))
            {
                return system32Path;
            }

            return "wsl.exe";
        }

        private static bool ReadBoolSetting(string name, bool fallback)
        {
            using (RegistryKey key = OpenSettingsKeyForRead())
            {
                object value = key == null ? null : key.GetValue(name);
                if (value == null)
                {
                    return fallback;
                }
                return Convert.ToInt32(value) != 0;
            }
        }

        private static void WriteBoolSetting(string name, bool value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsPath))
            {
                key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        private static string ReadStringSetting(string name, string fallback)
        {
            using (RegistryKey key = OpenSettingsKeyForRead())
            {
                object value = key == null ? null : key.GetValue(name);
                string text = value as string;
                return String.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
            }
        }

        private static void WriteStringSetting(string name, string value)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsPath))
            {
                key.SetValue(name, value ?? "", RegistryValueKind.String);
            }
        }

        private static void UpdateStartupRegistration(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (enabled)
                {
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                    key.DeleteValue(LegacyRunValueName, false);
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                    key.DeleteValue(LegacyRunValueName, false);
                }
            }
        }

        private static RegistryKey OpenSettingsKeyForRead()
        {
            RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsPath);
            if (key != null)
            {
                return key;
            }
            return Registry.CurrentUser.OpenSubKey(LegacySettingsPath);
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            statusTimer.Stop();
            trayIcon.Visible = false;
            if (stopOnExit && IsUserConfigured())
            {
                RunWslForConfig(configuredDistro, configuredUser, "cc-connect daemon stop");
            }
            statusTimer.Dispose();
            trayIcon.Dispose();
            menu.Dispose();
            userMenu.Dispose();
            languageMenu.Dispose();
            dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (Mutex mutex = new Mutex(true, @"Local\CC-Tray", out firstInstance))
            {
                if (!firstInstance)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }
}
