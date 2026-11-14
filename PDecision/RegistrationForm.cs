namespace PDecision
{
    /// <summary>
    /// Shown at startup when this PC is not registered: lists its MAC addresses so the user can send them to the administrator.
    /// </summary>
    internal class RegistrationForm : Form
    {
        private readonly TextBox txtMacs;

        public RegistrationForm(IReadOnlyList<string> macAddresses)
        {
            Text = "PDecision - PC not registered";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9.75f);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            // rows size themselves to their content, so nothing is cut off at any display scaling
            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
            };

            int textWidth = LogicalToDeviceUnits(440);
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Margin = new Padding(0, 0, 0, 8),
                Font = new Font(Font, FontStyle.Bold),
                Text = "This PC is not registered for PDecision, so the strategy service cannot start.",
            });
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(textWidth, 0),
                Margin = new Padding(0, 0, 0, 12),
                Text = "Send the MAC address below to your administrator to whitelist this PC. " +
                       "Once it is whitelisted, close PDecision and start it again.",
            });
            layout.Controls.Add(new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4),
                Text = macAddresses.Count == 1 ? "MAC address of this PC:" : "MAC addresses of this PC (send all of them):",
            });

            txtMacs = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 11f),
                Text = macAddresses.Count > 0 ? string.Join(Environment.NewLine, macAddresses) : "(no physical network adapter found)",
                Width = textWidth,
                Margin = new Padding(0, 0, 0, 12),
            };
            int lines = Math.Max(1, macAddresses.Count);
            txtMacs.Height = TextRenderer.MeasureText("X", txtMacs.Font).Height * lines + LogicalToDeviceUnits(10);
            layout.Controls.Add(txtMacs);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0),
                WrapContents = false,
            };
            var btnClose = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(84), 0), DialogResult = DialogResult.OK };
            var btnCopy = new Button { Text = "Copy", AutoSize = true, MinimumSize = new Size(LogicalToDeviceUnits(84), 0) };
            btnCopy.Click += (_, _) =>
            {
                Clipboard.SetText(txtMacs.Text);
                btnCopy.Text = "Copied";
            };
            buttons.Controls.Add(btnClose);
            buttons.Controls.Add(btnCopy);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
            AcceptButton = btnClose;
        }
    }
}
