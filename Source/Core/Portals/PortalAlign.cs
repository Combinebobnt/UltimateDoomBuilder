using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

namespace CodeImp.DoomBuilder.Portals
{
	public enum PortalAlignHandle
	{
		None,      // nothing can be dragged; Refusal says why
		Thing,     // the destination stack thing
		Island,    // every vertex and thing of the destination end's portal group
		Repick,    // the arg3 = 1 anchor moves to another line
	}

	// What align mode may move for one link
	public sealed class PortalAlignTarget
	{
		public PortalLink Link;
		public PortalAlignHandle Handle;
		public string Refusal;
		public bool CanMakeExact;
		public string MakeExactRefusal;
		public Vector2D HandlePosition;
		public readonly List<int> OriginSectors = new List<int>();   // drawn ghosted at the displacement
		public readonly HashSet<int> IslandSectors = new HashSet<int>();
		public readonly List<int> IslandLines = new List<int>();     // lines with a side in the island
		public readonly List<int> IslandVertices = new List<int>();

		public bool CanAlign { get { return Handle != PortalAlignHandle.None || CanMakeExact; } }
	}

	// One align edit over the snapshot: moved vertices, moved portal things and rewritten lines
	public sealed class PortalAlignEdit
	{
		public Vector2D Delta;
		public int RepickLine = -1;
		public readonly Dictionary<int, Vector2D> Vertices = new Dictionary<int, Vector2D>();
		public readonly Dictionary<int, Vector2D> Things = new Dictionary<int, Vector2D>();
		public readonly List<PortalLineEdit> LineEdits = new List<PortalLineEdit>();

		public bool IsEmpty { get { return Vertices.Count == 0 && Things.Count == 0 && LineEdits.Count == 0; } }
	}

	public sealed class PortalAlignChange
	{
		public PortalLink Before;
		public PortalLink After;   // null when the move breaks the link up
	}

	public sealed class PortalAlignCheck
	{
		public PortalLink Link;    // the aligned link after the edit, null when it no longer pairs
		public readonly List<PortalAlignChange> Changed = new List<PortalAlignChange>();
		public readonly List<PortalIssue> NewErrors = new List<PortalIssue>();
		public readonly List<PortalIssue> NewWarnings = new List<PortalIssue>();
		public int Overlaps;       // foreign lines the moved island's bounds newly meet
	}

	// Plans align mode's moves over the snapshot and checks them by analysing a copy with the move applied
	public static class PortalAlign
	{
		private const double EPSILON = 1e-6;

		public static PortalAlignTarget Target(PortalMapData data, PortalCapabilities caps, PortalReport report, PortalLink link)
		{
			PortalAlignTarget t = new PortalAlignTarget { Link = link };
			if(link == null || link.Origin == null || link.Destination == null || !link.HasDisplacement)
				return Refuse(t, "Only a linked line pair, an anchored sector portal or stacked sectors have an offset to align.");

			PortalElement o = link.Origin.Element, d = link.Destination.Element;
			if(link.Kind == PortalKind.StackedSectors)
			{
				if(o.Type != PortalElementType.Thing || d.Type != PortalElementType.Thing) return Refuse(t, "This link has no stack things.");
				t.Handle = PortalAlignHandle.Thing;
				t.HandlePosition = data.Things[d.Index].Position;
				int sector = data.Things[o.Index].Sector;
				if(sector >= 0) t.OriginSectors.Add(sector);
				return t;
			}
			if(o.Type != PortalElementType.Linedef || d.Type != PortalElementType.Linedef) return Refuse(t, "This link has no portal lines.");

			PortalLine ol = data.Lines[o.Index], dl = data.Lines[d.Index];
			t.HandlePosition = dl.Middle;
			if(link.Kind == PortalKind.AnchoredSector) t.OriginSectors.AddRange(link.Sectors);
			else if(SideSector(ol) >= 0) t.OriginSectors.Add(SideSector(ol));
			CheckMakeExact(t, link, ol, dl);

			int[] group = report.SectorGroup;
			int og = SideSector(ol) >= 0 ? group[SideSector(ol)] : -1;
			int dg = SideSector(dl) >= 0 ? group[SideSector(dl)] : -1;
			if(dg < 0)
			{
				if(link.Kind == PortalKind.AnchoredSector) t.Handle = PortalAlignHandle.Repick;
				else Refuse(t, "Line " + dl.Index + " has no sector, so there is no group to move.");
				return t;
			}
			if(og == dg)
			{
				// Both anchors in one group (common for visual portals): only the reference line can move
				if(link.Kind == PortalKind.AnchoredSector) t.Handle = PortalAlignHandle.Repick;
				else Refuse(t, "Both portal lines are in one portal group (connected through two-sided lines), so moving the exit side would move the portal line too.");
				return t;
			}

			for(int s = 0; s < group.Length; s++) if(group[s] == dg) t.IslandSectors.Add(s);
			HashSet<int> vertices = new HashSet<int>();
			foreach(PortalLine l in data.Lines)
			{
				if(!InIsland(t, l)) continue;
				t.IslandLines.Add(l.Index);
				if(l.V1Index < 0 || l.V2Index < 0) return Refuse(t, "The map snapshot has no vertex numbers.");
				vertices.Add(l.V1Index);
				vertices.Add(l.V2Index);
			}
			foreach(PortalLine l in data.Lines)
			{
				if(InIsland(t, l) || !(vertices.Contains(l.V1Index) || vertices.Contains(l.V2Index))) continue;
				int v = vertices.Contains(l.V1Index) ? l.V1Index : l.V2Index;
				t.IslandLines.Clear();
				t.IslandSectors.Clear();
				return Refuse(t, "Vertex " + v + " of the exit side's portal group is also used by line " + l.Index
					+ ", which is outside that group, so moving the group would drag other geometry.");
			}
			t.IslandVertices.AddRange(vertices);
			t.IslandVertices.Sort();
			t.Handle = PortalAlignHandle.Island;
			return t;
		}

