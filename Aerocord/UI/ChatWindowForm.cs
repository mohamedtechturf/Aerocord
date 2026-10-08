using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Aerocord.Core;

namespace Aerocord.UI
{
    public class ChatWindowForm : Form
    {
        private const string NudgeMarker = "\uD83D\uDD14 sends a nudge!";

        private readonly DiscordChannel _channel;
        private Panel _headerPanel;
        private RichTextBox _transcript;
        private TextBox _inputBox;
        private Button _sendButton;
        private Button _nudgeButton;
        private Label _typingLabel;

        private DateTime _lastTypingSent = DateTime.MinValue;
        private Timer _typingClearTimer;

        private AppState App { get { return AppState.Current; } }

        public ChatWindowForm(DiscordChannel channel)
        {
            _channel = channel;
            BuildUi();
            Load += ChatWindowForm_Load;
            App.TypingSeen += OnTypingSeen;
            FormClosed += (s, e) => App.TypingSeen -= OnTypingSeen;
        }

        private void BuildUi()
        {
            Text = _channel.DisplayName + " - Conversation";
            ClientSize = new Size(420, 460);
            MinimumSize = new Size(320, 300);
            StartPosition = FormStartPosition.CenterParent;
            MsnTheme.StyleWindow(this);
            Icon = IconFactory.AppIcon;

            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 40 };
            _headerPanel.Paint += (s, e) =>
            {
                MsnTheme.DrawVerticalGradient(e.Graphics, _headerPanel.ClientRectangle, MsnTheme.HeaderTop, MsnTheme.HeaderBottom);
                using (var b = new SolidBrush(Color.White))
                    e.Graphics.DrawString(_channel.DisplayName, MsnTheme.FontBold, b, new PointF(10, 10));
            };
            Controls.Add(_headerPanel);

