using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Aerocord.Core;

namespace Aerocord.UI
{
    public class MainForm : Form
    {
        private Panel _headerPanel;
        private ComboBox _statusCombo;
        private TextBox _customStatusBox;
        private Panel _listPanel;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusStripLabel;

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private bool _reallyExiting;

        private readonly Dictionary<string, ChatWindowForm> _openWindows = new Dictionary<string, ChatWindowForm>();
        private readonly Dictionary<string, ContactTileControl> _friendTiles = new Dictionary<string, ContactTileControl>();

        private AppState App { get { return AppState.Current; } }

        public MainForm()
        {
            BuildUi();
            BuildTrayIcon();
            HookAppEvents();
            Load += (s, e) => RebuildBuddyList();
        }

        #region UI construction

        private void BuildUi()
        {
            Text = "Aerocord";
            ClientSize = new Size(300, 520);
            MinimumSize = new Size(260, 360);
            StartPosition = FormStartPosition.CenterScreen;
            MsnTheme.StyleWindow(this);
            Icon = IconFactory.AppIcon;

            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 96 };
            _headerPanel.Paint += HeaderPanel_Paint;
            Controls.Add(_headerPanel);

            _statusCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(10, 58),
                Width = 140,
                Font = MsnTheme.FontSmall
            };
            _statusCombo.Items.AddRange(new object[] { "Online", "Away", "Busy", "Appear Offline" });
            _statusCombo.SelectedIndex = 0;
            _statusCombo.SelectedIndexChanged += StatusCombo_SelectedIndexChanged;
            _headerPanel.Controls.Add(_statusCombo);

