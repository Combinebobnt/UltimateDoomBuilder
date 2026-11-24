namespace CodeImp.DoomBuilder.Windows
{
	partial class MapStatisticsForm
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
			if(disposing && (components != null))
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
			System.Windows.Forms.DataGridViewCellStyle headerstyle1 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle headerstyle2 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle headerstyle3 = new System.Windows.Forms.DataGridViewCellStyle();
			this.tabs = new System.Windows.Forms.TabControl();
			this.taboverview = new System.Windows.Forms.TabPage();
			this.overviewgrid = new System.Windows.Forms.DataGridView();
			this.OverviewItem = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.OverviewValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.overviewcopy = new System.Windows.Forms.Button();
			this.tabtally = new System.Windows.Forms.TabPage();
			this.modelabel = new System.Windows.Forms.Label();
			this.modes = new System.Windows.Forms.ComboBox();
			this.classlabel = new System.Windows.Forms.Label();
			this.classes = new System.Windows.Forms.ComboBox();
			this.tallygrid = new System.Windows.Forms.DataGridView();
			this.tallyfooter = new System.Windows.Forms.Label();
			this.tallycopy = new System.Windows.Forms.Button();
			this.tabthings = new System.Windows.Forms.TabPage();
			this.thingspanel = new CodeImp.DoomBuilder.Controls.ThingTypesStatsPanel();
			this.tabtextures = new System.Windows.Forms.TabPage();
			this.texturesgrid = new System.Windows.Forms.DataGridView();
			this.TextureName = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.TextureKind = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.TextureUses = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.TextureMissing = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.tabtags = new System.Windows.Forms.TabPage();
			this.tagspanel = new CodeImp.DoomBuilder.Controls.TagStatsPanel();
			this.apply = new System.Windows.Forms.Button();
			this.cancel = new System.Windows.Forms.Button();
			this.tabs.SuspendLayout();
			this.taboverview.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.overviewgrid)).BeginInit();
			this.tabtally.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.tallygrid)).BeginInit();
			this.tabthings.SuspendLayout();
			this.tabtextures.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.texturesgrid)).BeginInit();
			this.tabtags.SuspendLayout();
			this.SuspendLayout();
			//
			// tabs
			//
			this.tabs.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tabs.Controls.Add(this.taboverview);
			this.tabs.Controls.Add(this.tabtally);
			this.tabs.Controls.Add(this.tabthings);
			this.tabs.Controls.Add(this.tabtextures);
			this.tabs.Controls.Add(this.tabtags);
			this.tabs.Location = new System.Drawing.Point(8, 8);
			this.tabs.Name = "tabs";
			this.tabs.SelectedIndex = 0;
			this.tabs.Size = new System.Drawing.Size(584, 402);
			this.tabs.TabIndex = 0;
			this.tabs.SelectedIndexChanged += new System.EventHandler(this.tabs_SelectedIndexChanged);
			//
			// taboverview
			//
			this.taboverview.Controls.Add(this.overviewgrid);
			this.taboverview.Controls.Add(this.overviewcopy);
			this.taboverview.Location = new System.Drawing.Point(4, 22);
			this.taboverview.Name = "taboverview";
			this.taboverview.Padding = new System.Windows.Forms.Padding(3);
			this.taboverview.Size = new System.Drawing.Size(576, 376);
			this.taboverview.TabIndex = 0;
			this.taboverview.Text = "Overview";
			this.taboverview.UseVisualStyleBackColor = true;
			//
			// overviewgrid
			//
			this.overviewgrid.AllowUserToAddRows = false;
			this.overviewgrid.AllowUserToDeleteRows = false;
			this.overviewgrid.AllowUserToResizeRows = false;
			this.overviewgrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.overviewgrid.BackgroundColor = System.Drawing.SystemColors.Window;
			this.overviewgrid.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
			this.overviewgrid.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.None;
			this.overviewgrid.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
			headerstyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
			headerstyle1.BackColor = System.Drawing.SystemColors.Control;
			headerstyle1.ForeColor = System.Drawing.SystemColors.WindowText;
			headerstyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
			headerstyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
			this.overviewgrid.ColumnHeadersDefaultCellStyle = headerstyle1;
			this.overviewgrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
			this.overviewgrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.OverviewItem,
            this.OverviewValue});
			this.overviewgrid.Location = new System.Drawing.Point(6, 6);
			this.overviewgrid.MultiSelect = false;
			this.overviewgrid.Name = "overviewgrid";
			this.overviewgrid.ReadOnly = true;
			this.overviewgrid.RowHeadersVisible = false;
			this.overviewgrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
			this.overviewgrid.Size = new System.Drawing.Size(564, 333);
			this.overviewgrid.TabIndex = 0;
			//
			// OverviewItem
			//
			this.OverviewItem.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
			this.OverviewItem.HeaderText = "Item";
			this.OverviewItem.Name = "OverviewItem";
			this.OverviewItem.ReadOnly = true;
			this.OverviewItem.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
			//
			// OverviewValue
			//
			this.OverviewValue.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
			this.OverviewValue.HeaderText = "Value";
			this.OverviewValue.Name = "OverviewValue";
			this.OverviewValue.ReadOnly = true;
			this.OverviewValue.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
			//
			// overviewcopy
			//
			this.overviewcopy.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.overviewcopy.Location = new System.Drawing.Point(6, 345);
			this.overviewcopy.Name = "overviewcopy";
			this.overviewcopy.Size = new System.Drawing.Size(90, 25);
			this.overviewcopy.TabIndex = 1;
			this.overviewcopy.Text = "Copy";
			this.overviewcopy.UseVisualStyleBackColor = true;
			this.overviewcopy.Click += new System.EventHandler(this.overviewcopy_Click);
			//
			// tabtally
			//
			this.tabtally.Controls.Add(this.modelabel);
			this.tabtally.Controls.Add(this.modes);
			this.tabtally.Controls.Add(this.classlabel);
			this.tabtally.Controls.Add(this.classes);
			this.tabtally.Controls.Add(this.tallygrid);
			this.tabtally.Controls.Add(this.tallyfooter);
			this.tabtally.Controls.Add(this.tallycopy);
			this.tabtally.Location = new System.Drawing.Point(4, 22);
			this.tabtally.Name = "tabtally";
			this.tabtally.Padding = new System.Windows.Forms.Padding(3);
			this.tabtally.Size = new System.Drawing.Size(576, 376);
			this.tabtally.TabIndex = 1;
			this.tabtally.Text = "Tally";
			this.tabtally.UseVisualStyleBackColor = true;
			//
			// modelabel
			//
			this.modelabel.AutoSize = true;
			this.modelabel.Location = new System.Drawing.Point(6, 10);
			this.modelabel.Name = "modelabel";
			this.modelabel.Size = new System.Drawing.Size(37, 13);
			this.modelabel.TabIndex = 0;
			this.modelabel.Text = "Mode:";
			//
			// modes
			//
			this.modes.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.modes.FormattingEnabled = true;
			this.modes.Location = new System.Drawing.Point(52, 6);
			this.modes.Name = "modes";
			this.modes.Size = new System.Drawing.Size(140, 21);
			this.modes.TabIndex = 1;
			this.modes.SelectedIndexChanged += new System.EventHandler(this.modes_SelectedIndexChanged);
			//
			// classlabel
			//
			this.classlabel.AutoSize = true;
			this.classlabel.Location = new System.Drawing.Point(210, 10);
			this.classlabel.Name = "classlabel";
			this.classlabel.Size = new System.Drawing.Size(35, 13);
			this.classlabel.TabIndex = 2;
			this.classlabel.Text = "Class:";
			//
			// classes
			//
			this.classes.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.classes.FormattingEnabled = true;
			this.classes.Location = new System.Drawing.Point(252, 6);
			this.classes.Name = "classes";
			this.classes.Size = new System.Drawing.Size(110, 21);
			this.classes.TabIndex = 3;
			this.classes.SelectedIndexChanged += new System.EventHandler(this.modes_SelectedIndexChanged);
			//
			// tallygrid
			//
			this.tallygrid.AllowUserToAddRows = false;
			this.tallygrid.AllowUserToDeleteRows = false;
			this.tallygrid.AllowUserToResizeRows = false;
			this.tallygrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tallygrid.BackgroundColor = System.Drawing.SystemColors.Window;
			this.tallygrid.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
			this.tallygrid.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.None;
			this.tallygrid.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
			headerstyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
			headerstyle2.BackColor = System.Drawing.SystemColors.Control;
			headerstyle2.ForeColor = System.Drawing.SystemColors.WindowText;
			headerstyle2.SelectionBackColor = System.Drawing.SystemColors.Highlight;
			headerstyle2.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
			headerstyle2.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
			this.tallygrid.ColumnHeadersDefaultCellStyle = headerstyle2;
			this.tallygrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
			this.tallygrid.Location = new System.Drawing.Point(6, 33);
			this.tallygrid.MultiSelect = false;
			this.tallygrid.Name = "tallygrid";
			this.tallygrid.ReadOnly = true;
			this.tallygrid.RowHeadersVisible = false;
			this.tallygrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
			this.tallygrid.Size = new System.Drawing.Size(564, 272);
			this.tallygrid.TabIndex = 4;
			this.tallygrid.CellMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.tallygrid_CellMouseClick);
			//
			// tallyfooter
			//
			this.tallyfooter.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tallyfooter.ForeColor = System.Drawing.SystemColors.GrayText;
			this.tallyfooter.Location = new System.Drawing.Point(6, 309);
			this.tallyfooter.Name = "tallyfooter";
			this.tallyfooter.Size = new System.Drawing.Size(564, 32);
			this.tallyfooter.TabIndex = 5;
			//
			// tallycopy
			//
			this.tallycopy.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.tallycopy.Location = new System.Drawing.Point(6, 345);
			this.tallycopy.Name = "tallycopy";
			this.tallycopy.Size = new System.Drawing.Size(90, 25);
			this.tallycopy.TabIndex = 6;
			this.tallycopy.Text = "Copy";
			this.tallycopy.UseVisualStyleBackColor = true;
			this.tallycopy.Click += new System.EventHandler(this.tallycopy_Click);
			//
			// tabthings
			//
			this.tabthings.Controls.Add(this.thingspanel);
			this.tabthings.Location = new System.Drawing.Point(4, 22);
			this.tabthings.Name = "tabthings";
			this.tabthings.Padding = new System.Windows.Forms.Padding(3);
			this.tabthings.Size = new System.Drawing.Size(576, 376);
			this.tabthings.TabIndex = 2;
			this.tabthings.Text = "Things";
			this.tabthings.UseVisualStyleBackColor = true;
			//
			// thingspanel
			//
			this.thingspanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.thingspanel.Location = new System.Drawing.Point(3, 3);
			this.thingspanel.Name = "thingspanel";
			this.thingspanel.Size = new System.Drawing.Size(570, 370);
			this.thingspanel.TabIndex = 0;
			//
			// tabtextures
			//
			this.tabtextures.Controls.Add(this.texturesgrid);
			this.tabtextures.Location = new System.Drawing.Point(4, 22);
			this.tabtextures.Name = "tabtextures";
			this.tabtextures.Padding = new System.Windows.Forms.Padding(3);
			this.tabtextures.Size = new System.Drawing.Size(576, 376);
			this.tabtextures.TabIndex = 3;
			this.tabtextures.Text = "Textures";
			this.tabtextures.UseVisualStyleBackColor = true;
			//
			// texturesgrid
			//
			this.texturesgrid.AllowUserToAddRows = false;
			this.texturesgrid.AllowUserToDeleteRows = false;
			this.texturesgrid.AllowUserToResizeRows = false;
			this.texturesgrid.BackgroundColor = System.Drawing.SystemColors.Window;
			this.texturesgrid.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
			this.texturesgrid.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.None;
			this.texturesgrid.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
			headerstyle3.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
			headerstyle3.BackColor = System.Drawing.SystemColors.Control;
			headerstyle3.ForeColor = System.Drawing.SystemColors.WindowText;
			headerstyle3.SelectionBackColor = System.Drawing.SystemColors.Highlight;
			headerstyle3.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
			this.texturesgrid.ColumnHeadersDefaultCellStyle = headerstyle3;
			this.texturesgrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
			this.texturesgrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.TextureName,
            this.TextureKind,
            this.TextureUses,
            this.TextureMissing});
			this.texturesgrid.Dock = System.Windows.Forms.DockStyle.Fill;
			this.texturesgrid.Location = new System.Drawing.Point(3, 3);
			this.texturesgrid.MultiSelect = false;
			this.texturesgrid.Name = "texturesgrid";
			this.texturesgrid.ReadOnly = true;
			this.texturesgrid.RowHeadersVisible = false;
			this.texturesgrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
			this.texturesgrid.Size = new System.Drawing.Size(570, 370);
			this.texturesgrid.TabIndex = 0;
			this.texturesgrid.CellMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.texturesgrid_CellMouseClick);
			//
			// TextureName
			//
			this.TextureName.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
			this.TextureName.HeaderText = "Name";
			this.TextureName.Name = "TextureName";
			this.TextureName.ReadOnly = true;
			//
			// TextureKind
			//
			this.TextureKind.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
			this.TextureKind.HeaderText = "Kind";
			this.TextureKind.Name = "TextureKind";
			this.TextureKind.ReadOnly = true;
			//
			// TextureUses
			//
			this.TextureUses.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
			this.TextureUses.HeaderText = "Uses";
			this.TextureUses.Name = "TextureUses";
			this.TextureUses.ReadOnly = true;
			//
			// TextureMissing
			//
			this.TextureMissing.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
			this.TextureMissing.HeaderText = "Missing";
			this.TextureMissing.Name = "TextureMissing";
			this.TextureMissing.ReadOnly = true;
			//
			// tabtags
			//
			this.tabtags.Controls.Add(this.tagspanel);
			this.tabtags.Location = new System.Drawing.Point(4, 22);
			this.tabtags.Name = "tabtags";
			this.tabtags.Padding = new System.Windows.Forms.Padding(3);
			this.tabtags.Size = new System.Drawing.Size(576, 376);
			this.tabtags.TabIndex = 4;
			this.tabtags.Text = "Tags";
			this.tabtags.UseVisualStyleBackColor = true;
			//
			// tagspanel
			//
			this.tagspanel.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tagspanel.Location = new System.Drawing.Point(3, 3);
			this.tagspanel.Name = "tagspanel";
			this.tagspanel.Size = new System.Drawing.Size(570, 370);
			this.tagspanel.TabIndex = 0;
			//
			// apply
			//
			this.apply.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.apply.Location = new System.Drawing.Point(406, 416);
			this.apply.Name = "apply";
			this.apply.Size = new System.Drawing.Size(90, 25);
			this.apply.TabIndex = 1;
			this.apply.Text = "OK";
			this.apply.UseVisualStyleBackColor = true;
			this.apply.Click += new System.EventHandler(this.apply_Click);
			//
			// cancel
			//
			this.cancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.cancel.Location = new System.Drawing.Point(502, 416);
			this.cancel.Name = "cancel";
			this.cancel.Size = new System.Drawing.Size(90, 25);
			this.cancel.TabIndex = 2;
			this.cancel.Text = "Cancel";
			this.cancel.UseVisualStyleBackColor = true;
			this.cancel.Click += new System.EventHandler(this.cancel_Click);
			//
			// MapStatisticsForm
			//
			this.AcceptButton = this.apply;
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.CancelButton = this.cancel;
			this.ClientSize = new System.Drawing.Size(600, 449);
			this.Controls.Add(this.tabs);
			this.Controls.Add(this.apply);
			this.Controls.Add(this.cancel);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.SizableToolWindow;
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(420, 300);
			this.Name = "MapStatisticsForm";
			this.Opacity = 0;
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.Text = "Map Statistics";
			this.tabs.ResumeLayout(false);
			this.taboverview.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.overviewgrid)).EndInit();
			this.tabtally.ResumeLayout(false);
			this.tabtally.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this.tallygrid)).EndInit();
			this.tabthings.ResumeLayout(false);
			this.tabtextures.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.texturesgrid)).EndInit();
			this.tabtags.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.TabControl tabs;
		private System.Windows.Forms.TabPage taboverview;
		private System.Windows.Forms.DataGridView overviewgrid;
		private System.Windows.Forms.DataGridViewTextBoxColumn OverviewItem;
		private System.Windows.Forms.DataGridViewTextBoxColumn OverviewValue;
		private System.Windows.Forms.Button overviewcopy;
		private System.Windows.Forms.TabPage tabtally;
		private System.Windows.Forms.Label modelabel;
		private System.Windows.Forms.ComboBox modes;
		private System.Windows.Forms.Label classlabel;
		private System.Windows.Forms.ComboBox classes;
		private System.Windows.Forms.DataGridView tallygrid;
		private System.Windows.Forms.Label tallyfooter;
		private System.Windows.Forms.Button tallycopy;
		private System.Windows.Forms.TabPage tabthings;
		private CodeImp.DoomBuilder.Controls.ThingTypesStatsPanel thingspanel;
		private System.Windows.Forms.TabPage tabtextures;
		private System.Windows.Forms.DataGridView texturesgrid;
		private System.Windows.Forms.DataGridViewTextBoxColumn TextureName;
		private System.Windows.Forms.DataGridViewTextBoxColumn TextureKind;
		private System.Windows.Forms.DataGridViewTextBoxColumn TextureUses;
		private System.Windows.Forms.DataGridViewTextBoxColumn TextureMissing;
		private System.Windows.Forms.TabPage tabtags;
		private CodeImp.DoomBuilder.Controls.TagStatsPanel tagspanel;
		private System.Windows.Forms.Button apply;
		private System.Windows.Forms.Button cancel;
	}
}
