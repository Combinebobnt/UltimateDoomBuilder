using System.Collections;
using System.Drawing;
using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	public class ToolbarLayoutModelTests
	{
		private const int TOP = ToolbarLayoutModel.EDGE_TOP;
		private const int BOTTOM = ToolbarLayoutModel.EDGE_BOTTOM;
		private const int LEFT = ToolbarLayoutModel.EDGE_LEFT;
		private const int RIGHT = ToolbarLayoutModel.EDGE_RIGHT;

		private static Hashtable Entry(object edge, object row, object offset)
		{
			Hashtable e = new Hashtable();
			e["edge"] = edge;
			e["row"] = row;
			e["offset"] = offset;
			return e;
		}

		private static Hashtable SavedBlock(ToolbarLayoutModel m)
		{
			Hashtable block = new Hashtable();
			m.Save((path, value) =>
			{
				string[] parts = path.Split('.');
				if(parts.Length == 1) { block[path] = value; return; }
				if(!(block[parts[0]] is Hashtable)) block[parts[0]] = new Hashtable();
				((Hashtable)block[parts[0]])[parts[1]] = value;
			});
			return block;
		}

		private static string Join(List<List<string>> rows)
		{
			return string.Join(" | ", rows.Select(r => string.Join(",", r)));
		}

		[Fact]
		public void DefaultIsOneTopRowInTodaysOrderWithModesOppositeDockers()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			Assert.Equal("file,script,undo,clipboard,prefabs,filters,view,geometry,rendering,testing", Join(m.Rows(TOP, 0)));
			Assert.Equal(ToolbarLayoutModel.EDGE_AUTO, m.Get("modes").Edge);
			Assert.Equal(RIGHT, ToolbarLayoutModel.ResolveEdge(m.Get("modes").Edge, 0)); // Dockers left
			Assert.Equal(LEFT, ToolbarLayoutModel.ResolveEdge(m.Get("modes").Edge, 1)); // Dockers right
			Assert.Equal(LEFT, ToolbarLayoutModel.ResolveEdge(m.Get("modes").Edge, 2)); // No dockers
			Assert.Equal("modes", Join(m.Rows(LEFT, 1)));
			Assert.Equal("", Join(m.Rows(BOTTOM, 0)));

			// A strip moved onto the automatic modes edge gets a row of its own, joining or not
			ToolbarLayoutModel j = ToolbarLayoutModel.Default();
			Assert.True(j.Move("view", LEFT, 0, false, 0));
			Assert.True(j.Move("undo", LEFT, 0, true, 0));
			List<List<string>> left = j.Rows(LEFT, 1);
			Assert.Equal(3, left.Count);
			Assert.Equal("modes", string.Join(",", left[0]));
			Assert.Equal("undo", string.Join(",", left[1]));
			Assert.True(j.Move("testing", RIGHT, 0, false, 0));
			Assert.Equal(2, j.Rows(RIGHT, 0).Count); // Dockers moved: modes still alone on its new edge
			Assert.False(m.Locked);
		}

		[Fact]
		public void SaveLoadRoundTrips()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			Assert.True(m.Move("geometry", LEFT, 0, true, 10));
			Assert.True(m.Move("view", BOTTOM, 0, true, 40));
			Assert.True(m.Move("modes", TOP, 1, true, 0));
			m.Locked = true;

			ToolbarLayoutModel l = ToolbarLayoutModel.Load(SavedBlock(m));
			Assert.True(l.Locked);
			foreach(string id in ToolbarLayoutModel.IDS)
			{
				ToolbarPlacement a = m.Get(id), b = l.Get(id);
				Assert.Equal(a.Edge + "/" + a.Row + "/" + a.Offset, b.Edge + "/" + b.Row + "/" + b.Offset);
			}
		}

		[Fact]
		public void WrongOrMissingVersionLoadsDefault()
		{
			ToolbarLayoutModel moved = ToolbarLayoutModel.Default();
			moved.Move("file", BOTTOM, 0, true, 0);
			string defaults = Join(ToolbarLayoutModel.Default().Rows(TOP, 0));

			foreach(object version in new object[] { null, 2, "1", 1.0 })
			{
				Hashtable block = SavedBlock(moved);
				if(version == null) block.Remove("version"); else block["version"] = version;
				ToolbarLayoutModel l = ToolbarLayoutModel.Load(block);
				Assert.Equal(defaults, Join(l.Rows(TOP, 0)));
				Assert.Equal(TOP, l.Get("file").Edge);
			}
			Assert.Equal(defaults, Join(ToolbarLayoutModel.Load(null).Rows(TOP, 0)));
		}

		[Fact]
		public void InvalidEntriesFallBackPerStrip()
		{
			Hashtable block = new Hashtable();
			block["version"] = ToolbarLayoutModel.VERSION;
			block["locked"] = "yes"; // Not a bool: unlocked
			block["bogus"] = Entry(LEFT, 0, 0); // Unknown id: ignored
			block["file"] = Entry(BOTTOM, 0, 0);
			block["script"] = Entry(9, 0, 0); // Edge out of range
			block["undo"] = Entry(TOP, -3, 0); // Negative row
			block["clipboard"] = Entry(TOP, 0, -50); // Negative offset clamps
			block["prefabs"] = Entry("top", 0, 0); // Not an integer
			block["filters"] = Entry(LEFT, 0, 0); // Refuses vertical edges
			block["view"] = Entry(ToolbarLayoutModel.EDGE_AUTO, 0, 0); // Auto is for modes only
			block["geometry"] = "nonsense";
			block["rendering"] = Entry(TOP, 0, 100);
			block["modes"] = Entry(BOTTOM, 1, 0);
			// testing missing

			ToolbarLayoutModel m = ToolbarLayoutModel.Load(block);
			Assert.False(m.Locked);
			Assert.Equal(0, m.Get("clipboard").Offset);
			Assert.Equal(BOTTOM, m.Get("file").Edge);
			Assert.Equal(BOTTOM, m.Get("modes").Edge);
			Assert.Equal(1, m.Get("modes").Row); // Bottom rows stay in order

			// Invalid and missing strips go after what is on the top row, in default order
			Assert.Equal("clipboard,rendering,script,undo,prefabs,filters,view,geometry,testing", Join(m.Rows(TOP, 0)));
			Assert.Equal("", Join(m.Rows(LEFT, 0)));
		}

		[Fact]
		public void LoadCompactsRowGaps()
		{
			ToolbarLayoutModel d = ToolbarLayoutModel.Default();
			Hashtable block = SavedBlock(d);
			block["file"] = Entry(LEFT, 4, 0);
			block["undo"] = Entry(LEFT, 9, 0);
			block["script"] = Entry(TOP, 7, 0);
			ToolbarLayoutModel m = ToolbarLayoutModel.Load(block);
			Assert.Equal(0, m.Get("file").Row);
			Assert.Equal(1, m.Get("undo").Row);
			Assert.Equal(1, m.Get("script").Row);
			Assert.Equal(0, m.Get("clipboard").Row);
		}

		[Fact]
		public void MoveJoinsNewRowsAndEdges()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();

			// New row after the top row
			Assert.True(m.Move("testing", TOP, 1, true, 0));
			Assert.Equal("testing", string.Join(",", m.Rows(TOP, 0)[1]));

			// New row before everything pushes the old rows down
			Assert.True(m.Move("undo", TOP, 0, true, 0));
			List<List<string>> rows = m.Rows(TOP, 0);
			Assert.Equal(3, rows.Count);
			Assert.Equal("undo", string.Join(",", rows[0]));
			Assert.Equal("testing", string.Join(",", rows[2]));

			// Joining a row places by offset; the dropped strip wins a tie
			Assert.True(m.Move("undo", TOP, 2, false, 0));
			Assert.Equal("undo,testing", string.Join(",", m.Rows(TOP, 0)[1]));

			// Another edge, and a refused one
			Assert.True(m.Move("geometry", LEFT, 0, true, 5));
			Assert.Equal("geometry", Join(m.Rows(LEFT, 0)));
			Assert.False(m.Move("filters", RIGHT, 0, true, 0));
			Assert.Equal(TOP, m.Get("filters").Edge);
			Assert.False(m.Move("view", ToolbarLayoutModel.EDGE_AUTO, 0, false, 0));
		}

		[Fact]
		public void JoinKeepsShownOrderWithoutLeavingGaps()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			List<string> row = m.Rows(TOP, 0)[0];

			// In front of the first strip, then back behind it: the row packs from 0 again
			Assert.True(m.Join("undo", TOP, 0, row, Packed(row),0, 0));
			row = m.Rows(TOP, 0)[0];
			Assert.Equal("undo", row[0]);
			Assert.Equal("file", row[1]);
			Assert.True(m.Join("undo", TOP, 0, row, Packed(row),2, 0)); // Behind file and script
			Assert.Equal(string.Join(",", ToolbarLayoutModel.Default().Rows(TOP, 0)[0]), string.Join(",", m.Rows(TOP, 0)[0]));
			int[] pos, size;
			ToolbarLayoutModel.LayoutRow(Offsets(m, m.Rows(TOP, 0)[0]), Lengths(m.Rows(TOP, 0)[0].Count, 50), 2000, 20, out pos, out size);
			for(int i = 0; i < pos.Length; i++) Assert.Equal(i * 50, pos[i]);

			// A gap asked for is kept, and strips behind it follow it
			row = m.Rows(TOP, 0)[0];
			Assert.True(m.Join("testing", TOP, 0, row, Packed(row),2, 700));
			row = m.Rows(TOP, 0)[0];
			Assert.Equal("testing", row[2]);
			Assert.Equal(700, m.Get("testing").Offset);
			Assert.True(m.Get(row[3]).Offset > 700);
		}

		private static List<bool> Packed(List<string> row)
		{
			List<bool> r = new List<bool>();
			foreach(string id in row) r.Add(true);
			return r;
		}

		private static int[] Offsets(ToolbarLayoutModel m, List<string> ids)
		{
			int[] r = new int[ids.Count];
			for(int i = 0; i < r.Length; i++) r[i] = m.Get(ids[i]).Offset;
			return r;
		}

		private static int[] Lengths(int n, int length)
		{
			int[] r = new int[n];
			for(int i = 0; i < n; i++) r[i] = length;
			return r;
		}

		[Fact]
		public void MovingTheLastStripOffARowRemovesTheRow()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			m.Move("testing", TOP, 1, true, 0);
			m.Move("testing", BOTTOM, 0, true, 0);
			Assert.Equal(1, m.Rows(TOP, 0).Count);
			Assert.Equal(0, m.Get("file").Row);
		}

		[Fact]
		public void HorizontalOnlyIsConfigurable()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			Assert.False(m.CanDock("filters", LEFT));
			Assert.True(m.CanDock("filters", BOTTOM));
			Assert.True(m.CanDock("rendering", RIGHT));
			Assert.True(m.CanDock("modes", ToolbarLayoutModel.EDGE_AUTO));
			Assert.False(m.CanDock("file", ToolbarLayoutModel.EDGE_AUTO));

			// Extra horizontal-only strips hold through loading and reset
			string[] extra = { "rendering" };
			Hashtable block = SavedBlock(ToolbarLayoutModel.Default());
			block["rendering"] = Entry(RIGHT, 0, 0);
			block["locked"] = true;
			ToolbarLayoutModel l = ToolbarLayoutModel.Load(block, extra);
			Assert.Equal(TOP, l.Get("rendering").Edge);
			Assert.False(l.CanDock("rendering", LEFT));
			ToolbarLayoutModel reset = l.CloneDefault();
			Assert.False(reset.CanDock("rendering", RIGHT));
			Assert.True(reset.Locked);
		}

		[Fact]
		public void LayoutRowPacksShiftsAndShortens()
		{
			int[] pos, size;

			// Packing: a strip never overlaps the previous one, and keeps a gap it asked for
			ToolbarLayoutModel.LayoutRow(new[] { 0, 0, 300 }, new[] { 100, 100, 50 }, 1000, 20, out pos, out size);
			Assert.Equal("0,100,300", string.Join(",", pos));
			Assert.Equal("100,100,50", string.Join(",", size));

			// Too far right: shifted back to fit
			ToolbarLayoutModel.LayoutRow(new[] { 0, 900 }, new[] { 100, 200 }, 1000, 20, out pos, out size);
			Assert.Equal("0,800", string.Join(",", pos));

			// Longer than the edge: the last strip is shortened
			ToolbarLayoutModel.LayoutRow(new[] { 0, 0 }, new[] { 300, 300 }, 500, 20, out pos, out size);
			Assert.Equal("0,300", string.Join(",", pos));
			Assert.Equal("300,200", string.Join(",", size));

			// Never shorter than the minimum
			ToolbarLayoutModel.LayoutRow(new[] { 0, 0 }, new[] { 300, 300 }, 310, 20, out pos, out size);
			Assert.Equal(20, size[1]);

			// Every strip stays on the edge: earlier ones give way so later ones keep their minimum
			ToolbarLayoutModel.LayoutRow(new[] { 0, 0, 0 }, new[] { 300, 300, 300 }, 500, 20, out pos, out size);
			for(int i = 0; i < 3; i++)
			{
				Assert.True(size[i] >= 20);
				Assert.True(pos[i] + size[i] <= 500);
				if(i > 0) Assert.True(pos[i] >= pos[i - 1] + size[i - 1]);
			}
		}

		private static ToolbarEdgeGeometry[] Window()
		{
			// 1000x700 client; top edge with one 25 px row at y 24..49, left edge empty at x 0,
			// right edge with one 30 px row, bottom edge empty at y 650
			ToolbarEdgeGeometry[] g = new ToolbarEdgeGeometry[4];
			g[TOP] = new ToolbarEdgeGeometry { Bounds = new Rectangle(0, 24, 1000, 25) };
			g[TOP].Rows.Add(new Rectangle(0, 24, 1000, 25));
			g[BOTTOM] = new ToolbarEdgeGeometry { Bounds = new Rectangle(0, 650, 1000, 0) };
			g[LEFT] = new ToolbarEdgeGeometry { Bounds = new Rectangle(0, 49, 0, 601) };
			g[RIGHT] = new ToolbarEdgeGeometry { Bounds = new Rectangle(970, 49, 30, 601) };
			g[RIGHT].Rows.Add(new Rectangle(970, 49, 30, 601));
			return g;
		}

		[Fact]
		public void HitTestRowsBandsAndBoundaries()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			ToolbarEdgeGeometry[] g = Window();
			ToolbarDropTarget t;

			// Middle of the top row joins it, with the offset less the grab
			t = m.HitTest("undo", new Point(400, 36), g, 10);
			Assert.True(t.Valid);
			Assert.Equal(TOP, t.Edge);
			Assert.Equal(0, t.Row);
			Assert.False(t.NewRow);
			Assert.Equal(390, t.Offset);

			// Outer quarter of the row: new row before it; inner quarter: after it
			t = m.HitTest("undo", new Point(400, 25), g, 0);
			Assert.True(t.NewRow);
			Assert.Equal(0, t.Row);
			t = m.HitTest("undo", new Point(400, 48), g, 0);
			Assert.True(t.NewRow);
			Assert.Equal(1, t.Row);

			// Below the row, within snapping distance: new row 1; beyond it: nothing on the top edge
			t = m.HitTest("undo", new Point(400, 49 + ToolbarLayoutModel.EDGE_SNAP - 1), g, 0);
			Assert.True(t.Valid);
			Assert.Equal(TOP, t.Edge);
			Assert.Equal(1, t.Row);
			t = m.HitTest("undo", new Point(400, 49 + ToolbarLayoutModel.EDGE_SNAP), g, 0);
			Assert.False(t.Valid);

			// An empty edge's band
			t = m.HitTest("undo", new Point(5, 300), g, 0);
			Assert.Equal(LEFT, t.Edge);
			Assert.True(t.NewRow);
			Assert.Equal(0, t.Row);
			t = m.HitTest("undo", new Point(500, 640), g, 0);
			Assert.Equal(BOTTOM, t.Edge);

			// A refused edge offers nothing, and a point above the top edge (the menu) neither
			Assert.False(m.HitTest("filters", new Point(5, 300), g, 0).Valid);
			Assert.False(m.HitTest("undo", new Point(400, 10), g, 0).Valid);

			// Offsets never go negative
			Assert.Equal(0, m.HitTest("undo", new Point(3, 36), g, 50).Offset);
		}

		[Fact]
		public void HitTestPrefersRowsThenTheNearestEdge()
		{
			ToolbarLayoutModel m = ToolbarLayoutModel.Default();
			ToolbarEdgeGeometry[] g = Window();

			// In the right edge's row and within the bottom band at once: the row wins
			ToolbarDropTarget t = m.HitTest("undo", new Point(985, 640), g, 0);
			Assert.Equal(RIGHT, t.Edge);
			Assert.False(t.NewRow);

			// Near the bottom-left corner, closer to the bottom
			t = m.HitTest("undo", new Point(15, 645), g, 0);
			Assert.Equal(BOTTOM, t.Edge);
			t = m.HitTest("undo", new Point(3, 630), g, 0);
			Assert.Equal(LEFT, t.Edge);
		}
	}
}