		// Moves the handle by delta: the stack thing, or the whole island with the portal things inside it
		public static PortalAlignEdit Move(PortalMapData data, PortalAlignTarget target, Vector2D delta)
		{
			PortalAlignEdit edit = new PortalAlignEdit { Delta = delta };
			if(delta.x == 0 && delta.y == 0) return edit;
			if(target.Handle == PortalAlignHandle.Thing)
			{
				int i = target.Link.Destination.Element.Index;
				edit.Things[i] = data.Things[i].Position + delta;
			}
			else if(target.Handle == PortalAlignHandle.Island)
			{
				foreach(PortalLine l in data.Lines)
				{
					if(!InIsland(target, l)) continue;
					edit.Vertices[l.V1Index] = l.V1 + delta;
					edit.Vertices[l.V2Index] = l.V2 + delta;
				}
				foreach(PortalThing t in data.Things)
					if(t.Sector >= 0 && target.IslandSectors.Contains(t.Sector)) edit.Things[t.Index] = t.Position + delta;
			}
			return edit;
		}

		// Moves the arg3 = 1 anchor's special and args to another line; null with a reason when that line cannot take it
		public static PortalAlignEdit Repick(PortalMapData data, PortalAlignTarget target, int line, out string refusal)
		{
			refusal = null;
			if(target.Handle != PortalAlignHandle.Repick) { refusal = "This link's reference line cannot be re-picked."; return null; }
			PortalLine old = data.Lines[target.Link.Destination.Element.Index];
			if(line < 0 || line >= data.Lines.Count) { refusal = "Pick a line."; return null; }
			if(line == old.Index) return new PortalAlignEdit();
			if(line == target.Link.Origin.Element.Index) { refusal = "Line " + line + " is the anchor line itself."; return null; }
			if(data.Lines[line].Action != 0) { refusal = "Line " + line + " already has an action."; return null; }

			PortalAlignEdit edit = new PortalAlignEdit { RepickLine = line };
			edit.LineEdits.Add(new PortalLineEdit { Index = old.Index, Action = 0, Args = new int[5] });
			edit.LineEdits.Add(new PortalLineEdit { Index = line, Action = old.Action, Args = (int[])old.Args.Clone() });
			return edit;
		}

		// Sets the exit line's vertices so the pair is exactly anti-parallel and equally long, at the rounded midpoint offset
		public static PortalAlignEdit MakeExact(PortalMapData data, PortalAlignTarget target)
		{
			if(!target.CanMakeExact) return null;
			PortalLine o = data.Lines[target.Link.Origin.Element.Index], d = data.Lines[target.Link.Destination.Element.Index];
			Vector2D mid = d.Middle - o.Middle;
			Vector2D offset = new Vector2D(Math.Round(mid.x), Math.Round(mid.y));
			PortalAlignEdit edit = new PortalAlignEdit();
			edit.Vertices[d.V1Index] = o.V2 + offset;
			edit.Vertices[d.V2Index] = o.V1 + offset;
			return edit;
		}

		public static PortalMapData Apply(PortalMapData data, PortalCapabilities caps, PortalAlignEdit edit)
		{
			PortalMapData copy = data.Clone();
			foreach(PortalLine l in copy.Lines)
			{
				Vector2D p;
				if(edit.Vertices.TryGetValue(l.V1Index, out p)) l.V1 = p;
				if(edit.Vertices.TryGetValue(l.V2Index, out p)) l.V2 = p;
			}
			foreach(KeyValuePair<int, Vector2D> pair in edit.Things) copy.Things[pair.Key].Position = pair.Value;
			foreach(PortalLineEdit e in edit.LineEdits)
			{
				copy.Lines[e.Index].Action = e.Action;
				copy.Lines[e.Index].Args = (int[])e.Args.Clone();
			}
			if(caps.Hexen && edit.LineEdits.Count > 0) copy.AssignHexenIds(caps);
			return copy;
		}

