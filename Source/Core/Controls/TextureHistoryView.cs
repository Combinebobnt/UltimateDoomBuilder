using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder.Controls
{
	public sealed class TextureHistoryClickEventArgs : EventArgs
	{
		public string Name { get; private set; }
		public TextureHistoryKind Kind { get; private set; }
		public int Index { get; private set; }
		public MouseButtons Button { get; private set; }
		public Keys Modifiers { get; private set; }
		public bool IsPinned { get; private set; }

		internal TextureHistoryClickEventArgs(string name, TextureHistoryKind kind, int index, MouseButtons button, Keys modifiers, bool pinned = false)
		{
			Name = name; Kind = kind; Index = index; Button = button; Modifiers = modifiers; IsPinned = pinned;
		}
	}

	public sealed class TextureHistoryView : UserControl
	{
		internal sealed class EntryInfo
		{
			internal string Name;
			internal TextureHistoryKind Kind;
			internal int Index;
			internal bool Pinned;
		}

		internal sealed class EntryGroup
		{
			internal string Title;
			internal readonly List<EntryInfo> Entries = new List<EntryInfo>();
		}

		private sealed class Entry : ImageBrowserItem
		{
			internal readonly EntryInfo Info;
			private readonly int namewidth;
			internal Entry(ImageData image, EntryInfo info) : base(image, info.Name, true)
			{
				Info = info;
				namewidth = TextRenderer.MeasureText(info.Name, SystemFonts.MessageBoxFont).Width;
			}
			public override string TextureName { get { return Info.Name; } }
			public override int TextureNameWidth { get { return namewidth; } }
		}

		private readonly ImageSelectorPanel list;
		private readonly Timer timer;
		private readonly ToolTip tooltip;
		private TextureHistory history;
		private TextureFavorites favorites;
		private ContextMenuStrip contextmenu;
		private List<EntryGroup> entrygroups = new List<EntryGroup>();
		private bool showtextures, showflats;
		private int groups;
		public event EventHandler<TextureHistoryClickEventArgs> EntryClicked;
		public event EventHandler EntriesChanged;
		public string Source { get; set; }
		public int ImageSize { get { return list.ImageSize; } set { list.ImageSize = Math.Max(1, value); } }
		public int MaximumColumns { get { return list.MaximumColumns; } set { list.MaximumColumns = value; } }
		public int EntryCount { get { return list.Items.Count; } }
		public int GroupCount { get { return groups; } }
		public int PinnedCount { get { return entrygroups.Sum(group => group.Entries.Count(entry => entry.Pinned)); } }
		public Point FirstEntryLocation
		{
			get
			{
				if(EntryCount == 0) return Point.Empty;
				Rectangle rect = list.GetItemRectangle(0);
				rect.Intersect(list.ClientRectangle);
				return new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
			}
		}

		public TextureHistoryView()
		{
			list = new ImageSelectorPanel(false) { Dock = DockStyle.Fill, HideSelection = true, TakeFocusOnClick = false, Title = null };
			// Same as the texture browser; the group headers draw white text with black browsers
			if(General.Settings != null && General.Settings.BlackBrowsers)
			{
				list.BackColor = Color.Black;
				list.ForeColor = Color.White;
			}
			list.ImageSize = Math.Max(1, (int)Math.Round(64 * MainForm.DPIScaler.Width));
			list.ItemClicked += (item, e) => ClickEntry((Entry)item, e.Button);
			list.IsFavorite = item => favorites != null && favorites.Contains(((Entry)item).Info.Kind, ((Entry)item).Info.Name);
			list.MouseUp += (sender, e) =>
			{
				if(list.GetIndexAt(e.Location) < 0) OnMouseUp(e);
			};
			Controls.Add(list);
			tooltip = new ToolTip();
			tooltip.SetToolTip(list, "Alt-right-click pins or unpins without applying. Shift-right-click opens pin and recent-history actions.");
			timer = new Timer { Interval = 100 };
			timer.Tick += (sender, e) =>
			{
				list.Invalidate();
				if(list.Items.All(item => item.Icon.IsDisposed || item.IsPreviewLoaded)) timer.Stop();
			};
		}

		public void SetEntries(TextureHistory history, bool showtextures, bool showflats)
		{
			CloseContextMenu();
			if(this.history != null) this.history.Changed -= HistoryChanged;
			if(favorites != null) favorites.Changed -= HistoryChanged;
			this.history = history;
			favorites = history != null && General.Map != null ? General.Map.Favorites : null;
			this.showtextures = showtextures;
			this.showflats = showflats;
			if(history != null) history.Changed += HistoryChanged;
			if(favorites != null) favorites.Changed += HistoryChanged;
			Rebuild();
		}

		private void HistoryChanged(object sender, EventArgs e) { Rebuild(); }

		public void Rebuild()
		{
			timer.Stop();
			List<ImageBrowserItemGroup> result = new List<ImageBrowserItemGroup>();
			entrygroups = BuildEntryGroups(history, favorites, showtextures, showflats);
			if(history != null && General.Map != null && General.Map.Data != null)
			{
				list.ClassicView = true;
				foreach(EntryGroup source in entrygroups)
				{
					var group = new ImageBrowserItemGroup(source.Title);
					foreach(EntryInfo info in source.Entries)
					{
						ImageData image = ResolveImage(General.Map.Data, info.Kind, info.Name);
						if(!info.Pinned && history.Mixed && image == General.Map.Data.UnknownImage)
							image = General.Map.Data.GetFlatImage(info.Name);
						if(!info.Pinned && history.Mixed && image != General.Map.Data.UnknownImage)
							info.Kind = image.TextureNamespace == TextureNamespace.FLAT ? TextureHistoryKind.Flat : TextureHistoryKind.Texture;
						group.Items.Add(new Entry(image, info));
					}
					result.Add(group);
				}
			}
			groups = result.Count;
			list.MinimumItemWidth = CellWidth(result.SelectMany(group => group.Items).Select(item => item.TextureNameWidth), ImageSize);
			list.SetItems(result, true);
			if(list.Items.Any(item => !item.IsPreviewLoaded)) timer.Start();
			if(EntriesChanged != null) EntriesChanged(this, EventArgs.Empty);
		}

		internal static ImageData ResolveImage(DataManager data, TextureHistoryKind kind, string name)
		{
			return kind == TextureHistoryKind.Flat ? data.GetFlatImage(name) : data.GetTextureImage(name);
		}

		internal static List<EntryGroup> BuildEntryGroups(TextureHistory history, TextureFavorites favorites, bool textures, bool flats)
		{
			var result = new List<EntryGroup>();
			if(history == null) return result;
			bool mixed = history.Mixed;
			if(favorites != null)
			{
				if(textures || (mixed && flats)) AddEntryGroup(result, "Pinned textures", favorites.Get(TextureHistoryKind.Texture), TextureHistoryKind.Texture, true);
				if(flats || (mixed && textures)) AddEntryGroup(result, "Pinned flats", favorites.Get(TextureHistoryKind.Flat), TextureHistoryKind.Flat, true);
			}
			if(textures || (mixed && flats)) AddEntryGroup(result, mixed ? "Recent" : "Recent textures", history.Get(TextureHistoryKind.Texture), TextureHistoryKind.Texture, false);
			if(flats && !mixed) AddEntryGroup(result, "Recent flats", history.Get(TextureHistoryKind.Flat), TextureHistoryKind.Flat, false);
			return result;
		}

		private static void AddEntryGroup(List<EntryGroup> groups, string title, IReadOnlyList<string> names, TextureHistoryKind kind, bool pinned)
		{
			if(names.Count == 0) return;
			var group = new EntryGroup { Title = title };
			for(int i = 0; i < names.Count; i++) group.Entries.Add(new EntryInfo { Name = names[i], Kind = kind, Index = i, Pinned = pinned });
			groups.Add(group);
		}

		// Fits every row: recents are capped, so only the screen limits the height
		public Size GetPaletteSize(int columns)
		{
			columns = Math.Max(1, Math.Min(6, columns));
			int rows = int.MaxValue;
			int padding = list.ClassicView ? 26 : 4;
			int width = CellWidth(list.Items.Select(item => item.TextureNameWidth), ImageSize);
			int font = SystemFonts.MessageBoxFont.Height + 4;
			int rowheight = ImageSize + 4 + font + (General.Settings.ShowTextureSizes && General.Settings.TextureSizesBelow ? font : 0);
			int height = PaletteHeight(entrygroups.Select(group => group.Entries.Count), columns, rows, rowheight, font + 18);
			int widest = entrygroups.Count == 0 ? 1 : entrygroups.Max(group => group.Entries.Count);
			return new Size(Math.Min(columns, Math.Max(1, widest)) * (width + padding) + SystemInformation.VerticalScrollBarWidth,
				Math.Max(rowheight, height));
		}

		internal static int CellWidth(IEnumerable<int> namewidths, int imagesize)
		{
			return namewidths.Aggregate(imagesize, Math.Max);
		}

		internal static int PaletteHeight(IEnumerable<int> counts, int columns, int rows, int rowheight, int headerheight)
		{
			int height = 0;
			foreach(int count in counts)
			{
				if(rows <= 0) break;
				int visible = Math.Min(rows, (count + columns - 1) / columns);
				if(visible == 0) continue;
				height += visible * rowheight + headerheight;
				rows -= visible;
			}
			return height;
		}

		public bool PickEntry(int index, MouseButtons button)
		{
			if(GroupCount != 1 || index < 0 || index >= EntryCount) return false;
			ClickEntry((Entry)list.Items[index], button);
			return true;
		}

		private void ClickEntry(Entry entry, MouseButtons button)
		{
			ActivateEntry(entry.Info, button, ModifierKeys);
		}

		internal void ActivateEntry(EntryInfo entry, MouseButtons button, Keys modifiers)
		{
			if(button != MouseButtons.Left && button != MouseButtons.Right) return;
			InteractionLog.Write("texpalette", "src", Source, "kind", entry.Kind.ToString().ToLowerInvariant(),
				"group", entry.Pinned ? "pinned" : "recent", "idx", entry.Index.ToString(),
				"op", button == MouseButtons.Right && (modifiers & Keys.Alt) != 0 ? "favorite"
					: button == MouseButtons.Right && (modifiers & Keys.Shift) != 0 ? "context" : "pick");
			if(button == MouseButtons.Right && (modifiers & Keys.Alt) != 0)
			{
				if(favorites != null) favorites.Toggle(entry.Kind, entry.Name);
				return;
			}
			if(button == MouseButtons.Right && (modifiers & Keys.Shift) != 0)
			{
				ShowEntryMenu(entry);
				return;
			}
			if(EntryClicked != null) EntryClicked(this, new TextureHistoryClickEventArgs(entry.Name, entry.Kind, entry.Index, button, modifiers, entry.Pinned));
		}

		private void ShowEntryMenu(EntryInfo entry)
		{
			CloseContextMenu();
			TextureFavorites ownerfavorites = favorites;
			TextureHistory ownerhistory = history;
			string name = entry.Name;
			TextureHistoryKind kind = entry.Kind;
			var menu = new ContextMenuStrip();
			if(ownerfavorites != null)
				menu.Items.Add(ownerfavorites.Contains(kind, name) ? "Unpin" : "Pin", null, (sender, e) => ownerfavorites.Toggle(kind, name));
			menu.Items.Add("Remove from recent", null, (sender, e) => { if(ownerhistory != null) ownerhistory.Remove(name, kind); });
			menu.Items.Add("Clear recent", null, (sender, e) => { if(ownerhistory != null) ownerhistory.Clear(); });
			menu.Closed += EntryMenuClosed;
			contextmenu = menu;
			menu.Show(this, PointToClient(Cursor.Position));
		}

		public bool OwnsContextMenuInput(IntPtr window)
		{
			ContextMenuStrip menu = contextmenu;
			if(menu == null || menu.IsDisposed || !menu.IsHandleCreated || window == IntPtr.Zero) return false;
			Control target = Control.FromHandle(window) ?? Control.FromChildHandle(window);
			return target == menu || (target != null && menu.Contains(target));
		}

		public void CloseContextMenu()
		{
			ContextMenuStrip menu = contextmenu;
			contextmenu = null;
			if(menu == null || menu.IsDisposed) return;
			menu.Closed -= EntryMenuClosed;
			try { menu.Close(); }
			finally { if(!menu.IsDisposed) menu.Dispose(); }
		}

		private void EntryMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
		{
			ContextMenuStrip menu = (ContextMenuStrip)sender;
			if(contextmenu == menu) contextmenu = null;
			menu.Closed -= EntryMenuClosed;
			menu.Dispose();
		}

		internal void PruneRecent(string name, TextureHistoryKind kind)
		{
			if(history != null) history.Remove(name, kind);
		}

		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				CloseContextMenu();
				if(history != null) history.Changed -= HistoryChanged;
				if(favorites != null) favorites.Changed -= HistoryChanged;
				timer.Dispose();
				tooltip.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
