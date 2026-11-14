namespace Controller.tests
{
    partial class DisplayHumans
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DisplayHumans));
            grid = new DataGridView();
            Column1 = new DataGridViewTextBoxColumn();
            Column2 = new DataGridViewTextBoxColumn();
            Column3 = new DataGridViewTextBoxColumn();
            toolbar = new ToolStrip();
            btnDownloads = new ToolStripButton();
            lblFrom = new ToolStripLabel();
            dtpFrom = new ToolStripControlHost(new DateTimePicker());
            lblTo = new ToolStripLabel();
            dtpTo = new ToolStripControlHost(new DateTimePicker());
            cbGameType = new ToolStripComboBox();
            btnLoad = new ToolStripButton();
            ((System.ComponentModel.ISupportInitialize)grid).BeginInit();
            toolbar.SuspendLayout();
            SuspendLayout();
            //
            // grid
            //
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.AddRange(new DataGridViewColumn[] { Column1, Column2, Column3 });
            grid.Dock = DockStyle.Bottom;
            grid.Location = new Point(0, 83);
            grid.Margin = new Padding(5, 6, 5, 6);
            grid.Name = "grid";
            grid.RowHeadersWidth = 51;
            grid.Size = new Size(820, 1211);
            grid.TabIndex = 0;
            //
            // Column1
            //
            Column1.HeaderText = "Name";
            Column1.MinimumWidth = 6;
            Column1.Name = "Column1";
            Column1.Width = 125;
            //
            // Column2
            //
            Column2.HeaderText = "Hands";
            Column2.MinimumWidth = 6;
            Column2.Name = "Column2";
            Column2.Width = 125;
            //
            // Column3
            //
            Column3.HeaderText = "Win/Loss";
            Column3.MinimumWidth = 8;
            Column3.Name = "Column3";
            Column3.Width = 150;
            //
            // toolbar
            //
            toolbar.AutoSize = false;
            toolbar.ImageScalingSize = new Size(30, 30);
            toolbar.Items.AddRange(new ToolStripItem[] { btnDownloads, lblFrom, dtpFrom, lblTo, dtpTo, cbGameType, btnLoad });
            toolbar.Location = new Point(0, 0);
            toolbar.Name = "toolbar";
            toolbar.Size = new Size(820, 52);
            toolbar.TabIndex = 168;
            //
            // btnDownloads
            //
            btnDownloads.AutoSize = false;
            btnDownloads.BackgroundImageLayout = ImageLayout.Stretch;
            btnDownloads.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnDownloads.Image = (Image)resources.GetObject("btnDownloads.Image");
            btnDownloads.ImageTransparentColor = Color.Magenta;
            btnDownloads.Margin = new Padding(5, 1, 0, 2);
            btnDownloads.Name = "btnDownloads";
            btnDownloads.Size = new Size(27, 27);
            btnDownloads.Text = "New Scenario";
            btnDownloads.ToolTipText = "Download hands";
            btnDownloads.Click += btnDownloads_Click;
            //
            // lblFrom
            //
            lblFrom.Name = "lblFrom";
            lblFrom.Size = new Size(40, 49);
            lblFrom.Text = "From:";
            //
            // dtpFrom
            //
            dtpFrom.Name = "dtpFrom";
            dtpFrom.Size = new Size(130, 23);
            ((DateTimePicker)dtpFrom.Control).Format = DateTimePickerFormat.Short;
            //
            // lblTo
            //
            lblTo.Name = "lblTo";
            lblTo.Size = new Size(24, 49);
            lblTo.Text = "To:";
            //
            // dtpTo
            //
            dtpTo.Name = "dtpTo";
            dtpTo.Size = new Size(130, 23);
            ((DateTimePicker)dtpTo.Control).Format = DateTimePickerFormat.Short;
            //
            // cbGameType
            //
            cbGameType.Name = "cbGameType";
            cbGameType.Size = new Size(199, 52);
            cbGameType.SelectedIndexChanged += cbGameType_Click;
            //
            // btnLoad
            //
            btnLoad.BackgroundImageLayout = ImageLayout.Stretch;
            btnLoad.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnLoad.Image = (Image)resources.GetObject("btnLoad.Image");
            btnLoad.ImageTransparentColor = Color.Magenta;
            btnLoad.Name = "btnLoad";
            btnLoad.Size = new Size(34, 47);
            btnLoad.Text = "show data";
            btnLoad.Click += btnLoad_Click;
            //
            // DisplayHumans
            //
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(820, 1294);
            Controls.Add(toolbar);
            Controls.Add(grid);
            Margin = new Padding(5, 6, 5, 6);
            Name = "DisplayHumans";
            Text = "DisplayHumans";
            ((System.ComponentModel.ISupportInitialize)grid).EndInit();
            toolbar.ResumeLayout(false);
            toolbar.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.DataGridView grid;
        private System.Windows.Forms.ToolStrip toolbar;
        private System.Windows.Forms.ToolStripButton btnDownloads;
        private System.Windows.Forms.ToolStripButton btnLoad;
        private ToolStripLabel lblFrom;
        private ToolStripControlHost dtpFrom;
        private ToolStripLabel lblTo;
        private ToolStripControlHost dtpTo;
        private ToolStripComboBox cbGameType;
        private DataGridViewTextBoxColumn Column1;
        private DataGridViewTextBoxColumn Column2;
        private DataGridViewTextBoxColumn Column3;
    }
}