		// The aligned link's new offset, every other link whose offset changes, and the issues the edit adds
		public static PortalAlignCheck Check(PortalMapData data, PortalCapabilities caps, PortalReport before, PortalAlignTarget target, PortalAlignEdit edit)
		{
			PortalAlignCheck check = new PortalAlignCheck();
			PortalMapData moved = Apply(data, caps, edit);
			PortalReport after = PortalAnalyzer.Analyze(moved, caps);

			PortalElement origin = target.Link.Origin.Element;
			PortalElement destination = edit.RepickLine >= 0 ? new PortalElement(PortalElementType.Linedef, edit.RepickLine) : target.Link.Destination.Element;
			foreach(PortalLink l in after.Links)
			{
				if(l.Kind != target.Link.Kind || l.Origin == null || l.Destination == null || !l.Origin.Element.Equals(origin) || !l.Destination.Element.Equals(destination)) continue;
				check.Link = l;
				break;
			}

			Dictionary<string, PortalLink> afterbykey = new Dictionary<string, PortalLink>();
			foreach(PortalLink l in after.Links) if(!afterbykey.ContainsKey(l.Key)) afterbykey.Add(l.Key, l);
			foreach(PortalLink l in before.Links)
			{
				if(l.Key == target.Link.Key || !l.HasDisplacement) continue;
				PortalLink a;
				afterbykey.TryGetValue(l.Key, out a);
				if(a != null && a == check.Link) continue;
				if(a != null && a.HasDisplacement && Same(a.Displacement, l.Displacement)) continue;
				check.Changed.Add(new PortalAlignChange { Before = l, After = a });
			}

			HashSet<string> old = new HashSet<string>();
			foreach(PortalIssue i in before.Issues) old.Add(PortalCreation.IssueKey(i));
			foreach(PortalIssue i in after.Issues)
				if(!old.Contains(PortalCreation.IssueKey(i))) (i.Severity == PortalSeverity.Error ? check.NewErrors : check.NewWarnings).Add(i);

			if(target.Handle == PortalAlignHandle.Island && edit.Vertices.Count > 0 && edit.RepickLine < 0) check.Overlaps = CountOverlaps(data, target, edit.Delta);
			return check;
		}

		#region ================== Helpers

		// Only linked line pairs must be exact; a pair sharing a vertex would move its own origin
		private static void CheckMakeExact(PortalAlignTarget t, PortalLink link, PortalLine o, PortalLine d)
		{
			bool linked = link.Kind == PortalKind.LinkedLineEE || ((link.Kind == PortalKind.LinePortal || link.Kind == PortalKind.QuickPortal) && link.Type == 3);
			if(!linked) return;
			if(!link.Issues.Exists(i => i.Code == PortalIssueCode.LinkedGeometry)) { t.MakeExactRefusal = "The two lines already match exactly."; return; }
			if(d.V1Index < 0 || d.V2Index < 0 || d.V1Index == d.V2Index) { t.MakeExactRefusal = "Line " + d.Index + " has no vertex numbers."; return; }
			if(d.V1Index == o.V1Index || d.V1Index == o.V2Index || d.V2Index == o.V1Index || d.V2Index == o.V2Index)
			{
				t.MakeExactRefusal = "Lines " + o.Index + " and " + d.Index + " share a vertex.";
				return;
			}
			t.CanMakeExact = true;
		}

		// Foreign lines meeting the island's bounds after the move but not before
		private static int CountOverlaps(PortalMapData data, PortalAlignTarget target, Vector2D delta)
		{
			double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
			foreach(int i in target.IslandLines)
			{
				PortalLine l = data.Lines[i];
				minx = Math.Min(minx, Math.Min(l.V1.x, l.V2.x)); maxx = Math.Max(maxx, Math.Max(l.V1.x, l.V2.x));
				miny = Math.Min(miny, Math.Min(l.V1.y, l.V2.y)); maxy = Math.Max(maxy, Math.Max(l.V1.y, l.V2.y));
			}
			if(minx > maxx) return 0;

			int count = 0;
			foreach(PortalLine l in data.Lines)
			{
				if(InIsland(target, l)) continue;
				double lminx = Math.Min(l.V1.x, l.V2.x), lmaxx = Math.Max(l.V1.x, l.V2.x);
				double lminy = Math.Min(l.V1.y, l.V2.y), lmaxy = Math.Max(l.V1.y, l.V2.y);
				bool now = lmaxx >= minx + delta.x && lminx <= maxx + delta.x && lmaxy >= miny + delta.y && lminy <= maxy + delta.y;
				bool was = lmaxx >= minx && lminx <= maxx && lmaxy >= miny && lminy <= maxy;
				if(now && !was) count++;
			}
			return count;
		}

		private static bool InIsland(PortalAlignTarget t, PortalLine l)
		{
			return (l.Front >= 0 && t.IslandSectors.Contains(l.Front)) || (l.Back >= 0 && t.IslandSectors.Contains(l.Back));
		}

		private static int SideSector(PortalLine l) { return l.Front >= 0 ? l.Front : l.Back; }

		private static bool Same(Vector2D a, Vector2D b) { return Math.Abs(a.x - b.x) < EPSILON && Math.Abs(a.y - b.y) < EPSILON; }

		private static PortalAlignTarget Refuse(PortalAlignTarget t, string reason)
		{
			t.Handle = PortalAlignHandle.None;
			t.Refusal = reason;
			return t;
		}

		#endregion
	}
}
