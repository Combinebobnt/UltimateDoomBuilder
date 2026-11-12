namespace CodeImp.DoomBuilder.BuilderModes
{
	partial class TextureHistoryPanel
	{
		private System.ComponentModel.IContainer components;
		private CodeImp.DoomBuilder.Controls.TextureHistoryView view;
		private System.Windows.Forms.Label state;
		private System.Windows.Forms.Button clear;

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				if(history != null) history.Changed -= HistoryChanged;
				view.EntriesChanged -= HistoryChanged;
				if(components != null) components.Dispose();
			}
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			components = new System.ComponentModel.Container();
			view = new CodeImp.DoomBuilder.Controls.TextureHistoryView { Dock = System.Windows.Forms.DockStyle.Fill, Source = "docker" };
			state = new System.Windows.Forms.Label { Dock = System.Windows.Forms.DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoEllipsis = true };
			clear = new System.Windows.Forms.Button { Dock = System.Windows.Forms.DockStyle.Right, Text = "Clear", Width = 60 };
			clear.Click += ClearClicked;
			var header = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Top, Height = 28 };
			header.Controls.Add(state);
			header.Controls.Add(clear);
			var tooltip = new System.Windows.Forms.ToolTip(components);
			tooltip.SetToolTip(view, "Click to set the brush and apply to the selection only. Right-click a flat for ceilings. Alt-right-click toggles a pin without applying; Shift-right-click opens pin/recent actions. Namespace mixing stays fixed until the map is reopened.");
			tooltip.SetToolTip(clear, "Clear this map session's recent textures and flats");
			AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			Controls.Add(view);
			Controls.Add(header);
			Name = "TextureHistoryPanel";
			Size = new System.Drawing.Size(300, 480);
		}
	}
}
