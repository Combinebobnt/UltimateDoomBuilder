namespace CodeImp.DoomBuilder.BuilderModes
{
	partial class TexturePaletteForm
	{
		private CodeImp.DoomBuilder.Controls.TextureHistoryView view;

		protected override void Dispose(bool disposing)
		{
			if(disposing) System.Windows.Forms.Application.RemoveMessageFilter(this);
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			SuspendLayout();
			view = new CodeImp.DoomBuilder.Controls.TextureHistoryView { Dock = System.Windows.Forms.DockStyle.Fill, Source = "popup", MaximumColumns = 6 };
			AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
			FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			ShowInTaskbar = false;
			StartPosition = System.Windows.Forms.FormStartPosition.Manual;
			KeyPreview = true;
			Padding = new System.Windows.Forms.Padding(4);
			Controls.Add(view);
			Name = "TexturePaletteForm";
			Text = "Texture palette";
			ResumeLayout(false);
		}
	}
}
