using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Portals;

namespace Builder.Tests
{
	public class PortalAlignTests
	{
		private const int SETPORTAL = 156, SECTORPORTAL = 57, UPPER = 9077, LOWER = 9078;

		private static PortalCapabilities Caps(bool hexen = false)
		{
			Dictionary<string, int> actions = new Dictionary<string, int> { { "Line_SetPortal", SETPORTAL }, { "Sector_SetPortal", SECTORPORTAL } };
			Dictionary<string, int> things = new Dictionary<string, int> { { "UpperStackLookOnly", UPPER }, { "LowerStackLookOnly", LOWER } };
			return PortalCapabilities.Create(id => actions.TryGetValue(id, out int n) ? n : 0, cls => things.TryGetValue(cls, out int n) ? n : 0, hexen, true);
		}

		// Builds snapshot lines whose vertices are numbered by position, as a map shares them
		private sealed class Builder
		{
			public readonly PortalMapData D = new PortalMapData();
			private readonly Dictionary<string, int> vertices = new Dictionary<string, int>();

			public PortalLine Line(int action, int[] args, int[] ids, double x1, double y1, double x2, double y2, int front, int back)
			{
				return D.AddLine(action, args, ids, new Vector2D(x1, y1), new Vector2D(x2, y2), front, back, Vertex(x1, y1), Vertex(x2, y2));
			}

			// One-sided walls around a polygon
			public void Walls(int sector, params double[] xy)
			{
				for(int i = 0; i < xy.Length; i += 2)
				{
					int j = (i + 2) % xy.Length;
					Line(0, null, null, xy[i], xy[i + 1], xy[j], xy[j + 1], sector, -1);
				}
			}

			private int Vertex(double x, double y)
			{
				string key = x + "," + y;
				int index;
				if(!vertices.TryGetValue(key, out index)) vertices.Add(key, index = vertices.Count);
				return index;
			}
		}

		// Sector 0 at the origin holds a linked portal line; sectors 1 and 2, joined by a two-sided line, are the exit side.
		// Lines 0 and 1 are the portal pair (offset 1024, 0); thing 0 (sector 0) and thing 1 (sector 2) are stacked sectors (offset 1280, 0).
		private static Builder Layout(double exitTop = 192)
		{
			Builder b = new Builder();
			b.D.AddSector();
			b.D.AddSector();
			b.D.AddSector();
			b.Line(SETPORTAL, new[] { 2, 0, 3 }, new[] { 1 }, 128, 64, 128, 192, 0, 0);
			b.Line(SETPORTAL, new[] { 1, 0, 3 }, new[] { 2 }, 1152, exitTop, 1152, 64, 1, 1);
			b.Walls(0, 0, 0, 256, 0, 256, 256, 0, 256);
			b.Line(0, null, null, 1024, 0, 1280, 0, 1, -1);
			b.Line(0, null, null, 1280, 256, 1024, 256, 1, -1);
			b.Line(0, null, null, 1024, 256, 1024, 0, 1, -1);
			b.Line(0, null, null, 1280, 0, 1280, 256, 1, 2);
			b.Line(0, null, null, 1280, 0, 1536, 0, 2, -1);
			b.Line(0, null, null, 1536, 0, 1536, 256, 2, -1);
			b.Line(0, null, null, 1536, 256, 1280, 256, 2, -1);
			b.D.AddThing(UPPER, 5, null, new Vector2D(32, 32), 0);
			b.D.AddThing(LOWER, 5, null, new Vector2D(1312, 32), 2);
			return b;
		}

		private static PortalLink Find(PortalReport r, PortalKind kind) { return r.Links.Single(l => l.Kind == kind); }

		[Fact]
		public void LinkedLinePair_InDifferentGroups_MovesTheWholeExitGroup()
		{
			Builder b = Layout();
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.LinePortal));

			Assert.Equal(PortalAlignHandle.Island, t.Handle);
			Assert.Equal(new[] { 1, 2 }, t.IslandSectors.OrderBy(s => s).ToArray());
			Assert.Equal(new[] { 0 }, t.OriginSectors.ToArray());

