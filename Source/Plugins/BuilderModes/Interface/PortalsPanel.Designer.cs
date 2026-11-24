namespace CodeImp.DoomBuilder.BuilderModes
{
	partial class PortalsPanel
	{
		private System.ComponentModel.IContainer components;
		private System.Windows.Forms.TreeView tree;
		private System.Windows.Forms.FlowLayoutPanel options;
		private System.Windows.Forms.CheckBox selectonclick;
		private System.Windows.Forms.CheckBox centeronclick;
		private System.Windows.Forms.CheckBox problemsonly;
		private System.Windows.Forms.Label summary;
		private System.Windows.Forms.ToolTip tooltip;
		private System.Windows.Forms.ImageList images;
		private System.Windows.Forms.Timer updatetimer;

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				Terminate();
				if(components != null) components.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.tree = new System.Windows.Forms.TreeView();
			this.options = new System.Windows.Forms.FlowLayoutPanel();
			this.selectonclick = new System.Windows.Forms.CheckBox();
			this.centeronclick = new System.Windows.Forms.CheckBox();
			this.problemsonly = new System.Windows.Forms.CheckBox();
			this.summary = new System.Windows.Forms.Label();
			this.tooltip = new System.Windows.Forms.ToolTip(this.components);
			this.images = new System.Windows.Forms.ImageList(this.components);
			this.updatetimer = new CodeImp.DoomBuilder.GuardedTimer(this.components);
			this.options.SuspendLayout();
			this.SuspendLayout();
			this.images.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
			this.images.ImageSize = new System.Drawing.Size(16, 16);
			this.options.AutoSize = true;
			this.options.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.options.Controls.Add(this.selectonclick);
			this.options.Controls.Add(this.centeronclick);
			this.options.Controls.Add(this.problemsonly);
			this.options.Dock = System.Windows.Forms.DockStyle.Top;
			this.options.Name = "options";
			this.options.Size = new System.Drawing.Size(300, 46);
			this.options.TabIndex = 0;
			this.selectonclick.AutoSize = true;
			this.selectonclick.Name = "selectonclick";
			this.selectonclick.TabIndex = 0;
			this.selectonclick.Text = "Select on click";
			this.selectonclick.UseVisualStyleBackColor = true;
			this.selectonclick.CheckedChanged += this.option_CheckedChanged;
			this.tooltip.SetToolTip(this.selectonclick, "Clicking a row selects its elements in the matching editing mode");
			this.centeronclick.AutoSize = true;
			this.centeronclick.Name = "centeronclick";
			this.centeronclick.TabIndex = 1;
			this.centeronclick.Text = "Center on click";
			this.centeronclick.UseVisualStyleBackColor = true;
			this.centeronclick.CheckedChanged += this.option_CheckedChanged;
			this.tooltip.SetToolTip(this.centeronclick, "Clicking a row centers the view on its elements");
			this.problemsonly.AutoSize = true;
			this.problemsonly.Name = "problemsonly";
			this.problemsonly.TabIndex = 2;
			this.problemsonly.Text = "Problems only";
			this.problemsonly.UseVisualStyleBackColor = true;
			this.problemsonly.CheckedChanged += this.problemsonly_CheckedChanged;
			this.tooltip.SetToolTip(this.problemsonly, "Show only portals with errors or warnings");
			this.tree.Dock = System.Windows.Forms.DockStyle.Fill;
			this.tree.HideSelection = false;
			this.tree.ImageList = this.images;
			this.tree.Name = "tree";
			this.tree.ShowNodeToolTips = true;
			this.tree.Size = new System.Drawing.Size(300, 480);
			this.tree.TabIndex = 1;
			this.tree.NodeMouseClick += this.tree_NodeMouseClick;
			this.summary.AutoEllipsis = true;
			this.summary.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.summary.Name = "summary";
			this.summary.Size = new System.Drawing.Size(300, 22);
			this.summary.TabIndex = 2;
			this.summary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this.updatetimer.Interval = 250;
			this.updatetimer.Tick += this.updatetimer_Tick;
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.Controls.Add(this.tree);
			this.Controls.Add(this.options);
			this.Controls.Add(this.summary);
			this.Name = "PortalsPanel";
			this.Size = new System.Drawing.Size(300, 548);
			this.VisibleChanged += this.PortalsPanel_VisibleChanged;
			this.options.ResumeLayout(false);
			this.options.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();
		}
	}
}
