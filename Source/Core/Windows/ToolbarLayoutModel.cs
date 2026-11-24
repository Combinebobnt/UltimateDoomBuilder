#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Where one toolbar strip sits: an edge of the main window, a row on that edge (0 is outermost)
	// and the requested start along the row, in pixels
	internal sealed class ToolbarPlacement
	{
		public string Id;
		public int Edge;
		public int Row;
		public int Offset;

		public ToolbarPlacement(string id, int edge, int row, int offset)
		{
			Id = id;
			Edge = edge;
			Row = row;
			Offset = offset;
		}
	}

	// A drop position found by ToolbarLayoutModel.HitTest
	internal struct ToolbarDropTarget
	{
		public bool Valid;
		public int Edge;
		public int Row;
		public bool NewRow; // When true, a new row is inserted at Row
		public int Offset;
	}

	// Where a shown strip starts and ends along its row, in pixels
	internal struct ToolbarSpan
	{
		public int Start;
		public int End;

		public ToolbarSpan(int start, int end) { Start = start; End = end; }
	}

	// The geometry of one edge, in main window client coordinates
	internal sealed class ToolbarEdgeGeometry
	{
		public Rectangle Bounds;
		public List<Rectangle> Rows = new List<Rectangle>();
	}

	// Strip placement, validation, persistence and drop math for the dockable toolbars. No WinForms types.
	internal sealed class ToolbarLayoutModel
	{
		#region ================== Constants

		public const int VERSION = 1;

		public const int EDGE_AUTO = -1;
		public const int EDGE_TOP = 0;
		public const int EDGE_BOTTOM = 1;
		public const int EDGE_LEFT = 2;
		public const int EDGE_RIGHT = 3;

		public const string MODES = "modes";
		public const string FILTERS = "filters";

		// Default order along the top row; also the settings keys
		public static readonly string[] IDS = { "file", "script", "undo", "clipboard", "prefabs", FILTERS, "view", "geometry", "rendering", "testing", MODES };

		// Distance from a window edge, in pixels, within which a drop docks to that edge
		public const int EDGE_SNAP = 24;

		#endregion

		#region ================== Variables

		private readonly Dictionary<string, ToolbarPlacement> placements = new Dictionary<string, ToolbarPlacement>(StringComparer.Ordinal);
		private readonly HashSet<string> horizontalonly = new HashSet<string>(StringComparer.Ordinal) { FILTERS };
		private bool locked;

		#endregion

		#region ================== Properties

		public bool Locked { get { return locked; } set { locked = value; } }

		#endregion

		#region ================== Constructor

		// horizontalonly: strips that refuse the left and right edges, besides filters
		private ToolbarLayoutModel(IEnumerable<string> horizontalonly)
		{
			if(horizontalonly != null) this.horizontalonly.UnionWith(horizontalonly);
		}

		public static ToolbarLayoutModel Default(IEnumerable<string> horizontalonly = null)
		{
			ToolbarLayoutModel m = new ToolbarLayoutModel(horizontalonly);
			foreach(string id in IDS) m.placements[id] = m.DefaultPlacement(id);
			return m;
		}

		// A default layout with the same horizontal-only strips and lock state
		public ToolbarLayoutModel CloneDefault()
		{
			ToolbarLayoutModel m = Default(horizontalonly);
			m.locked = locked;
			return m;
		}

		#endregion

		#region ================== Queries

		public ToolbarPlacement Get(string id) { return placements[id]; }

		public static bool IsVertical(int edge) { return edge == EDGE_LEFT || edge == EDGE_RIGHT; }

		public bool CanDock(string id, int edge)
		{
			if(edge == EDGE_AUTO) return id == MODES;
			if(edge < EDGE_TOP || edge > EDGE_RIGHT) return false;
			return !(IsVertical(edge) && horizontalonly.Contains(id));
		}

		// The modes strip's automatic edge sits opposite the dockers (DockersPosition 0 = left)
		public static int ResolveEdge(int edge, int dockersposition)
		{
			if(edge != EDGE_AUTO) return edge;
			return (dockersposition == 0) ? EDGE_RIGHT : EDGE_LEFT;
		}

		// True when the modes strip still follows the dockers; it then holds the outermost row of its edge alone
		public bool ModesAuto { get { return placements[MODES].Edge == EDGE_AUTO; } }

		// The strips on one edge, grouped by row and ordered along each row
		public List<List<string>> Rows(int edge, int dockersposition)
		{
			List<ToolbarPlacement> onedge = new List<ToolbarPlacement>();
			foreach(string id in IDS)
				if(placements[id].Edge != EDGE_AUTO && placements[id].Edge == edge) onedge.Add(placements[id]);
			onedge.Sort(ComparePlacements);

			// Automatic modes rows are numbered apart from the edge's own rows, so never share one
			List<List<string>> rows = new List<List<string>>();
			if(ModesAuto && ResolveEdge(EDGE_AUTO, dockersposition) == edge) rows.Add(new List<string> { MODES });
			int lastrow = int.MinValue;
			foreach(ToolbarPlacement p in onedge)
			{
				if(p.Row != lastrow) { rows.Add(new List<string>()); lastrow = p.Row; }
				rows[rows.Count - 1].Add(p.Id);
			}
			return rows;
		}

		private static int ComparePlacements(ToolbarPlacement a, ToolbarPlacement b)
		{
			if(a.Row != b.Row) return a.Row.CompareTo(b.Row);
			if(a.Offset != b.Offset) return a.Offset.CompareTo(b.Offset);
			return Array.IndexOf(IDS, a.Id).CompareTo(Array.IndexOf(IDS, b.Id));
		}

		#endregion

		#region ================== Moving

		// Puts a strip into an existing row at an index of the row's shown order (the other strips, front
		// to back; flush marks those touching the one ahead, which have no gap of their own). Offsets
		// become the smallest that keep that order and the remaining gaps, so a strip that was only
		// pushed along by packing never turns that push into a gap.
		public bool Join(string id, int edge, int row, IList<string> order, IList<bool> flush, int index, int offset)
		{
			if(!placements.ContainsKey(id) || !CanDock(id, edge) || edge == EDGE_AUTO) return false;
			ToolbarPlacement moved = placements[id];
			moved.Edge = edge;
			moved.Row = row;

			List<string> ids = new List<string>();
			List<int> wanted = new List<int>();
			for(int i = 0; i < order.Count; i++)
			{
				if(order[i] == id || !placements.ContainsKey(order[i])) continue;
				ids.Add(order[i]);
				wanted.Add(flush[i] ? 0 : placements[order[i]].Offset);
			}
			index = Math.Max(0, Math.Min(index, ids.Count));
			ids.Insert(index, id);
			wanted.Insert(index, Math.Max(0, offset));

			int prev = -1;
			for(int i = 0; i < ids.Count; i++)
			{
				ToolbarPlacement p = placements[ids[i]];
				p.Offset = Math.Max(wanted[i], prev + 1);
				prev = p.Offset;
			}
			Compact();
			return true;
		}

		// Join's arguments for a drop into the model's whole row; spans holds the shown strips. Hidden ones
		// go flush behind the strip ahead, so a stale offset can't sort them past the drop.
		public static int JoinOrder(string id, IList<string> row, IDictionary<string, ToolbarSpan> spans, string before, ref int offset, List<string> order, List<bool> flush)
		{
			int index = 0, prevend = 0, end = 0;
			foreach(string other in row)
			{
				ToolbarSpan span;
				if(!spans.TryGetValue(other, out span))
				{
					if(other == id) continue;
					order.Add(other);
					flush.Add(true);
					continue;
				}

				bool touching = (span.Start <= end);
				end = span.End;
				if(other == id) continue;
				if(other != before && span.Start <= offset)
				{
					index = order.Count + 1;
					prevend = end;
				}
				order.Add(other);
				flush.Add(touching);
			}
			if(offset <= prevend) offset = 0; // Flush, so it follows if the strips ahead shrink
			return index;
		}

		// Moves a strip onto a row of an edge, or into a new row inserted at that index
		public bool Move(string id, int edge, int row, bool newrow, int offset)
		{
			if(!placements.ContainsKey(id) || !CanDock(id, edge) || edge == EDGE_AUTO) return false;
			ToolbarPlacement moved = placements[id];
			row = Math.Max(0, row);
			offset = Math.Max(0, offset);

			// Rows hold their order through compaction, so numbering is only relative
			foreach(ToolbarPlacement p in placements.Values)
			{
				if(p == moved || p.Edge != edge) continue;
				if(newrow && p.Row >= row) p.Row++;
				else if(!newrow && p.Row == row && p.Offset == offset) p.Offset++; // The dropped strip wins a tie
			}

			moved.Edge = edge;
			moved.Row = row;
			moved.Offset = offset;
			Compact();
			return true;
		}

		// Renumbers each edge's rows 0..n-1 in their current order
		private void Compact()
		{
			for(int edge = EDGE_AUTO; edge <= EDGE_RIGHT; edge++)
			{
				List<int> rows = new List<int>();
				foreach(ToolbarPlacement p in placements.Values)
					if(p.Edge == edge && !rows.Contains(p.Row)) rows.Add(p.Row);
				rows.Sort();
				foreach(ToolbarPlacement p in placements.Values)
					if(p.Edge == edge) p.Row = rows.IndexOf(p.Row);
			}
		}

		private ToolbarPlacement DefaultPlacement(string id)
		{
			return new ToolbarPlacement(id, (id == MODES) ? EDGE_AUTO : EDGE_TOP, 0, 0);
		}

		// A strip with no usable saved placement goes after everything on its default edge's last row
		private void AppendDefault(string id)
		{
			ToolbarPlacement p = DefaultPlacement(id);
			int lastrow = -1, maxoffset = -1;
			foreach(ToolbarPlacement o in placements.Values)
			{
				if(o.Edge != p.Edge) continue;
				if(o.Row > lastrow) { lastrow = o.Row; maxoffset = -1; }
				if(o.Row == lastrow) maxoffset = Math.Max(maxoffset, o.Offset);
			}
			if(lastrow >= 0) { p.Row = lastrow; p.Offset = maxoffset + 1; }
			placements[id] = p;
		}

		#endregion

		#region ================== Persistence

		// Reads a "toolbars" settings block; anything unusable falls back to its default
		public static ToolbarLayoutModel Load(IDictionary block, IEnumerable<string> horizontalonly = null)
		{
			if(block == null || !(block["version"] is int) || (int)block["version"] != VERSION) return Default(horizontalonly);

			ToolbarLayoutModel m = new ToolbarLayoutModel(horizontalonly);
			m.locked = (block["locked"] is bool) && (bool)block["locked"];

			List<string> invalid = new List<string>();
			foreach(string id in IDS)
			{
				ToolbarPlacement p = ReadPlacement(id, block[id] as IDictionary);
				if(p != null && m.CanDock(id, p.Edge)) m.placements[id] = p;
				else invalid.Add(id);
			}

			m.Compact();
			foreach(string id in invalid) m.AppendDefault(id);
			return m;
		}

		private static ToolbarPlacement ReadPlacement(string id, IDictionary entry)
		{
			if(entry == null || !(entry["edge"] is int) || !(entry["row"] is int) || !(entry["offset"] is int)) return null;
			int row = (int)entry["row"];
			if(row < 0) return null;
			return new ToolbarPlacement(id, (int)entry["edge"], row, Math.Max(0, (int)entry["offset"]));
		}

		// Writes the block as setting paths relative to the "toolbars" key
		public void Save(Action<string, object> write)
		{
			write("version", VERSION);
			write("locked", locked);
			foreach(string id in IDS)
			{
				ToolbarPlacement p = placements[id];
				write(id + ".edge", p.Edge);
				write(id + ".row", p.Row);
				write(id + ".offset", p.Offset);
			}
		}

		#endregion

		#region ================== Row layout

		// Places strips along a row: each at max(offset, end of the previous one), the row shifted back to
		// fit the edge length, and a strip that still doesn't fit shortened, down to minlength, so that
		// every strip keeps a minlength slot on the edge while the edge has room for them all
		public static void LayoutRow(int[] offsets, int[] lengths, int edgelength, int minlength, out int[] positions, out int[] sizes)
		{
			int n = offsets.Length;
			positions = new int[n];
			sizes = (int[])lengths.Clone();

			int end = 0;
			for(int i = 0; i < n; i++)
			{
				positions[i] = Math.Max(offsets[i], end);
				end = positions[i] + sizes[i];
			}

			// Shift back from the far end
			int limit = edgelength;
			for(int i = n - 1; i >= 0; i--)
			{
				positions[i] = Math.Min(positions[i], limit - sizes[i]);
				limit = positions[i];
			}

			// Then forward again, shortening what overruns while leaving each later strip its minimum
			int reserve = 0;
			for(int i = 0; i < n; i++) reserve += Math.Min(minlength, sizes[i]);
			end = 0;
			for(int i = 0; i < n; i++)
			{
				int floor = Math.Min(minlength, sizes[i]);
				reserve -= floor;
				positions[i] = Math.Max(positions[i], end);
				if(positions[i] + sizes[i] > edgelength - reserve)
					sizes[i] = Math.Max(floor, edgelength - reserve - positions[i]);
				end = positions[i] + sizes[i];
			}
		}

		#endregion

		#region ================== Drop targets

		// Depth of a point into an edge, measured from the window side
		private static int Depth(int edge, Rectangle bounds, Point p)
		{
			switch(edge)
			{
				case EDGE_TOP: return p.Y - bounds.Top;
				case EDGE_BOTTOM: return bounds.Bottom - p.Y;
				case EDGE_LEFT: return p.X - bounds.Left;
				default: return bounds.Right - p.X;
			}
		}

		// Depth of a row's window-side boundary
		private static int RowStart(int edge, Rectangle bounds, Rectangle row)
		{
			switch(edge)
			{
				case EDGE_TOP: return row.Top - bounds.Top;
				case EDGE_BOTTOM: return bounds.Bottom - row.Bottom;
				case EDGE_LEFT: return row.Left - bounds.Left;
				default: return bounds.Right - row.Right;
			}
		}

		// Finds where a strip dragged to point p (window client coordinates) would land. grab is how far
		// along the strip the pointer holds it, so the drop keeps the strip under the pointer.
		public ToolbarDropTarget HitTest(string id, Point p, ToolbarEdgeGeometry[] edges, int grab)
		{
			ToolbarDropTarget best = new ToolbarDropTarget();
			int bestdepth = int.MaxValue;
			bool bestinrow = false;

			for(int edge = EDGE_TOP; edge <= EDGE_RIGHT; edge++)
			{
				if(!CanDock(id, edge) || edges[edge] == null) continue;
				Rectangle b = edges[edge].Bounds;
				bool vertical = IsVertical(edge);

				// The pointer has to be within the edge's extent along it
				int along = vertical ? p.Y : p.X;
				int alongstart = vertical ? b.Top : b.Left;
				int alongend = vertical ? b.Bottom : b.Right;
				if(along < alongstart || along >= alongend) continue;

				int depth = Depth(edge, b, p);
				if(depth < 0) continue;

				ToolbarDropTarget t = new ToolbarDropTarget { Valid = true, Edge = edge, Offset = Math.Max(0, along - alongstart - grab) };
				List<Rectangle> rows = edges[edge].Rows;
				bool inrow = false;
				int rowsend = 0;

				for(int r = 0; r < rows.Count; r++)
				{
					int start = RowStart(edge, b, rows[r]);
					int thickness = vertical ? rows[r].Width : rows[r].Height;
					rowsend = start + thickness;
					if(depth < start || depth >= rowsend) continue;

					// The outer and inner quarters open a new row on that side; the middle joins the row
					int within = depth - start;
					if(within < thickness / 4) { t.Row = r; t.NewRow = true; }
					else if(within >= thickness - thickness / 4) { t.Row = r + 1; t.NewRow = true; }
					else { t.Row = r; t.NewRow = false; }
					inrow = true;
					break;
				}

				if(!inrow)
				{
					// Past the last row, within snapping distance: a new innermost row
					if(depth >= rowsend + EDGE_SNAP) continue;
					t.Row = rows.Count;
					t.NewRow = true;
				}

				// A row hit beats a band hit; otherwise the nearest edge wins
				if((inrow && !bestinrow) || (inrow == bestinrow && depth < bestdepth))
				{
					best = t;
					bestdepth = depth;
					bestinrow = inrow;
				}
			}

			return best;
		}

		#endregion
	}
}
