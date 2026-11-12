using System;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Editing;

namespace CodeImp.DoomBuilder.BuilderModes
{
	public partial class TextureHistoryPanel : UserControl
	{
		private TextureHistory history;

		public TextureHistoryPanel()
		{
			InitializeComponent();
			view.EntryClicked += EntryClicked;
			view.EntriesChanged += HistoryChanged;
			WatchHistory(null);
		}

		internal void WatchHistory(TextureHistory value)
		{
			if(history != null) history.Changed -= HistoryChanged;
			history = value;
			view.SetEntries(value, true, true);
			if(history != null) history.Changed += HistoryChanged;
			UpdateHeader();
		}

		internal void Rebuild() { view.Rebuild(); }
		private void HistoryChanged(object sender, EventArgs e) { UpdateHeader(); }

		private void UpdateHeader()
		{
			state.Text = history == null ? "No map open" : history.Capacity == 0
				? (view.PinnedCount > 0 ? "Pinned textures (recent disabled)" : "Disabled (texturehistorysize = 0)") : "Pinned and recent textures";
			clear.Enabled = history != null && (history.Get(TextureHistoryKind.Texture).Count > 0 || history.Get(TextureHistoryKind.Flat).Count > 0);
		}

		private void EntryClicked(object sender, TextureHistoryClickEventArgs e)
		{
			if(history == null || General.Map == null || General.Map.IsDisposed) return;
			try
			{
				TexturePalette.SetBrush(e.Name, e.Kind);
				LinedefsMode lines = General.Editing.Mode as LinedefsMode;
				SectorsMode sectors = General.Editing.Mode as SectorsMode;
				if(lines != null && e.Button == MouseButtons.Left && (history.Mixed || e.Kind == TextureHistoryKind.Texture))
					lines.ApplyFromPalette(e.Name, General.Map.Map.GetSelectedLinedefs(true));
				else if(sectors != null)
				{
					if(history.Mixed || e.Kind == TextureHistoryKind.Flat)
						sectors.ApplyFromPalette(e.Name, General.Map.Map.GetSelectedSectors(true), false, e.Button == MouseButtons.Right);
					else if(e.Button == MouseButtons.Left)
						sectors.ApplyFromPalette(e.Name, General.Map.Map.GetSelectedSectors(true), true, false);
				}
			}
			finally { General.Interface.FocusDisplay(); }
		}

		private void ClearClicked(object sender, EventArgs e)
		{
			if(history != null) history.Clear();
			General.Interface.FocusDisplay();
		}
	}
}
