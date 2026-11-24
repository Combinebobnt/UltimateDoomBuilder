#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Hosts the main window's toolbar strips on four edge panels and lays them out from a ToolbarLayoutModel
	internal sealed class ToolbarDock
	{
		#region ================== Constants

		private const string SETTINGS_KEY = "toolbars";

		#endregion

		#region ================== Types

		// A strip's place after the last layout, in its edge's coordinates
		private sealed class StripState
		{
			public string Id;
			public ToolStrip Strip;
			public bool Allowed = true;
			public int Edge = -1;
			public int VisibleRow = -1;
			public Rectangle Bounds;
		}

		// What a vertical strip changed on an item, to put back on a horizontal edge
		private sealed class ItemStyle
		{
			public ToolStripItemDisplayStyle DisplayStyle;
			public bool AutoSize;
			public Size Size;
		}

		// A row as laid out: the model's row number, its strips and its rectangle in edge coordinates
		private sealed class LaidRow
		{
			public int ModelRow;
			public List<StripState> Strips = new List<StripState>();
			public Rectangle Bounds;
		}

		#endregion

		#region ================== Variables

		private readonly MainForm form;
		private readonly ContextMenuStrip menu;
		private readonly Dictionary<string, StripState> strips = new Dictionary<string, StripState>(StringComparer.Ordinal);
		private readonly Dictionary<ToolStripItem, ItemStyle> itemstyles = new Dictionary<ToolStripItem, ItemStyle>();
		private readonly Panel[] edges = new Panel[4];
		private readonly List<LaidRow>[] laidrows = { new List<LaidRow>(), new List<LaidRow>(), new List<LaidRow>(), new List<LaidRow>() };
		private ToolbarLayoutModel model;
		private int batch;
		private bool layoutpending;
		private bool inlayout;

		private ToolStripSeparator menuseparator;
		private ToolStripMenuItem menudockto;
		private ToolStripMenuItem[] menudockedges;
		private ToolStripMenuItem menulock;
		private ToolStripMenuItem menureset;
		private ToolStrip menustrip; // The strip the context menu opened on

		#endregion

		#region ================== Properties

		public ToolbarLayoutModel Model { get { return model; } }
		public bool Locked { get { return model.Locked; } }
		public IEnumerable<ToolStrip> Strips { get { foreach(string id in ToolbarLayoutModel.IDS) if(strips.ContainsKey(id)) yield return strips[id].Strip; } }

		// Raised after a layout changed an edge's thickness, so the dockers can follow their space
		public event EventHandler EdgesResized;

		#endregion

		#region ================== Constructor

		public ToolbarDock(MainForm form, ContextMenuStrip menu)
		{
			this.form = form;
			this.menu = menu;
#if MONO_WINFORMS
			// Mono doesn't open a split button's dropdown from its arrow on a vertical strip
			string[] horizontalonly = { "rendering", "testing" };
#else
			string[] horizontalonly = null;
#endif
			model = ToolbarLayoutModel.Load(General.Settings.ReadSetting(SETTINGS_KEY, (System.Collections.IDictionary)null), horizontalonly);
		}

		#endregion

		#region ================== Setup

		// Registers a strip. With strip null a new one is made; items are moved into it in order.
		public ToolStrip AddStrip(string id, ToolStrip strip, string text, params ToolStripItem[] items)
		{
			if(strip == null)
			{
				strip = new ToolStrip();
				strip.Name = "toolbar" + id;
				strip.Text = text;
				strip.ImageScalingSize = MainForm.ScaledIconSize;
			}

			strip.SuspendLayout();
			foreach(ToolStripItem item in items)
			{
				// Mono's Add leaves the item in its old owner's collection too, where it keeps painting
				if(item.Owner != null && item.Owner != strip) item.Owner.Items.Remove(item);
				strip.Items.Add(item);
			}
			strip.Dock = DockStyle.None;
			strip.AutoSize = false;
			strip.ContextMenuStrip = menu;
			strip.GripStyle = model.Locked ? ToolStripGripStyle.Hidden : ToolStripGripStyle.Visible;
			strip.ResumeLayout(false);

			strips[id] = new StripState { Id = id, Strip = strip };
			return strip;
		}

		// Adds the edge panels to the form around the display: top under the menu row, bottom above the
		// info panel, left and right inside those and outside the dockers space
		public void Install(Control display, Control dockersspace, Control menurow, Control infopanel)
		{
			Control.ControlCollection controls = form.Controls;
			string[] names = { "toolbaredgetop", "toolbaredgebottom", "toolbaredgeleft", "toolbaredgeright" };
			DockStyle[] docks = { DockStyle.Top, DockStyle.Bottom, DockStyle.Left, DockStyle.Right };

			form.SuspendLayout();
			foreach(StripState s in strips.Values)
				if(s.Strip.Parent != null) s.Strip.Parent.Controls.Remove(s.Strip);

			for(int i = 0; i < 4; i++)
			{
				Panel p = new Panel();
				p.Name = names[i];
				p.Dock = docks[i];
				p.Size = Size.Empty;
				p.Margin = Padding.Empty;
				int edge = i;
				p.Resize += delegate { if(!inlayout) PlaceStrips(edge); };
				edges[i] = p;
				controls.Add(p);
			}

			// Docking runs from the highest index down, so the highest docks outermost
			int index = controls.IndexOf(dockersspace) + 1;
			controls.SetChildIndex(edges[ToolbarLayoutModel.EDGE_LEFT], index);
			controls.SetChildIndex(edges[ToolbarLayoutModel.EDGE_RIGHT], index + 1);
			controls.SetChildIndex(edges[ToolbarLayoutModel.EDGE_BOTTOM], controls.IndexOf(infopanel));
			controls.SetChildIndex(edges[ToolbarLayoutModel.EDGE_TOP], controls.IndexOf(menurow));
			form.ResumeLayout(false);

			CreateMenuItems();
		}

		private void CreateMenuItems()
		{
			menuseparator = new ToolStripSeparator();
			menudockto = new ToolStripMenuItem("Dock to");
			string[] names = { "Top", "Bottom", "Left", "Right" };
			menudockedges = new ToolStripMenuItem[4];
			for(int i = 0; i < 4; i++)
			{
				int edge = i;
				menudockedges[i] = new ToolStripMenuItem(names[i], null, delegate { DockMenuStrip(edge); });
				menudockto.DropDownItems.Add(menudockedges[i]);
			}
			menulock = new ToolStripMenuItem("Lock Toolbars", null, delegate { SetLocked(!model.Locked); });
			menureset = new ToolStripMenuItem("Reset Toolbar Layout", null, delegate { Reset(); });
			menu.Items.AddRange(new ToolStripItem[] { menuseparator, menudockto, menulock, menureset });
			menu.Opening += menu_Opening;
		}

		#endregion

		#region ================== Strip lookup

		public ToolStrip Strip(string id) { return strips[id].Strip; }

		public string IdOf(ToolStrip strip)
		{
			foreach(StripState s in strips.Values) if(s.Strip == strip) return s.Id;
			return null;
		}

		public int EdgeOf(string id) { return strips[id].Edge; }

		public Rectangle BoundsOf(string id)
		{
			StripState s = strips[id];
			if(s.Edge < 0) return Rectangle.Empty;
			Rectangle r = s.Bounds;
			r.Offset(edges[s.Edge].Location);
			return r;
		}

		// The strip whose items include the given item, if any
		public ToolStrip StripOf(ToolStripItem item)
		{
			foreach(StripState s in strips.Values) if(s.Strip.Items.Contains(item)) return s.Strip;
			return null;
		}

		// The modes strip shows only with a map loaded; the others whenever they have an item to show
		public void SetAllowed(string id, bool allowed) { strips[id].Allowed = allowed; }

		#endregion

		#region ================== Batching

		public void BeginUpdate()
		{
			batch++;
		}

		public void EndUpdate()
		{
			if(batch > 0) batch--;
			if(batch == 0 && layoutpending) PerformLayout();
		}

		// Lays out now, or at the end of the current batch
		public void RequestLayout()
		{
			if(batch > 0 || inlayout) layoutpending = true;
			else PerformLayout();
		}

		#endregion

		#region ================== Layout

		private static bool HasAvailableItem(ToolStrip strip)
		{
			foreach(ToolStripItem item in strip.Items)
				if(item.Available && !(item is ToolStripSeparator)) return true;
			return false;
		}

		public void PerformLayout()
		{
			if(edges[0] == null || inlayout) return;
			layoutpending = false;
			inlayout = true;

			int dockerspos = General.Settings.DockersPosition;
			int[] oldthickness = new int[4];
			for(int i = 0; i < 4; i++) oldthickness[i] = Thickness(i);

			form.SuspendLayout();
			try
			{
				for(int edge = 0; edge < 4; edge++)
				{
					bool vertical = ToolbarLayoutModel.IsVertical(edge);
					laidrows[edge].Clear();
					foreach(List<string> row in model.Rows(edge, dockerspos))
					{
						LaidRow laid = null;
						foreach(string id in row)
						{
							if(!strips.ContainsKey(id)) continue;
							StripState s = strips[id];
							bool show = s.Allowed && HasAvailableItem(s.Strip);
							if(!show)
							{
								s.Strip.Visible = false;
								s.Edge = -1;
								continue;
							}

							PrepareStrip(s, edge, vertical);
							if(!s.Strip.Visible) s.Strip.Visible = true; // Before measuring, which may skip a hidden strip's items
							if(laid == null)
							{
								laid = new LaidRow { ModelRow = model.Get(id).Row };
								laidrows[edge].Add(laid);
							}
							s.Edge = edge;
							s.VisibleRow = laidrows[edge].Count - 1;
							laid.Strips.Add(s);
						}
					}

					// Row thickness is the thickest strip in it
					int total = 0;
					foreach(LaidRow laid in laidrows[edge])
					{
						int thickness = 0;
						foreach(StripState s in laid.Strips)
						{
							Size pref = s.Strip.GetPreferredSize(Size.Empty);
							thickness = Math.Max(thickness, vertical ? pref.Width : pref.Height);
						}
						laid.Bounds = vertical ? new Rectangle(total, 0, thickness, 0) : new Rectangle(0, total, 0, thickness);
						total += thickness;
					}

					// Rows count from the window side
					if(edge == ToolbarLayoutModel.EDGE_BOTTOM || edge == ToolbarLayoutModel.EDGE_RIGHT)
					{
						foreach(LaidRow laid in laidrows[edge])
						{
							Rectangle b = laid.Bounds;
							if(vertical) b.X = total - b.X - b.Width; else b.Y = total - b.Y - b.Height;
							laid.Bounds = b;
						}
					}

					if(vertical) edges[edge].Width = total; else edges[edge].Height = total;
				}
			}
			finally
			{
				form.ResumeLayout(true);
			}

			for(int edge = 0; edge < 4; edge++) PlaceStrips(edge);
			inlayout = false;

			bool resized = false;
			for(int i = 0; i < 4; i++) resized |= (oldthickness[i] != Thickness(i));
			if(resized && EdgesResized != null) EdgesResized(this, EventArgs.Empty);
			if(layoutpending) RequestLayout();
		}

		private int Thickness(int edge)
		{
			return ToolbarLayoutModel.IsVertical(edge) ? edges[edge].Width : edges[edge].Height;
		}

		// Puts a strip on an edge panel in that edge's orientation
		private void PrepareStrip(StripState s, int edge, bool vertical)
		{
			ToolStrip strip = s.Strip;
			if(strip.Parent != edges[edge]) edges[edge].Controls.Add(strip);
			ToolStripLayoutStyle style = vertical ? ToolStripLayoutStyle.VerticalStackWithOverflow : ToolStripLayoutStyle.HorizontalStackWithOverflow;
			if(strip.LayoutStyle != style)
			{
				strip.LayoutStyle = style;

				// Mono's preferred size keeps the old orientation's length as the new thickness
				strip.Size = Size.Empty;
			}
			ApplyItemStyles(strip, vertical);
		}

		// Text items show as images on a vertical strip when they have one
		private void ApplyItemStyles(ToolStrip strip, bool vertical)
		{
			foreach(ToolStripItem item in strip.Items)
			{
				if(vertical)
				{
					if(itemstyles.ContainsKey(item) || item.Image == null || item is ToolStripSeparator) continue;
					if(item.DisplayStyle != ToolStripItemDisplayStyle.Text && item.DisplayStyle != ToolStripItemDisplayStyle.ImageAndText) continue;
					itemstyles[item] = new ItemStyle { DisplayStyle = item.DisplayStyle, AutoSize = item.AutoSize, Size = item.Size };
					item.DisplayStyle = ToolStripItemDisplayStyle.Image;
					item.AutoSize = true;
				}
				else if(itemstyles.ContainsKey(item))
				{
					RestoreItemStyle(item);
				}
			}
		}

		// Puts back what a vertical strip changed on an item; plugins reuse their items across mode switches
		public void RestoreItemStyle(ToolStripItem item)
		{
			ItemStyle st;
			if(!itemstyles.TryGetValue(item, out st)) return;
			itemstyles.Remove(item);
			item.DisplayStyle = st.DisplayStyle;
			item.AutoSize = st.AutoSize;
			if(!st.AutoSize) item.Size = st.Size;
		}

		// Positions the strips of each row along an edge
		private void PlaceStrips(int edge)
		{
			if(edges[edge] == null) return;
			bool vertical = ToolbarLayoutModel.IsVertical(edge);
			int length = vertical ? edges[edge].Height : edges[edge].Width;

			foreach(LaidRow laid in laidrows[edge])
			{
				int n = laid.Strips.Count;
				int[] offsets = new int[n], lengths = new int[n], positions, sizes;
				for(int i = 0; i < n; i++)
				{
					Size pref = laid.Strips[i].Strip.GetPreferredSize(Size.Empty);
					offsets[i] = model.Get(laid.Strips[i].Id).Offset;
					lengths[i] = vertical ? pref.Height : pref.Width;
				}

				int minlength = (int)(32 * MainForm.DPIScaler.Width);
				ToolbarLayoutModel.LayoutRow(offsets, lengths, length, minlength, out positions, out sizes);

				if(vertical) laid.Bounds = new Rectangle(laid.Bounds.X, 0, laid.Bounds.Width, length);
				else laid.Bounds = new Rectangle(0, laid.Bounds.Y, length, laid.Bounds.Height);

				for(int i = 0; i < n; i++)
				{
					StripState s = laid.Strips[i];
					s.Bounds = vertical
						? new Rectangle(laid.Bounds.X, positions[i], laid.Bounds.Width, sizes[i])
						: new Rectangle(positions[i], laid.Bounds.Y, sizes[i], laid.Bounds.Height);
					if(s.Strip.Bounds != s.Bounds) s.Strip.Bounds = s.Bounds;

					// Mono can leave an item where an earlier, narrower layout put it, painting over its neighbours
					s.Strip.PerformLayout();
				}
			}
		}

		#endregion

		#region ================== Geometry for dragging

		// Edge and row rectangles in form client coordinates, for ToolbarLayoutModel.HitTest
		public ToolbarEdgeGeometry[] GetGeometry()
		{
			ToolbarEdgeGeometry[] result = new ToolbarEdgeGeometry[4];
			for(int edge = 0; edge < 4; edge++)
			{
				Rectangle b = edges[edge].Bounds;
				result[edge] = new ToolbarEdgeGeometry { Bounds = b };
				foreach(LaidRow laid in laidrows[edge])
				{
					Rectangle r = laid.Bounds;
					r.Offset(b.Location);
					result[edge].Rows.Add(r);
				}
			}
			return result;
		}

		// A row rectangle in form client coordinates, or the line where a new row would open
		public Rectangle TargetBounds(string id, ToolbarDropTarget t, int striplength)
		{
			Rectangle b = edges[t.Edge].Bounds;
			bool vertical = ToolbarLayoutModel.IsVertical(t.Edge);
			List<LaidRow> rows = laidrows[t.Edge];
			int bar = Math.Max(3, (int)(3 * MainForm.DPIScaler.Width));
			int offset;
			InsertBefore(id, t, out offset);
			int along = vertical ? b.Top + offset : b.Left + offset;

			if(!t.NewRow && t.Row < rows.Count)
			{
				Rectangle r = rows[t.Row].Bounds;
				r.Offset(b.Location);
				return vertical ? new Rectangle(r.X, along, r.Width, Math.Min(striplength, b.Bottom - along))
				                : new Rectangle(along, r.Y, Math.Min(striplength, b.Right - along), r.Height);
			}

			// Boundary on the window side of row t.Row, or past the innermost row
			int depth = 0;
			for(int i = 0; i < Math.Min(t.Row, rows.Count); i++) depth += vertical ? rows[i].Bounds.Width : rows[i].Bounds.Height;
			switch(t.Edge)
			{
				case ToolbarLayoutModel.EDGE_TOP: return new Rectangle(b.Left, b.Top + depth, b.Width, bar);
				case ToolbarLayoutModel.EDGE_BOTTOM: return new Rectangle(b.Left, b.Bottom - depth - bar, b.Width, bar);
				case ToolbarLayoutModel.EDGE_LEFT: return new Rectangle(b.Left + depth, b.Top, bar, b.Height);
				default: return new Rectangle(b.Right - depth - bar, b.Top, bar, b.Height);
			}
		}

		#endregion

		#region ================== Moving

		// Applies a drop: hit-test rows count visible rows only, the model counts all of them
		public void Commit(string id, ToolbarDropTarget t)
		{
			if(!t.Valid) return;
			List<LaidRow> rows = laidrows[t.Edge];
			int modelrow;
			if(t.Row < rows.Count) modelrow = rows[t.Row].ModelRow;
			else modelrow = (rows.Count > 0) ? rows[rows.Count - 1].ModelRow + 1 : 0;

			int offset;
			StripState before = InsertBefore(id, t, out offset);
			if(t.NewRow || t.Row >= rows.Count)
			{
				if(model.Move(id, t.Edge, modelrow, t.NewRow, offset)) Changed();
				return;
			}

			// Joining a row: the drop goes in the row's shown order, flush behind the strip it lands on
			bool vertical = ToolbarLayoutModel.IsVertical(t.Edge);
			List<string> order = new List<string>();
			List<bool> flush = new List<bool>();
			int index = 0, prevend = 0, end = 0;
			foreach(StripState s in rows[t.Row].Strips)
			{
				int start = vertical ? s.Bounds.Y : s.Bounds.X;
				bool touching = (start <= end);
				end = vertical ? s.Bounds.Bottom : s.Bounds.Right;
				if(s.Id == id) continue;
				if(s != before && start <= offset)
				{
					index = order.Count + 1;
					prevend = end;
				}
				order.Add(s.Id);
				flush.Add(touching);
			}
			if(offset <= prevend) offset = 0; // Flush, so it follows if the strips ahead shrink
			if(model.Join(id, t.Edge, modelrow, order, flush, index, offset)) Changed();
		}

		// A drop on the leading half of a strip in the joined row takes that strip's place and pushes it on
		private StripState InsertBefore(string id, ToolbarDropTarget t, out int offset)
		{
			offset = t.Offset;
			if(t.NewRow || t.Row >= laidrows[t.Edge].Count) return null;
			bool vertical = ToolbarLayoutModel.IsVertical(t.Edge);
			foreach(StripState s in laidrows[t.Edge][t.Row].Strips)
			{
				if(s.Id == id) continue;
				int start = vertical ? s.Bounds.Y : s.Bounds.X;
				int length = vertical ? s.Bounds.Height : s.Bounds.Width;
				if(t.Offset >= start && t.Offset < start + length / 2)
				{
					offset = start;
					return s;
				}
			}
			return null;
		}

		// "Dock to": the end of the edge's last row, or a new row past it when the strip doesn't fit there
		private void DockMenuStrip(int edge)
		{
			string id = (menustrip != null) ? IdOf(menustrip) : null;
			if(id == null || !model.CanDock(id, edge)) return;
			List<LaidRow> rows = laidrows[edge];
			ToolbarDropTarget t = new ToolbarDropTarget { Valid = true, Edge = edge, Row = rows.Count, NewRow = true };
			if(rows.Count > 0)
			{
				LaidRow last = rows[rows.Count - 1];
				bool vertical = ToolbarLayoutModel.IsVertical(edge);
				int end = 0;
				foreach(StripState s in last.Strips)
					if(s.Id != id) end = Math.Max(end, vertical ? s.Bounds.Bottom : s.Bounds.Right);

				// Item count is about the same either way round, so the strip's longer side stands in for its length
				Size pref = menustrip.GetPreferredSize(Size.Empty);
				int length = Math.Max(pref.Width, pref.Height);
				int edgelength = vertical ? edges[edge].Height : edges[edge].Width;
				if(end + length <= edgelength)
				{
					t.Row = rows.Count - 1;
					t.NewRow = false;
					t.Offset = end;
				}
			}
			Commit(id, t);
		}

		public void SetLocked(bool locked)
		{
			model.Locked = locked;
			foreach(StripState s in strips.Values)
				s.Strip.GripStyle = locked ? ToolStripGripStyle.Hidden : ToolStripGripStyle.Visible;
			Changed();
		}

		public void Reset()
		{
			model = model.CloneDefault();
			Changed();
		}

		private void Changed()
		{
			Save();
			PerformLayout();
		}

		private void Save()
		{
			General.Settings.Config.DeleteSetting(SETTINGS_KEY);
			model.Save((path, value) => General.Settings.WriteSetting(SETTINGS_KEY + "." + path, value));
		}

		#endregion

		#region ================== Menu

		private void menu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
		{
			menustrip = menu.SourceControl as ToolStrip;
			string id = (menustrip != null) ? IdOf(menustrip) : null;
			menudockto.Enabled = (id != null);
			for(int i = 0; i < 4; i++)
			{
				menudockedges[i].Enabled = (id != null) && model.CanDock(id, i);
				menudockedges[i].Checked = (id != null) && strips[id].Edge == i;
			}
			menulock.Checked = model.Locked;
		}

		#endregion
	}
}
