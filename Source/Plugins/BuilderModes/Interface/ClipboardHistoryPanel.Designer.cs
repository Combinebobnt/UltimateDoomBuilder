namespace CodeImp.DoomBuilder.BuilderModes
{
	partial class ClipboardHistoryPanel
	{
		private System.ComponentModel.IContainer components;
		private CodeImp.DoomBuilder.Controls.OptimizedListView list;
		private System.Windows.Forms.ColumnHeader coldescription;
		private System.Windows.Forms.ColumnHeader colsize;
		private System.Windows.Forms.ColumnHeader colsource;
		private System.Windows.Forms.ColumnHeader colcopied;
		private System.Windows.Forms.Button clear;
		private System.Windows.Forms.ToolTip tooltip;
		private System.Windows.Forms.ContextMenuStrip menu;
		private System.Windows.Forms.ToolStripMenuItem paste;
		private System.Windows.Forms.ToolStripMenuItem pastespecial;
		private System.Windows.Forms.ToolStripMenuItem copy;
		private System.Windows.Forms.ToolStripMenuItem remove;
		private System.Windows.Forms.ToolStripMenuItem clearall;

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				if(history != null) history.Changed -= history_Changed;
				if(components != null) components.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.list = new CodeImp.DoomBuilder.Controls.OptimizedListView();
			this.coldescription = new System.Windows.Forms.ColumnHeader();
			this.colsize = new System.Windows.Forms.ColumnHeader();
			this.colsource = new System.Windows.Forms.ColumnHeader();
			this.colcopied = new System.Windows.Forms.ColumnHeader();
			this.clear = new System.Windows.Forms.Button();
			this.tooltip = new System.Windows.Forms.ToolTip(this.components);
			this.menu = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.paste = new System.Windows.Forms.ToolStripMenuItem("Paste", null, this.paste_Click);
			this.pastespecial = new System.Windows.Forms.ToolStripMenuItem("Paste Special...", null, this.pastespecial_Click);
			this.copy = new System.Windows.Forms.ToolStripMenuItem("Copy to Clipboard", null, this.copy_Click);
			this.remove = new System.Windows.Forms.ToolStripMenuItem("Remove", null, this.remove_Click);
			this.clearall = new System.Windows.Forms.ToolStripMenuItem("Clear All", null, this.clear_Click);
			this.SuspendLayout();
			this.menu.Items.AddRange(new System.Windows.Forms.ToolStripItem[] { this.paste, this.pastespecial, this.copy, this.remove, this.clearall });
			this.menu.Opening += this.menu_Opening;
			this.list.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { this.coldescription, this.colsize, this.colsource, this.colcopied });
			this.list.ContextMenuStrip = this.menu;
			this.list.Dock = System.Windows.Forms.DockStyle.Fill;
			this.list.FullRowSelect = true;
			this.list.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.Nonclickable;
			this.list.MultiSelect = false;
			this.list.Name = "list";
			this.list.ShowGroups = false;
			this.list.ShowItemToolTips = true;
			this.list.Size = new System.Drawing.Size(390, 522);
			this.list.TabIndex = 0;
			this.list.UseCompatibleStateImageBehavior = false;
			this.list.View = System.Windows.Forms.View.Details;
			this.list.Resize += this.list_Resize;
			this.list.SelectedIndexChanged += this.list_SelectedIndexChanged;
			this.list.MouseDown += this.list_MouseDown;
			this.list.MouseUp += this.list_MouseUp;
			this.list.KeyUp += this.list_KeyUp;
			this.coldescription.Text = "Contents";
			this.coldescription.Width = 173;
			this.colsize.Text = "Size";
			this.colsize.Width = 64;
			this.colsource.Text = "From";
			this.colsource.Width = 90;
			this.colcopied.Text = "Copied";
			this.colcopied.Width = 45;
			this.clear.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.clear.Name = "clear";
			this.clear.Size = new System.Drawing.Size(390, 26);
			this.clear.TabIndex = 1;
			this.clear.Text = "Clear All";
			this.clear.UseVisualStyleBackColor = true;
			this.clear.Click += this.clear_Click;
			this.tooltip.SetToolTip(this.clear, "Remove all entries from this session's clipboard history");
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.Controls.Add(this.list);
			this.Controls.Add(this.clear);
			this.Name = "ClipboardHistoryPanel";
			this.Size = new System.Drawing.Size(390, 548);
			this.ResumeLayout(false);
		}
	}
}