            _typingLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 18,
                Text = "",
                ForeColor = Color.DimGray,
                Font = MsnTheme.FontSmall,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            Controls.Add(_typingLabel);

            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 110 };
            _inputBox = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Top,
                Height = 64,
                Font = MsnTheme.FontNormal,
                AcceptsReturn = true
            };
            _inputBox.KeyDown += InputBox_KeyDown;
            _inputBox.TextChanged += InputBox_TextChanged;
            bottomPanel.Controls.Add(_inputBox);

            var buttonRow = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            _sendButton = new Button { Text = "Send", Size = new Size(90, 30), Location = new Point(bottomPanel.Width - 100, 5), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            MsnTheme.StyleFlatButton(_sendButton);
            _sendButton.Click += async (s, e) => await SendCurrentInputAsync();

            _nudgeButton = new Button { Text = "Nudge!", Size = new Size(90, 30), Location = new Point(bottomPanel.Width - 200, 5), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            MsnTheme.StyleFlatButton(_nudgeButton);
            _nudgeButton.Click += async (s, e) => await SendNudgeAsync();

            buttonRow.Controls.Add(_sendButton);
            buttonRow.Controls.Add(_nudgeButton);
            bottomPanel.Controls.Add(buttonRow);
            Controls.Add(bottomPanel);
            bottomPanel.BringToFront();

            _transcript = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = MsnTheme.FontNormal
            };
            Controls.Add(_transcript);
            _transcript.BringToFront();

            bottomPanel.Resize += (s, e) =>
            {
                _sendButton.Location = new Point(bottomPanel.Width - 100, 5);
                _nudgeButton.Location = new Point(bottomPanel.Width - 200, 5);
            };
        }

        private async void ChatWindowForm_Load(object sender, EventArgs e)
        {
            _inputBox.Focus();
            try
            {
                var history = await App.Rest.GetMessagesAsync(_channel.Id, 50);
                foreach (var m in history)
                    AppendMessage(m, m.IsFromSelf || (App.Me != null && m.Author != null && m.Author.Id == App.Me.Id));
            }
            catch (Exception ex)
            {
                AppendSystemLine("Couldn't load history: " + ex.Message);
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                _ = SendCurrentInputAsync();
            }
        }

        private async void InputBox_TextChanged(object sender, EventArgs e)
        {
            if ((DateTime.UtcNow - _lastTypingSent).TotalSeconds < 4) return;
            _lastTypingSent = DateTime.UtcNow;
            try { await App.Rest.SendTypingAsync(_channel.Id); } catch { /* non-critical */ }
        }

        private async Task SendCurrentInputAsync()
        {
            string text = _inputBox.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;
            _inputBox.Clear();
            try
            {
                var sent = await App.Rest.SendMessageAsync(_channel.Id, text);
                sent.IsFromSelf = true;
                AppendMessage(sent, true);
            }
            catch (Exception ex)
            {
                AppendSystemLine("Failed to send: " + ex.Message);
            }
        }

        private async Task SendNudgeAsync()
        {
            Shake();
            System.Media.SystemSounds.Exclamation.Play();
            try { await App.Rest.SendMessageAsync(_channel.Id, NudgeMarker); }
            catch { /* local shake still happened, don't block on network failure */ }
        }

        /// <summary>Called by MainForm when a realtime message for this channel arrives.</summary>
        public void AppendIncoming(DiscordMessage msg)
        {
            if (msg.Content != null && msg.Content.Contains(NudgeMarker))
            {
                Shake();
                System.Media.SystemSounds.Exclamation.Play();
                AppendSystemLine((msg.Author != null ? msg.Author.DisplayName : "Someone") + " sent you a nudge!");
                return;
            }
            AppendMessage(msg, false);
            if (WindowState == FormWindowState.Minimized || !ContainsFocus)
                System.Media.SystemSounds.Asterisk.Play();
        }

        private void AppendMessage(DiscordMessage msg, bool isSelf)
        {
            string who = isSelf ? "You" : (msg.Author != null ? msg.Author.DisplayName : "Unknown");
            Color nameColor = isSelf ? Color.FromArgb(0x0B, 0x6E, 0x0B) : MsnTheme.HeaderBottom;

            _transcript.SelectionStart = _transcript.TextLength;
            _transcript.SelectionColor = Color.Gray;
            _transcript.SelectionFont = MsnTheme.FontSmall;
            _transcript.AppendText(msg.Timestamp == default(DateTime) ? DateTime.Now.ToString("t") : msg.Timestamp.ToLocalTime().ToString("t"));
            _transcript.AppendText("  ");

            _transcript.SelectionColor = nameColor;
            _transcript.SelectionFont = MsnTheme.FontBold;
            _transcript.AppendText(who + ":\n");

            _transcript.SelectionColor = Color.Black;
            _transcript.SelectionFont = MsnTheme.FontNormal;
            _transcript.AppendText(msg.Content + "\n\n");

            _transcript.SelectionStart = _transcript.TextLength;
            _transcript.ScrollToCaret();
        }

        private void AppendSystemLine(string text)
        {
            _transcript.SelectionStart = _transcript.TextLength;
            _transcript.SelectionColor = Color.DarkOrange;
            _transcript.SelectionFont = MsnTheme.FontSmall;
            _transcript.AppendText(text + "\n\n");
            _transcript.ScrollToCaret();
        }

        private void OnTypingSeen(string channelId, string userId)
        {
            if (channelId != _channel.Id) return;
            if (App.Me != null && userId == App.Me.Id) return;
            if (InvokeRequired) { BeginInvoke((Action)(() => OnTypingSeen(channelId, userId))); return; }

            _typingLabel.Text = _channel.DisplayName + " is typing...";
            if (_typingClearTimer == null)
            {
                _typingClearTimer = new Timer { Interval = 4000 };
                _typingClearTimer.Tick += (s, e) => { _typingLabel.Text = ""; _typingClearTimer.Stop(); };
            }
            _typingClearTimer.Stop();
            _typingClearTimer.Start();
        }

        /// <summary>Classic MSN "nudge" window-shake effect.</summary>
        private void Shake()
        {
            var original = Location;
            var timer = new Timer { Interval = 30 };
            int count = 0;
            var rnd = new Random();
            timer.Tick += (s, e) =>
            {
                count++;
                if (count > 12)
                {
                    timer.Stop();
                    timer.Dispose();
                    Location = original;
                    return;
                }
                Location = new Point(original.X + rnd.Next(-8, 9), original.Y + rnd.Next(-8, 9));
            };
            timer.Start();
        }
    }
}
