using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Aerocord.Core;

namespace Aerocord.UI
{
    public class LoginForm : Form
    {
        private Panel _headerPanel;
        private TextBox _tokenBox;
        private CheckBox _rememberBox;
        private Button _signInButton;
        private LinkLabel _helpLink;
        private Label _statusLabel;

        public LoginForm()
        {
            BuildUi();
            Load += LoginForm_Load;
        }

        private void BuildUi()
        {
            Text = "Aerocord - Sign In";
            ClientSize = new Size(400, 340);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            MsnTheme.StyleWindow(this);
            Icon = IconFactory.AppIcon;

            _headerPanel = new Panel { Dock = DockStyle.Top, Height = 78 };
            _headerPanel.Paint += HeaderPanel_Paint;
            Controls.Add(_headerPanel);

            var lblToken = new Label
            {
                Text = "Sign-in token:",
                Location = new Point(24, 100),
                AutoSize = true,
                Font = MsnTheme.FontBold
            };
            Controls.Add(lblToken);

            _tokenBox = new TextBox
            {
                Location = new Point(24, 122),
                Width = 352,
                PasswordChar = '*',
                Font = MsnTheme.FontNormal
            };
            Controls.Add(_tokenBox);

            _helpLink = new LinkLabel
            {
                Text = "How do I get my token?",
                Location = new Point(24, 150),
                AutoSize = true,
                Font = MsnTheme.FontSmall,
                LinkColor = MsnTheme.LinkBlue
            };
            _helpLink.LinkClicked += HelpLink_LinkClicked;
            Controls.Add(_helpLink);

            _rememberBox = new CheckBox
            {
                Text = "Remember my sign-in info",
                Location = new Point(24, 182),
                AutoSize = true,
                Font = MsnTheme.FontNormal
            };
            Controls.Add(_rememberBox);

            var disclaimer = new Label
            {
                Text = "Note: signing in with an account token instead of a bot is against\n" +
                       "Discord's Terms of Service and can put your account at risk. Use a\n" +
                       "throwaway/alt account if you're unsure.",
                Location = new Point(24, 212),
                Size = new Size(352, 48),
                Font = MsnTheme.FontSmall,
                ForeColor = Color.FromArgb(0x80, 0x40, 0x00)
            };
            Controls.Add(disclaimer);

            _signInButton = new Button
            {
                Text = "Sign In",
                Location = new Point(220, 268),
                Size = new Size(100, 32)
            };
            MsnTheme.StyleFlatButton(_signInButton);
            _signInButton.Click += SignInButton_Click;
            Controls.Add(_signInButton);

            var exitButton = new Button
            {
                Text = "Cancel",
                Location = new Point(112, 268),
                Size = new Size(100, 32)
            };
            MsnTheme.StyleFlatButton(exitButton);
            exitButton.Click += (s, e) => Application.Exit();
            Controls.Add(exitButton);

            _statusLabel = new Label
            {
                Location = new Point(24, 306),
                Size = new Size(352, 20),
                Font = MsnTheme.FontSmall,
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(_statusLabel);

            AcceptButton = _signInButton;
        }

        private void HeaderPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            MsnTheme.DrawVerticalGradient(g, _headerPanel.ClientRectangle, MsnTheme.HeaderTop, MsnTheme.HeaderBottom);

            // Simple original "chat bubble" mark - not a reproduction of any trademarked logo.
            using (var brush = new SolidBrush(Color.White))
            {
                g.FillEllipse(brush, 18, 16, 44, 34);
                Point[] tail = { new Point(30, 46), new Point(20, 62), new Point(42, 48) };
                g.FillPolygon(brush, tail);
            }
            using (var textBrush = new SolidBrush(Color.White))
            using (var f = MsnTheme.FontTitle)
            {
                g.DrawString("Aerocord", f, textBrush, new PointF(76, 24));
            }
        }

        private void HelpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            MessageBox.Show(this,
                "Your Discord token is a login credential for your account - treat it\n" +
                "like a password and never share it.\n\n" +
                "In Discord (browser or desktop app): open Developer Tools (Ctrl+Shift+I),\n" +
                "go to the Network tab, send any message, click a request to discord.com/api,\n" +
                "and copy the 'authorization' request header value.\n\n" +
                "This app stores it locally (encrypted) only if you check 'Remember me'.",
                "Getting your token",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LoginForm_Load(object sender, EventArgs e)
        {
            string saved = TokenStorage.Load();
            if (!string.IsNullOrEmpty(saved))
            {
                _tokenBox.Text = saved;
                _rememberBox.Checked = true;
            }
        }

        private async void SignInButton_Click(object sender, EventArgs e)
        {
            string token = _tokenBox.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                _statusLabel.Text = "Please enter a token.";
                return;
            }

            SetBusy(true, "Connecting...");
            try
            {
                var app = new AppState(token);

                // Validate the token with a lightweight REST call before opening the gateway,
                // so a bad token fails fast with a clear message.
                DiscordUser me = await app.Rest.GetCurrentUserAsync();
                app.Me = me;

                bool opened = false;
                app.ReadyAndPopulated += () =>
                {
                    if (opened) return;
                    opened = true;
                    BeginInvoke((Action)(() =>
                    {
                        if (_rememberBox.Checked) TokenStorage.Save(token);
                        else TokenStorage.Clear();

                        AppState.Current = app;
                        var main = new MainForm();
                        main.Show();
                        Hide();
                    }));
                };
                app.Gateway.ConnectionError += ex =>
                {
                    BeginInvoke((Action)(() => SetBusy(false, "Connection error: " + ex.Message)));
                };

                SetBusy(true, "Signed in as " + me.DisplayName + ". Opening session...");
                await app.Gateway.ConnectAsync();
            }
            catch (DiscordApiException apiEx)
            {
                SetBusy(false, apiEx.StatusCode == 401
                    ? "Invalid token. Please check it and try again."
                    : "Sign-in failed: " + apiEx.Message);
            }
            catch (Exception ex)
            {
                SetBusy(false, "Sign-in failed: " + ex.Message);
            }
        }

        private void SetBusy(bool busy, string status)
        {
            _signInButton.Enabled = !busy;
            _tokenBox.Enabled = !busy;
            _statusLabel.Text = status;
        }
    }
}