            _customStatusBox = new TextBox
            {
                Location = new Point(156, 59),
                Width = 132,
                Font = MsnTheme.FontSmall,
                Text = "Type a personal message"
            };
            _customStatusBox.GotFocus += (s, e) =>
            {
                if (_customStatusBox.Text == "Type a personal message") _customStatusBox.Text = "";
            };
            _customStatusBox.KeyDown += async (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    App.MyCustomStatus = _customStatusBox.Text;
                    await App.Gateway.SendPresenceUpdateAsync(App.MyStatus, App.MyCustomStatus);
                }
            };
            _headerPanel.Controls.Add(_customStatusBox);

            var scrollHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = MsnTheme.PanelBackground };
            _listPanel = new Panel { Location = new Point(0, 0), Width = scrollHost.ClientSize.Width, AutoSize = false, BackColor = MsnTheme.PanelBackground };
            scrollHost.Controls.Add(_listPanel);
            scrollHost.Resize += (s, e) => { _listPanel.Width = scrollHost.ClientSize.Width - (scrollHost.VerticalScroll.Visible ? 18 : 2); RebuildBuddyList(); };
            Controls.Add(scrollHost);
            scrollHost.BringToFront();

            _statusStrip = new StatusStrip();
            _statusStripLabel = new ToolStripStatusLabel("Not connected");
            _statusStrip.Items.Add(_statusStripLabel);
            Controls.Add(_statusStrip);

            var menu = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("File");
            var minimizeItem = new ToolStripMenuItem("Minimize to Tray", null, (s, e) => HideToTray());
            var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());
            fileMenu.DropDownItems.Add(minimizeItem);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(exitItem);
            menu.Items.Add(fileMenu);
            MainMenuStrip = menu;
            Controls.Add(menu);
            menu.BringToFront();

            FormClosing += MainForm_FormClosing;
            Resize += MainForm_Resize;
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            MsnTheme.DrawVerticalGradient(g, _headerPanel.ClientRectangle, MsnTheme.HeaderTop, MsnTheme.HeaderBottom);

            var avatarRect = new Rectangle(10, 8, 40, 40);
            using (var avBrush = new SolidBrush(Color.FromArgb(0xE0, 0xEC, 0xFB)))
                g.FillEllipse(avBrush, avatarRect);
            string initials = App != null && App.Me != null && !string.IsNullOrEmpty(App.Me.DisplayName)
                ? App.Me.DisplayName.Substring(0, 1).ToUpperInvariant() : "?";
            using (var f = MsnTheme.FontTitle)
            using (var tb = new SolidBrush(MsnTheme.HeaderBottom))
                g.DrawString(initials, f, tb, avatarRect.X + 10, avatarRect.Y + 4);

            string name = App != null && App.Me != null ? App.Me.DisplayName : "Signing in...";
            using (var nameBrush = new SolidBrush(Color.White))
                g.DrawString(name, MsnTheme.FontBold, nameBrush, new PointF(58, 12));
        }

        #endregion

        #region Tray

        private void BuildTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Open Aerocord", null, (s, e) => RestoreFromTray());
            _trayMenu.Items.Add(new ToolStripSeparator());

            var statusMenu = new ToolStripMenuItem("Set Status");
            statusMenu.DropDownItems.Add("Online", null, async (s, e) => await SetStatusAsync(PresenceStatus.Online));
            statusMenu.DropDownItems.Add("Away", null, async (s, e) => await SetStatusAsync(PresenceStatus.Idle));
            statusMenu.DropDownItems.Add("Busy", null, async (s, e) => await SetStatusAsync(PresenceStatus.DoNotDisturb));
            statusMenu.DropDownItems.Add("Appear Offline", null, async (s, e) => await SetStatusAsync(PresenceStatus.Invisible));
            _trayMenu.Items.Add(statusMenu);

            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon
            {
                Icon = IconFactory.TrayIcon,
                Text = "Aerocord",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        }

        private async System.Threading.Tasks.Task SetStatusAsync(PresenceStatus status)
        {
            App.MyStatus = status;
            _statusCombo.SelectedIndexChanged -= StatusCombo_SelectedIndexChanged;
            _statusCombo.SelectedIndex = StatusToComboIndex(status);
            _statusCombo.SelectedIndexChanged += StatusCombo_SelectedIndexChanged;
            await App.Gateway.SendPresenceUpdateAsync(status, App.MyCustomStatus);
        }

        private static int StatusToComboIndex(PresenceStatus s)
        {
            switch (s)
            {
                case PresenceStatus.Online: return 0;
                case PresenceStatus.Idle: return 1;
                case PresenceStatus.DoNotDisturb: return 2;
                default: return 3;
            }
        }

        private async void StatusCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            PresenceStatus status;
            switch (_statusCombo.SelectedIndex)
            {
                case 0: status = PresenceStatus.Online; break;
                case 1: status = PresenceStatus.Idle; break;
                case 2: status = PresenceStatus.DoNotDisturb; break;
                default: status = PresenceStatus.Invisible; break;
            }
            App.MyStatus = status;
            await App.Gateway.SendPresenceUpdateAsync(status, App.MyCustomStatus);
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
                HideToTray();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_reallyExiting) return;
            e.Cancel = true;
            HideToTray();
            _trayIcon.ShowBalloonTip(1500, "Aerocord", "Still running in the background.", ToolTipIcon.Info);
        }

        private void ExitApplication()
        {
            _reallyExiting = true;
            _trayIcon.Visible = false;
            foreach (var w in _openWindows.Values.ToList()) w.Close();
            Application.Exit();
        }

        #endregion

        #region App event wiring

        private void HookAppEvents()
        {
            if (App == null) return;
            App.MessageArrived += OnMessageArrived;
            App.PresenceChanged += OnPresenceChanged;
            App.Disconnected += OnDisconnected;
            _statusStripLabel.Text = "Connected as " + (App.Me != null ? App.Me.DisplayName : "?");
        }

        private void OnDisconnected()
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() =>
            {
                _statusStripLabel.Text = "Disconnected";
                _trayIcon.ShowBalloonTip(2000, "Aerocord", "Connection lost.", ToolTipIcon.Warning);
            }));
        }

        private void OnPresenceChanged(string userId, PresenceStatus status)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() =>
            {
                ContactTileControl tile;
                if (_friendTiles.TryGetValue(userId, out tile))
                    tile.SetStatus(status);
            }));
        }

        private void OnMessageArrived(DiscordMessage msg)
        {
            if (IsDisposed) return;
            BeginInvoke((Action)(() =>
            {
                ChatWindowForm win;
                if (_openWindows.TryGetValue(msg.ChannelId, out win) && !win.IsDisposed)
                {
                    win.AppendIncoming(msg);
                    return;
                }
                if (msg.IsFromSelf) return;

                string who = msg.Author != null ? msg.Author.DisplayName : "Someone";
                System.Media.SystemSounds.Asterisk.Play();
                _trayIcon.ShowBalloonTip(3000, who, Truncate(msg.Content, 100), ToolTipIcon.Info);
            }));
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "(no text content)";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        #endregion

        #region Buddy list

        private void RebuildBuddyList()
        {
            if (App == null) return;
            _listPanel.SuspendLayout();
            _listPanel.Controls.Clear();
            _friendTiles.Clear();
            int y = 0;

            // Friends group
            var friendsHeader = new GroupHeaderControl("Friends (" + App.Friends.Count + ")");
            friendsHeader.ExpandedChanged += (s, e) => RebuildBuddyList();
            PlaceControl(friendsHeader, ref y);
            if (friendsHeader.Expanded)
            {
                foreach (var rel in App.Friends.OrderBy(r => r.User != null ? r.User.DisplayName : ""))
                {
                    if (rel.User == null) continue;
                    PresenceStatus status;
                    App.PresenceByUserId.TryGetValue(rel.User.Id, out status);
                    var tile = new ContactTileControl(rel.User.DisplayName, MsnTheme.StatusLabel(status))
                    {
                        EntityId = rel.User.Id,
                        Width = _listPanel.Width
                    };
                    tile.SetStatus(status);
                    tile.Click += async (s, e) => await OpenDmAsync(rel.User);
                    PlaceControl(tile, ref y);
                    _friendTiles[rel.User.Id] = tile;
                }
            }

            // Group DMs
            var groupDms = App.PrivateChannels.Where(c => c.Kind == ChannelKind.GroupDirectMessage).ToList();
            if (groupDms.Count > 0)
            {
                var gh = new GroupHeaderControl("Group Chats (" + groupDms.Count + ")");
                gh.ExpandedChanged += (s, e) => RebuildBuddyList();
                PlaceControl(gh, ref y);
                if (gh.Expanded)
                {
                    foreach (var c in groupDms)
                    {
                        var tile = new ContactTileControl(c.DisplayName, "Group conversation") { EntityId = c.Id, Width = _listPanel.Width };
                        tile.Click += (s, e) => OpenChannelChat(c);
                        PlaceControl(tile, ref y);
                    }
                }
            }

            // Servers
            foreach (var guild in App.Guilds.OrderBy(g => g.Name))
            {
                var gh = new GroupHeaderControl(guild.Name);
                gh.ExpandedChanged += (s, e) => RebuildBuddyList();
                PlaceControl(gh, ref y);
                if (!gh.Expanded) continue;
                foreach (var ch in guild.Channels
                    .Where(c => c.Kind == ChannelKind.GuildText || c.Kind == ChannelKind.GuildVoice)
                    .OrderBy(c => c.Position))
                {
                    string prefix = ch.Kind == ChannelKind.GuildVoice ? "\uD83D\uDD0A " : "# ";
                    var tile = new ContactTileControl(prefix + ch.Name, ch.Kind == ChannelKind.GuildVoice ? "Voice channel" : "Text channel")
                    {
                        EntityId = ch.Id,
                        Width = _listPanel.Width
                    };
                    tile.Click += async (s, e) =>
                    {
                        if (ch.Kind == ChannelKind.GuildVoice) await TryJoinVoiceAsync(guild, ch);
                        else OpenChannelChat(ch);
                    };
                    PlaceControl(tile, ref y);
                }
            }

            _listPanel.Height = Math.Max(y, 1);
            _listPanel.ResumeLayout();
        }

        private void PlaceControl(Control c, ref int y)
        {
            c.Left = 0;
            c.Top = y;
            c.Width = _listPanel.Width;
            _listPanel.Controls.Add(c);
            y += c.Height;
        }

        private async System.Threading.Tasks.Task OpenDmAsync(DiscordUser user)
        {
            var existing = App.PrivateChannels.FirstOrDefault(c =>
                c.Kind == ChannelKind.DirectMessage && c.Recipients.Any(r => r.Id == user.Id));
            DiscordChannel channel = existing;
            if (channel == null)
            {
                channel = await App.Rest.CreateDmAsync(user.Id);
                App.PrivateChannels.Add(channel);
            }
            OpenChannelChat(channel);
        }

        private void OpenChannelChat(DiscordChannel channel)
        {
            ChatWindowForm win;
            if (_openWindows.TryGetValue(channel.Id, out win) && !win.IsDisposed)
            {
                win.Activate();
                return;
            }
            win = new ChatWindowForm(channel);
            win.FormClosed += (s, e) => _openWindows.Remove(channel.Id);
            _openWindows[channel.Id] = win;
            win.Show();
        }

        private async System.Threading.Tasks.Task TryJoinVoiceAsync(DiscordGuild guild, DiscordChannel channel)
        {
            var confirm = MessageBox.Show(this,
                "Join voice channel \"" + channel.Name + "\"?\n\n" +
                "Experimental: this connects and shows you as present in the channel,\n" +
                "but this build does not send or play actual audio.",
                "Join voice (experimental)", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            _statusStripLabel.Text = "Joining voice: " + channel.Name + "...";
            App.Voice.VoiceReady += () => BeginInvoke((Action)(() => _statusStripLabel.Text = "Voice connected: " + channel.Name + " (signaling only, no audio)"));
            App.Voice.VoiceError += err => BeginInvoke((Action)(() => _statusStripLabel.Text = "Voice error: " + err));
            await App.Voice.JoinAsync(guild.Id, channel.Id);
        }

        #endregion
    }
}
