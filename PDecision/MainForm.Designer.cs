namespace PDecision
{
    partial class DecisionUI
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DecisionUI));
            statusStrip1 = new StatusStrip();
            toolStripStatusLabel1 = new ToolStripStatusLabel();
            toolbar = new ToolStrip();
            toolStripSeparator1 = new ToolStripSeparator();
            btnReport = new ToolStripButton();
            txtActivity = new TextBox();
            statusStrip1.SuspendLayout();
            toolbar.SuspendLayout();
            SuspendLayout();
            //
            // statusStrip1
            //
            statusStrip1.ImageScalingSize = new Size(24, 24);
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel1 });
            statusStrip1.Dock = DockStyle.Bottom;
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Padding = new Padding(1, 0, 16, 0);
            statusStrip1.Size = new Size(760, 26);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            //
            // toolStripStatusLabel1
            //
            toolStripStatusLabel1.Name = "toolStripStatusLabel1";
            toolStripStatusLabel1.Size = new Size(151, 20);
            toolStripStatusLabel1.Text = "toolStripStatusLabel1";
            //
            // toolbar
            //
            toolbar.AutoSize = false;
            toolbar.Dock = DockStyle.Top;
            toolbar.ImageScalingSize = new Size(30, 30);
            toolbar.Items.AddRange(new ToolStripItem[] { toolStripSeparator1, btnReport });
            toolbar.Location = new Point(0, 0);
            toolbar.Name = "toolbar";
            toolbar.Padding = new Padding(0, 0, 2, 0);
            toolbar.Size = new Size(760, 35);
            toolbar.TabIndex = 167;
            //
            // toolStripSeparator1
            //
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 35);
            //
            // btnReport
            //
            btnReport.BackgroundImageLayout = ImageLayout.Stretch;
            btnReport.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnReport.Image = (Image)resources.GetObject("btnReport.Image");
            btnReport.ImageTransparentColor = Color.Magenta;
            btnReport.Name = "btnReport";
            btnReport.Size = new Size(34, 32);
            btnReport.Text = "Report";
            btnReport.Click += btnReport_Click;
            //
            // txtActivity
            //
            txtActivity.BackColor = Color.Black;
            txtActivity.Dock = DockStyle.Fill;
            txtActivity.Font = new Font("Consolas", 9F);
            txtActivity.ForeColor = Color.Lime;
            txtActivity.Location = new Point(0, 35);
            txtActivity.Multiline = true;
            txtActivity.Name = "txtActivity";
            txtActivity.ReadOnly = true;
            txtActivity.ScrollBars = ScrollBars.Vertical;
            txtActivity.Size = new Size(760, 270);
            txtActivity.TabIndex = 168;
            //
            // DecisionUI
            //
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 331);
            Controls.Add(txtActivity);
            Controls.Add(toolbar);
            Controls.Add(statusStrip1);
            Margin = new Padding(3, 4, 3, 4);
            MinimumSize = new Size(500, 300);
            Name = "DecisionUI";
            Text = "DecisionUI";
            FormClosed += MainForm_FormClosed;
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            toolbar.ResumeLayout(false);
            toolbar.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private StatusStrip statusStrip1;
        private ToolStripStatusLabel toolStripStatusLabel1;
        private ToolStrip toolbar;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripButton btnReport;
        private TextBox txtActivity;

    }
}