			PortalAlignEdit edit = PortalAlign.Move(b.D, t, new Vector2D(64, 32));
			HashSet<int> expected = new HashSet<int>();
			foreach(PortalLine l in b.D.Lines)
				if(l.Front == 1 || l.Front == 2 || l.Back == 1 || l.Back == 2) { expected.Add(l.V1Index); expected.Add(l.V2Index); }
			Assert.Equal(expected.OrderBy(v => v).ToArray(), edit.Vertices.Keys.OrderBy(v => v).ToArray());
			Assert.Equal(new[] { 1 }, edit.Things.Keys.ToArray());

			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(), r, t, edit);
			Assert.Equal(1088, check.Link.Displacement.x);
			Assert.Equal(32, check.Link.Displacement.y);
			PortalAlignChange change = Assert.Single(check.Changed);
			Assert.Equal(PortalKind.StackedSectors, change.Before.Kind);
			Assert.Equal(1344, change.After.Displacement.x);
			Assert.Empty(check.NewErrors);
			Assert.Equal(0, check.Overlaps);
		}

		[Fact]
		public void IslandMove_IsRefusedWhenAVertexIsSharedWithOutsideGeometry()
		{
			Builder b = Layout();
			b.D.AddSector();
			b.Line(0, null, null, 1536, 0, 1600, -64, 3, -1);
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.LinePortal));

			Assert.Equal(PortalAlignHandle.None, t.Handle);
			Assert.Contains("line " + (b.D.Lines.Count - 1), t.Refusal);
			Assert.Empty(PortalAlign.Move(b.D, t, new Vector2D(8, 0)).Vertices);
		}

		[Fact]
		public void IslandMove_IsRefusedWhenASectorlessLineTouchesTheIsland()
		{
			Builder b = Layout();
			b.Line(0, null, null, 1536, 0, 1600, -64, -1, -1);
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.LinePortal));

			Assert.Equal(PortalAlignHandle.None, t.Handle);
			Assert.Contains("line " + (b.D.Lines.Count - 1), t.Refusal);
		}

		[Fact]
		public void IslandMove_CountsOutsideLinesItsBoundsNewlyMeet()
		{
			Builder b = Layout();
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.LinePortal));

			PortalAlignCheck onto = PortalAlign.Check(b.D, Caps(), r, t, PortalAlign.Move(b.D, t, new Vector2D(-1024, 0)));
			Assert.Equal(5, onto.Overlaps);  // sector 0's four walls and its portal line
		}

		[Fact]
		public void StackedSectors_MoveOnlyTheDestinationThing()
		{
			Builder b = Layout();
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalLink stack = Find(r, PortalKind.StackedSectors);
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, stack);

			Assert.Equal(PortalAlignHandle.Thing, t.Handle);
			Assert.False(t.CanMakeExact);
			PortalAlignEdit edit = PortalAlign.Move(b.D, t, new Vector2D(-32, 16));
			Assert.Empty(edit.Vertices);
			Assert.Equal(new[] { stack.Destination.Element.Index }, edit.Things.Keys.ToArray());

			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(), r, t, edit);
			Assert.Equal(1248, check.Link.Displacement.x);
			Assert.Equal(16, check.Link.Displacement.y);
			Assert.Empty(check.Changed);
		}

		// The engine takes the exit's group from its front sector, so a back-only exit has no group to move
		[Fact]
		public void ExitLine_WithOnlyABackSector_IsRefused()
		{
			Builder b = Layout();
			b.D.Lines[1].Front = -1;
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.LinePortal));

			Assert.Equal(PortalAlignHandle.None, t.Handle);
			Assert.Contains("no sector", t.Refusal);
		}

		[Fact]
		public void LinePair_InOneGroup_IsRefused()
		{
			Builder b = Layout();
			b.D.Lines[1].Front = b.D.Lines[1].Back = 0;
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.LinePortal));

			Assert.Equal(PortalAlignHandle.None, t.Handle);
			Assert.Contains("one portal group", t.Refusal);
		}

		// One sector tagged 7: line 4 anchors a Sector_SetPortal of the given type, line 5 is its arg3 = 1 reference,
		// line 6 is free and line 7 has an action
		private static Builder Anchored(int type = 0)
		{
			Builder b = new Builder();
			b.D.AddSector(7);
			b.Walls(0, 0, 0, 512, 0, 512, 512, 0, 512);
			b.Line(SECTORPORTAL, new[] { 7, type, 0, 0, 128 }, null, 64, 64, 64, 128, 0, 0);
			b.Line(SECTORPORTAL, new[] { 7, type, 0, 1, 128 }, null, 256, 64, 256, 128, 0, 0);
			b.Line(0, null, null, 384, 64, 384, 192, 0, 0);
			b.Line(1, null, null, 448, 64, 448, 192, 0, 0);
			return b;
		}

		[Fact]
		public void AnchoredPortal_InOneGroup_RepicksTheReferenceLine()
		{
			Builder b = Anchored();
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.AnchoredSector));
			Assert.Equal(PortalAlignHandle.Repick, t.Handle);

			string refusal;
			Assert.Null(PortalAlign.Repick(b.D, t, 7, out refusal));
			Assert.Contains("already has an action", refusal);
			Assert.Null(PortalAlign.Repick(b.D, t, 4, out refusal));
			Assert.Contains("anchor line", refusal);

			PortalAlignEdit edit = PortalAlign.Repick(b.D, t, 6, out refusal);
			Assert.Null(refusal);
			Assert.Equal(0, edit.LineEdits.Single(e => e.Index == 5).Action);
			Assert.Equal(new[] { 7, 0, 0, 1, 128 }, edit.LineEdits.Single(e => e.Index == 6).Args);

			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(), r, t, edit);
			Assert.Equal(6, check.Link.Destination.Element.Index);
			Assert.Equal(320, check.Link.Displacement.x);
			Assert.Equal(32, check.Link.Displacement.y);
			Assert.Empty(check.NewErrors);
			Assert.Empty(check.NewWarnings);
		}

		// The engine disables a linked anchored portal in one group either way, so the moved issue is not new
		[Fact]
		public void Repick_CarriesAnIssueNamingTheOldReferenceLineAlong()
		{
			Builder b = Anchored(6);
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			Assert.True(r.Issues.Exists(i => i.Code == PortalIssueCode.SameGroup));
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.AnchoredSector));

			string refusal;
			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(), r, t, PortalAlign.Repick(b.D, t, 6, out refusal));
			Assert.Equal(6, check.Link.Destination.Element.Index);
			Assert.Empty(check.NewErrors);
		}

		[Fact]
		public void Repick_InHexenFormat_ReassignsIdsAndFindsTheMovedLink()
		{
			Builder b = Anchored();
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps(true));
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(true), r, Find(r, PortalKind.AnchoredSector));
			Assert.Equal(PortalAlignHandle.Repick, t.Handle);

			string refusal;
			PortalAlignEdit edit = PortalAlign.Repick(b.D, t, 6, out refusal);
			PortalMapData moved = PortalAlign.Apply(b.D, Caps(true), edit);
			Assert.Equal(SECTORPORTAL, moved.Lines[6].Action);
			Assert.Equal(0, moved.Lines[5].Action);

			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(true), r, t, edit);
			Assert.Equal(6, check.Link.Destination.Element.Index);
			Assert.Equal(320, check.Link.Displacement.x);
			Assert.Empty(check.NewErrors);
		}

		[Fact]
		public void AnchoredPortal_WithASectorlessReferenceLine_RepicksIt()
		{
			Builder b = Anchored();
			b.D.Lines[5].Front = b.D.Lines[5].Back = -1;
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, Find(r, PortalKind.AnchoredSector));
			Assert.Equal(PortalAlignHandle.Repick, t.Handle);

			string refusal;
			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(), r, t, PortalAlign.Repick(b.D, t, 6, out refusal));
			Assert.Null(refusal);
			Assert.Equal(6, check.Link.Destination.Element.Index);
		}

		[Fact]
		public void MakeExact_IsRefusedWhenTheExitLineSharesAVertexOutsideItsGroup()
		{
			Builder inside = Layout(200);
			inside.Line(0, null, null, 1152, 64, 1100, 64, 1, -1);
			PortalReport ir = PortalAnalyzer.Analyze(inside.D, Caps());
			Assert.True(PortalAlign.Target(inside.D, Caps(), ir, Find(ir, PortalKind.LinePortal)).CanMakeExact);

			Builder outside = Layout(200);
			outside.D.AddSector();
			outside.Line(0, null, null, 1152, 64, 2000, 64, 3, -1);
			PortalReport outr = PortalAnalyzer.Analyze(outside.D, Caps());
			PortalAlignTarget t = PortalAlign.Target(outside.D, Caps(), outr, Find(outr, PortalKind.LinePortal));
			Assert.False(t.CanMakeExact);
			Assert.Contains("line " + (outside.D.Lines.Count - 1), t.MakeExactRefusal);
			Assert.Null(PortalAlign.MakeExact(outside.D, t));

			Builder origin = Layout(200);
			origin.D.Lines[1].V2Index = origin.D.Lines[0].V1Index;
			PortalReport orr = PortalAnalyzer.Analyze(origin.D, Caps());
			PortalAlignTarget o = PortalAlign.Target(origin.D, Caps(), orr, Find(orr, PortalKind.LinePortal));
			Assert.False(o.CanMakeExact);
			Assert.Contains("share a vertex", o.MakeExactRefusal);
		}

		[Fact]
		public void MakeExact_ReversesTheExitLineAtTheRoundedMidpointOffset()
		{
			Builder b = Layout(200);
			PortalReport r = PortalAnalyzer.Analyze(b.D, Caps());
			PortalLink link = Find(r, PortalKind.LinePortal);
			Assert.True(link.Issues.Exists(i => i.Code == PortalIssueCode.LinkedGeometry));
			PortalAlignTarget t = PortalAlign.Target(b.D, Caps(), r, link);
			Assert.True(t.CanMakeExact);

			PortalAlignEdit edit = PortalAlign.MakeExact(b.D, t);
			Assert.Equal(2, edit.Vertices.Count);
			PortalAlignCheck check = PortalAlign.Check(b.D, Caps(), r, t, edit);
			Assert.False(check.Link.Issues.Exists(i => i.Code == PortalIssueCode.LinkedGeometry));
			Assert.Equal(1024, check.Link.Displacement.x);
			Assert.Equal(4, check.Link.Displacement.y);
			Assert.Empty(check.NewErrors);

			Builder e = Layout();
			PortalReport er = PortalAnalyzer.Analyze(e.D, Caps());
			PortalAlignTarget exact = PortalAlign.Target(e.D, Caps(), er, Find(er, PortalKind.LinePortal));
			Assert.False(exact.CanMakeExact);
			Assert.NotNull(exact.MakeExactRefusal);
		}
	}
}
