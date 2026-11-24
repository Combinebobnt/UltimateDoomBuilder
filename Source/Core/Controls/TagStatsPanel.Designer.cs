namespace CodeImp.DoomBuilder.Controls
{
	partial class TagStatsPanel
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

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle2 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle3 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle4 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle5 = new System.Windows.Forms.DataGridViewCellStyle();
			System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle6 = new System.Windows.Forms.DataGridViewCellStyle();
			this.dataGridView = new System.Windows.Forms.DataGridView();
			this.TagColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.LabelColumn = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.Sectors = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.Linedefs = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.Things = new System.Windows.Forms.DataGridViewTextBoxColumn();
			this.hint = new System.Windows.Forms.TextBox();
			((System.ComponentModel.ISupportInitialize)(this.dataGridView)).BeginInit();
			this.SuspendLayout();
			//
			// dataGridView
			//
			this.dataGridView.AllowUserToAddRows = false;
			this.dataGridView.AllowUserToDeleteRows = false;
			this.dataGridView.AllowUserToResizeColumns = false;
			this.dataGridView.AllowUserToResizeRows = false;
			this.dataGridView.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.dataGridView.BackgroundColor = System.Drawing.SystemColors.Window;
			this.dataGridView.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
			this.dataGridView.CellBorderStyle = System.Windows.Forms.DataGridViewCellBorderStyle.None;
			this.dataGridView.ColumnHeadersBorderStyle = System.Windows.Forms.DataGridViewHeaderBorderStyle.Single;
			dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
			dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Control;
			dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.WindowText;
			dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
			dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
			dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
			this.dataGridView.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle1;
			this.dataGridView.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
			this.dataGridView.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.TagColumn,
            this.LabelColumn,
            this.Sectors,
            this.Linedefs,
            this.Things});
			this.dataGridView.Location = new System.Drawing.Point(3, 3);
			this.dataGridView.MultiSelect = false;
			this.dataGridView.Name = "dataGridView";
			this.dataGridView.RowHeadersVisible = false;
			this.dataGridView.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.dataGridView.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
			this.dataGridView.Size = new System.Drawing.Size(490, 290);
			this.dataGridView.TabIndex = 0;
			this.dataGridView.CellMouseClick += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.dataGridView_CellMouseClick);
			//
			// TagColumn
			//
			this.TagColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.ColumnHeader;
			dataGridViewCellStyle2.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
			this.TagColumn.DefaultCellStyle = dataGridViewCellStyle2;
			this.TagColumn.HeaderText = "Tag";
			this.TagColumn.Name = "TagColumn";
			this.TagColumn.ReadOnly = true;
			//
			// LabelColumn
			//
			this.LabelColumn.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill;
			dataGridViewCellStyle3.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
			this.LabelColumn.DefaultCellStyle = dataGridViewCellStyle3;
			this.LabelColumn.HeaderText = "Label";
			this.LabelColumn.Name = "LabelColumn";
			//
			// Sectors
			//
			this.Sectors.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.ColumnHeader;
			dataGridViewCellStyle4.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
			this.Sectors.DefaultCellStyle = dataGridViewCellStyle4;
			this.Sectors.HeaderText = "Sectors";
			this.Sectors.Name = "Sectors";
			this.Sectors.ReadOnly = true;
			//
			// Linedefs
			//
			this.Linedefs.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.ColumnHeader;
			dataGridViewCellStyle5.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
			this.Linedefs.DefaultCellStyle = dataGridViewCellStyle5;
			this.Linedefs.HeaderText = "Linedefs";
			this.Linedefs.Name = "Linedefs";
			this.Linedefs.ReadOnly = true;
			//
			// Things
			//
			this.Things.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.ColumnHeader;
			dataGridViewCellStyle6.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
			this.Things.DefaultCellStyle = dataGridViewCellStyle6;
			this.Things.HeaderText = "Things";
			this.Things.Name = "Things";
			this.Things.ReadOnly = true;
			//
			// hint
			//
			this.hint.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.hint.BackColor = System.Drawing.SystemColors.Control;
			this.hint.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.hint.Location = new System.Drawing.Point(3, 299);
			this.hint.Multiline = true;
			this.hint.Name = "hint";
			this.hint.ReadOnly = true;
			this.hint.Size = new System.Drawing.Size(490, 46);
			this.hint.TabIndex = 1;
			this.hint.TabStop = false;
			this.hint.Text = "Click on a cell in Sectors, Linedefs or Things column to select map elements with given tag.\r\n"
				+ "Right click to open Properties form for map elements with given tag.\r\n"
				+ "Click or press F2 in the Label cells to edit tag labels.";
			//
			// TagStatsPanel
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.Controls.Add(this.dataGridView);
			this.Controls.Add(this.hint);
			this.Name = "TagStatsPanel";
			this.Size = new System.Drawing.Size(496, 348);
			((System.ComponentModel.ISupportInitialize)(this.dataGridView)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.DataGridView dataGridView;
		private System.Windows.Forms.DataGridViewTextBoxColumn TagColumn;
		private System.Windows.Forms.DataGridViewTextBoxColumn LabelColumn;
		private System.Windows.Forms.DataGridViewTextBoxColumn Sectors;
		private System.Windows.Forms.DataGridViewTextBoxColumn Linedefs;
		private System.Windows.Forms.DataGridViewTextBoxColumn Things;
		private System.Windows.Forms.TextBox hint;
	}
}
