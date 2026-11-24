using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

namespace CodeImp.DoomBuilder.Portals
{
	public enum PortalKind
	{
		LinePortal,
		QuickPortal,
		LinkedLineEE,
		AnchoredSector,
		StackedSectors,
		Skybox,
		SkyPicker,
		PlaneHorizon,
		Copy,
	}

	public enum PortalElementType { Linedef, Sector, Thing }

	public enum PortalSeverity { Warning, Error }

	public enum PortalIssueCode
	{
		NoExit,
		LoneAnchor,
		StackUnpaired,
	}

	public struct PortalElement : IEquatable<PortalElement>
	{
		public readonly PortalElementType Type;
		public readonly int Index;

		public PortalElement(PortalElementType type, int index) { Type = type; Index = index; }

		public bool Equals(PortalElement other) { return Type == other.Type && Index == other.Index; }
		public override bool Equals(object obj) { return obj is PortalElement && Equals((PortalElement)obj); }
		public override int GetHashCode() { return ((int)Type << 28) ^ Index; }
		public override string ToString() { return (Type == PortalElementType.Linedef ? "line " : Type == PortalElementType.Sector ? "sector " : "thing ") + Index; }
	}

	public sealed class PortalEnd
	{
		public readonly PortalElement Element;
		public readonly string Role;

		public PortalEnd(PortalElement element, string role) { Element = element; Role = role; }
	}

	public sealed class PortalIssue
	{
		public readonly PortalSeverity Severity;
		public readonly PortalIssueCode Code;
		public readonly string Message;
		public readonly List<PortalElement> Elements;

		public PortalIssue(PortalSeverity severity, PortalIssueCode code, string message, params PortalElement[] elements)
		{
			Severity = severity;
			Code = code;
			Message = message;
			Elements = new List<PortalElement>(elements);
		}
	}

	public sealed class PortalLink
	{
		public PortalKind Kind;
		public int Type = -1;      // 156 or 57 type, -1 when the kind has none
		public int Plane = -1;     // 57 plane, -1 when the kind has none
		public int Id;             // line id, sector tag or TID that ties the ends together
		public PortalEnd Origin;   // either end may be null when it is missing
		public PortalEnd Destination;
		public bool TwoWay;
		public bool HasDisplacement;
		public Vector2D Displacement;
		public readonly List<int> Sectors = new List<int>();
		public readonly List<PortalEnd> Dependents = new List<PortalEnd>();
		public readonly List<PortalIssue> Issues = new List<PortalIssue>();

		public bool HasErrors { get { return Issues.Exists(i => i.Severity == PortalSeverity.Error); } }
		public bool HasWarnings { get { return Issues.Exists(i => i.Severity == PortalSeverity.Warning); } }

		// Stable across refreshes as long as the ends keep their indices
		public string Key
		{
			get { return Kind + ":" + (Origin != null ? Origin.Element.ToString() : "-") + ":" + (Destination != null ? Destination.Element.ToString() : "-"); }
		}

		public IEnumerable<PortalEnd> Ends
		{
			get
			{
				if(Origin != null) yield return Origin;
				if(Destination != null) yield return Destination;
			}
		}
	}

	public sealed class PortalReport
	{
		public readonly List<PortalLink> Links = new List<PortalLink>();
		public readonly List<PortalIssue> Issues = new List<PortalIssue>();
		public int[] SectorGroup = new int[0];
	}

	// Pairs portal ends the way UZDoom's map loader does (specials.cpp, portal.cpp), tie-breaks included
	public static class PortalAnalyzer
	{
		private const int LINE_LINKED = 3;
		private const int LINE_LINKED_EE = 4;
		private const int SECTOR_ANCHORED = 0;
		private const int SECTOR_COPY = 1;
		private const int SECTOR_SKYBOX = 2;
		private const int SECTOR_PLANE = 3;
		private const int SECTOR_HORIZON = 4;
		private const int SECTOR_COPY_TO_LINE = 5;
		private const int SECTOR_LINKED = 6;

		private sealed class Context
		{
			public PortalMapData Data;
			public PortalCapabilities Caps;
			public PortalReport Report;
			public Dictionary<int, List<PortalLine>> LinesById;
		}

		// A portal that 57 type 1 and 5 lines can copy
		private struct CopySource
		{
			public PortalLink Link;
			public int Tag;
			public int Plane;
			public bool ToLines;
		}

		public static PortalReport Analyze(PortalMapData data, PortalCapabilities caps)
		{
			Context c = new Context { Data = data, Caps = caps, Report = new PortalReport() };
			c.Report.SectorGroup = ComputeGroups(data);
			if(!caps.Any) return c.Report;

			c.LinesById = new Dictionary<int, List<PortalLine>>();
			foreach(PortalLine l in data.Lines)
			{
				foreach(int id in l.Ids)
				{
					if(id == 0) continue;
					List<PortalLine> list;
					if(!c.LinesById.TryGetValue(id, out list)) c.LinesById.Add(id, list = new List<PortalLine>());
					if(list.Count == 0 || list[list.Count - 1] != l) list.Add(l);
				}
			}

			if(caps.LineSetPortal != 0)
			{
				AddLinePortals(c);
				AddLinkedLinesEE(c);
			}
			if(caps.LineQuickPortal != 0) AddQuickPortals(c);
			if(caps.SectorSetPortal != 0) AddSectorPortals(c);
			if(caps.StackedThings) AddStackedSectors(c);
			if(caps.SkyPicker != 0) AddSkyPickers(c);
			return c.Report;
		}

		#region ================== Portal groups

		// Every sector reachable from another through two-sided lines shares a group
		public static int[] ComputeGroups(PortalMapData data)
		{
			int n = data.Sectors.Count;
			int[] parent = new int[n];
			for(int i = 0; i < n; i++) parent[i] = i;
			foreach(PortalLine l in data.Lines)
			{
				if(l.Front < 0 || l.Back < 0 || l.Front >= n || l.Back >= n) continue;
				int a = Find(parent, l.Front), b = Find(parent, l.Back);
				if(a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
			}

			// Number groups densely in order of their lowest sector
			int[] group = new int[n];
			Dictionary<int, int> ids = new Dictionary<int, int>();
			for(int i = 0; i < n; i++)
			{
				int root = Find(parent, i);
				int id;
				if(!ids.TryGetValue(root, out id)) ids.Add(root, id = ids.Count);
				group[i] = id;
			}
			return group;
		}

		private static int Find(int[] parent, int i)
		{
			while(parent[i] != i)
			{
				parent[i] = parent[parent[i]];
				i = parent[i];
			}
			return i;
		}

		#endregion

		#region ================== Line portals

		private static PortalElement Line(PortalLine l) { return new PortalElement(PortalElementType.Linedef, l.Index); }
		private static PortalElement Thing(PortalThing t) { return new PortalElement(PortalElementType.Thing, t.Index); }

		// FindPortalDestination: the first other line carrying the id, in index order; matchaction -1 accepts any special
		private static PortalLine FindDestination(Context c, PortalLine src, int id, int matchaction)
		{
			List<PortalLine> list;
			if(id == 0 || !c.LinesById.TryGetValue(id, out list)) return null;
			foreach(PortalLine l in list)
				if(l != src && (matchaction == -1 || l.Action == matchaction)) return l;
			return null;
		}

		private static void AddLinePortals(Context c)
		{
			int action = c.Caps.LineSetPortal;
			Dictionary<PortalLine, PortalLine> exits = new Dictionary<PortalLine, PortalLine>();
			List<PortalLine> sources = new List<PortalLine>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || l.Args[2] < 0 || l.Args[2] > LINE_LINKED) continue;
				sources.Add(l);
				exits[l] = FindDestination(c, l, l.Args[0], -1);
			}

			HashSet<PortalLine> done = new HashSet<PortalLine>();
			foreach(PortalLine l in sources)
			{
				if(done.Contains(l)) continue;
				done.Add(l);
				PortalLine exit = exits[l];
				PortalLink link = new PortalLink { Kind = PortalKind.LinePortal, Type = l.Args[2], Id = l.Args[0] };
				link.Origin = new PortalEnd(Line(l), "Portal line");

				if(exit == null)
				{
					string msg = l.Args[0] == 0
						? "Line_SetPortal has exit line id 0, so the portal is off"
						: "No other line has id " + l.Args[0] + ", so the portal has no exit";
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.NoExit, msg, Line(l)));
				}
				else
				{
					PortalLine back;
					link.TwoWay = exits.TryGetValue(exit, out back) && back == l;
					if(link.TwoWay) done.Add(exit);
					link.Destination = new PortalEnd(Line(exit), link.TwoWay ? "Portal line" : "Exit line");
					if(l.Args[2] == LINE_LINKED) SetDisplacement(link, exit.V2 - l.V1);
				}
				AddFrontSectors(c, link, l, exit);
				c.Report.Links.Add(link);
			}
		}

		// Type 4: the arg0 = 0 line pairs with every Line_SetPortal line of the same first id with arg0 = 1, whatever its type
		private static void AddLinkedLinesEE(Context c)
		{
			int action = c.Caps.LineSetPortal;
			HashSet<PortalLine> used = new HashSet<PortalLine>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || l.Args[2] != LINE_LINKED_EE || l.Args[0] != 0) continue;
				bool found = false;
				foreach(PortalLine p in c.Data.Lines)
				{
					if(p.Action != action || p.Args[0] != 1 || p.FirstId != l.FirstId) continue;
					found = true;
					used.Add(p);
					PortalLink link = new PortalLink { Kind = PortalKind.LinkedLineEE, Type = LINE_LINKED_EE, Id = l.FirstId, TwoWay = true };
					link.Origin = new PortalEnd(Line(l), "Portal line (arg0 = 0)");
					link.Destination = new PortalEnd(Line(p), "Portal line (arg0 = 1)");
					SetDisplacement(link, p.V2 - l.V1);
					AddFrontSectors(c, link, l, p);
					c.Report.Links.Add(link);
				}
				if(!found)
				{
					PortalLink link = new PortalLink { Kind = PortalKind.LinkedLineEE, Type = LINE_LINKED_EE, Id = l.FirstId };
					link.Origin = new PortalEnd(Line(l), "Portal line (arg0 = 0)");
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.NoExit,
						"No Line_SetPortal line with id " + l.FirstId + " and arg0 = 1 to link with", Line(l)));
					AddFrontSectors(c, link, l, null);
					c.Report.Links.Add(link);
				}
			}

			foreach(PortalLine p in c.Data.Lines)
			{
				if(p.Action != action || p.Args[2] != LINE_LINKED_EE || p.Args[0] != 1 || used.Contains(p)) continue;
				PortalLink link = new PortalLink { Kind = PortalKind.LinkedLineEE, Type = LINE_LINKED_EE, Id = p.FirstId };
				link.Destination = new PortalEnd(Line(p), "Portal line (arg0 = 1)");
				AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.NoExit,
					"No type 4 Line_SetPortal line with id " + p.FirstId + " and arg0 = 0 links to this line", Line(p)));
				AddFrontSectors(c, link, p, null);
				c.Report.Links.Add(link);
			}
		}

		private static void AddQuickPortals(Context c)
		{
			int action = c.Caps.LineQuickPortal;
			Dictionary<PortalLine, PortalLine> partners = new Dictionary<PortalLine, PortalLine>();
			List<PortalLine> sources = new List<PortalLine>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action) continue;
				sources.Add(l);
				partners[l] = FindDestination(c, l, l.FirstId, action);
			}

			HashSet<PortalLine> done = new HashSet<PortalLine>();
			foreach(PortalLine l in sources)
			{
				if(done.Contains(l)) continue;
				done.Add(l);
				PortalLine partner = partners[l];
				bool linked = l.Args[0] == 0;
				PortalLink link = new PortalLink { Kind = PortalKind.QuickPortal, Type = linked ? LINE_LINKED : 0, Id = l.FirstId };
				link.Origin = new PortalEnd(Line(l), "Portal line");

				if(partner == null)
				{
					string msg = l.FirstId == 0
						? "Line_QuickPortal line has no line id, so it has no partner"
						: "No other Line_QuickPortal line has id " + l.FirstId;
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.NoExit, msg, Line(l)));
				}
				else
				{
					PortalLine back;
					link.TwoWay = partners.TryGetValue(partner, out back) && back == l;
					if(link.TwoWay) done.Add(partner);
					link.Destination = new PortalEnd(Line(partner), link.TwoWay ? "Portal line" : "Exit line");
					if(linked) SetDisplacement(link, partner.V2 - l.V1);
				}
				AddFrontSectors(c, link, l, partner);
				c.Report.Links.Add(link);
			}
		}

		private static void AddFrontSectors(Context c, PortalLink link, PortalLine a, PortalLine b)
		{
			if(a != null && a.Front >= 0) AddSector(link, a.Front);
			if(b != null && b.Front >= 0) AddSector(link, b.Front);
		}

		#endregion

		#region ================== Sector portals

		private static void AddSectorPortals(Context c)
		{
			int action = c.Caps.SectorSetPortal;
			List<CopySource> copysources = new List<CopySource>();

			// Anchored portals: only the arg3 = 0 line builds; it takes the first matching arg3 = 1 line in index order
			HashSet<PortalLine> usedrefs = new HashSet<PortalLine>();
			HashSet<string> anchorkeys = new HashSet<string>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || !IsAnchoredType(l.Args[1]) || l.Args[3] != 0) continue;
				anchorkeys.Add(AnchorKey(l));
				PortalLine reference = null;
				foreach(PortalLine o in c.Data.Lines)
				{
					if(o.Action == action && o.Args[3] == 1 && o.Args[0] == l.Args[0] && o.Args[1] == l.Args[1] && o.Args[2] == l.Args[2])
					{
						reference = o;
						break;
					}
				}

				PortalLink link = new PortalLink { Kind = PortalKind.AnchoredSector, Type = l.Args[1], Plane = l.Args[2], Id = l.Args[0] };
				link.Origin = new PortalEnd(Line(l), "Anchor (arg3 = 0)");
				AddTaggedSectors(c, link, l.Args[0]);
				if(reference == null)
				{
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.LoneAnchor,
						"No arg3 = 1 line with the same tag, type and plane, so the portal is never built", Line(l)));
				}
				else
				{
					usedrefs.Add(reference);
					link.Destination = new PortalEnd(Line(reference), "Reference (arg3 = 1)");
					SetDisplacement(link, reference.Middle - l.Middle);
					copysources.Add(new CopySource { Link = link, Tag = l.Args[0], Plane = l.Args[2], ToLines = false });
				}
				c.Report.Links.Add(link);
			}

			// Reference lines no anchor of their tag, type and plane exists for
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || !IsAnchoredType(l.Args[1]) || l.Args[3] != 1 || usedrefs.Contains(l) || anchorkeys.Contains(AnchorKey(l))) continue;
				PortalLink link = new PortalLink { Kind = PortalKind.AnchoredSector, Type = l.Args[1], Plane = l.Args[2], Id = l.Args[0] };
				link.Destination = new PortalEnd(Line(l), "Reference (arg3 = 1)");
				AddTaggedSectors(c, link, l.Args[0]);
				AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.LoneAnchor,
					"No arg3 = 0 line with the same tag, type and plane, so this line does nothing", Line(l)));
				c.Report.Links.Add(link);
			}

			// Skyboxes: each SkyCamCompat thing takes the first type 2 line of its sector
			HashSet<PortalLine> usedsky = new HashSet<PortalLine>();
			if(c.Caps.SkyCamCompat != 0)
			{
				foreach(PortalThing t in c.Data.Things)
				{
					if(t.Type != c.Caps.SkyCamCompat || t.Sector < 0) continue;
					foreach(PortalLine l in c.Data.Lines)
					{
						if(l.Action != action || l.Args[1] != SECTOR_SKYBOX || (l.Front != t.Sector && l.Back != t.Sector)) continue;
						usedsky.Add(l);
						PortalLink link = new PortalLink { Kind = PortalKind.Skybox, Type = SECTOR_SKYBOX, Plane = l.Args[2], Id = l.Args[0] };
						link.Origin = new PortalEnd(Line(l), "Skybox line");
						link.Destination = new PortalEnd(Thing(t), "Viewpoint");
						AddTaggedSectors(c, link, l.Args[0]);
						copysources.Add(new CopySource { Link = link, Tag = l.Args[0], Plane = l.Args[2], ToLines = true });
						c.Report.Links.Add(link);
						break;
					}
				}
			}
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || l.Args[1] != SECTOR_SKYBOX || usedsky.Contains(l)) continue;
				PortalLink link = new PortalLink { Kind = PortalKind.Skybox, Type = SECTOR_SKYBOX, Plane = l.Args[2], Id = l.Args[0] };
				link.Origin = new PortalEnd(Line(l), "Skybox line");
				AddTaggedSectors(c, link, l.Args[0]);
				c.Report.Links.Add(link);
			}

			// Plane and horizon portals stand alone
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || (l.Args[1] != SECTOR_PLANE && l.Args[1] != SECTOR_HORIZON)) continue;
				PortalLink link = new PortalLink { Kind = PortalKind.PlaneHorizon, Type = l.Args[1], Plane = l.Args[2], Id = l.Args[0] };
				link.Origin = new PortalEnd(Line(l), l.Args[1] == SECTOR_PLANE ? "Plane portal line" : "Horizon portal line");
				AddTaggedSectors(c, link, l.Args[0]);
				copysources.Add(new CopySource { Link = link, Tag = l.Args[0], Plane = l.Args[2], ToLines = true });
				c.Report.Links.Add(link);
			}

			// Copies (CopyPortal): type 1 matches the plane or plane 3; type 5 copies only portals built with tolines
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || (l.Args[1] != SECTOR_COPY && l.Args[1] != SECTOR_COPY_TO_LINE)) continue;
				bool copied = false;
				foreach(CopySource s in copysources)
				{
					if(s.Tag != l.Args[3]) continue;
					if(l.Args[1] == SECTOR_COPY ? (l.Args[2] != s.Plane && l.Args[2] != 3) : !s.ToLines) continue;
					s.Link.Dependents.Add(new PortalEnd(Line(l), CopyRole(l)));
					copied = true;
				}
				if(!copied)
				{
					PortalLink link = new PortalLink { Kind = PortalKind.Copy, Type = l.Args[1], Plane = l.Args[2], Id = l.Args[3] };
					link.Origin = new PortalEnd(Line(l), CopyRole(l));
					c.Report.Links.Add(link);
				}
			}
		}

		private static bool IsAnchoredType(int type) { return type == SECTOR_ANCHORED || type == SECTOR_LINKED; }

		private static string AnchorKey(PortalLine l) { return l.Args[0] + "/" + l.Args[1] + "/" + l.Args[2]; }

		private static string CopyRole(PortalLine l)
		{
			if(l.Args[1] == SECTOR_COPY)
				return l.Args[0] == 0 ? "Copy to front sector" : "Copy to sectors tagged " + l.Args[0];
			return l.Args[0] == 0 ? "Copy to this line" : "Copy to lines with id " + l.Args[0];
		}

		private static void AddTaggedSectors(Context c, PortalLink link, int tag)
		{
			foreach(PortalSector s in c.Data.Sectors)
				if(s.MatchesTag(tag)) link.Sectors.Add(s.Index);
		}

		#endregion

		#region ================== Thing portals

		// Each stack point takes the other class's thing with its TID; the TID hash yields the highest index first
		private static void AddStackedSectors(Context c)
		{
			Dictionary<int, PortalThing> uppers = new Dictionary<int, PortalThing>();
			Dictionary<int, PortalThing> lowers = new Dictionary<int, PortalThing>();
			List<int> order = new List<int>();
			foreach(PortalThing t in c.Data.Things)
			{
				bool upper = t.Type == c.Caps.UpperStack;
				if(!upper && t.Type != c.Caps.LowerStack) continue;
				if(t.Tid == 0)
				{
					PortalLink lone = new PortalLink { Kind = PortalKind.StackedSectors };
					SetStackEnd(lone, t, upper);
					AddIssue(c, lone, new PortalIssue(PortalSeverity.Error, PortalIssueCode.StackUnpaired,
						"Stacked sector things with TID 0 never pair", Thing(t)));
					c.Report.Links.Add(lone);
					continue;
				}
				if(!uppers.ContainsKey(t.Tid) && !lowers.ContainsKey(t.Tid)) order.Add(t.Tid);
				(upper ? uppers : lowers)[t.Tid] = t;
			}

			foreach(int tid in order)
			{
				PortalThing u, l;
				uppers.TryGetValue(tid, out u);
				lowers.TryGetValue(tid, out l);
				PortalLink link = new PortalLink { Kind = PortalKind.StackedSectors, Id = tid, TwoWay = u != null && l != null };
				if(u != null) SetStackEnd(link, u, true);
				if(l != null) SetStackEnd(link, l, false);
				if(u != null && l != null)
				{
					SetDisplacement(link, l.Position - u.Position);
				}
				else
				{
					PortalThing t = u ?? l;
					string msg = u != null
						? "No Lower Sector thing (LowerStackLookOnly) has TID " + tid + ", so this floor portal is never built"
						: "No Upper Sector thing (UpperStackLookOnly) has TID " + tid + ", so this ceiling portal is never built";
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.StackUnpaired, msg, Thing(t)));
				}
				c.Report.Links.Add(link);
			}
		}

		private static void SetStackEnd(PortalLink link, PortalThing t, bool upper)
		{
			PortalEnd end = new PortalEnd(Thing(t), upper ? "Upper Sector thing" : "Lower Sector thing");
			if(upper) link.Origin = end; else link.Destination = end;
			link.Id = t.Tid;
			if(t.Sector >= 0) AddSector(link, t.Sector);
		}

		// A picker with arg0 = 0 restores the default sky, which is not a link
		private static void AddSkyPickers(Context c)
		{
			foreach(PortalThing p in c.Data.Things)
			{
				if(p.Type != c.Caps.SkyPicker || p.Args[0] == 0) continue;
				PortalThing viewpoint = null;
				foreach(PortalThing t in c.Data.Things)
					if(t.Tid == p.Args[0] && c.Caps.IsViewpoint(t.Type)) viewpoint = t;

				PortalLink link = new PortalLink { Kind = PortalKind.SkyPicker, Id = p.Args[0] };
				link.Origin = new PortalEnd(Thing(p), "Skybox picker");
				if(viewpoint != null) link.Destination = new PortalEnd(Thing(viewpoint), "Viewpoint");
				if(p.Sector >= 0) AddSector(link, p.Sector);
				c.Report.Links.Add(link);
			}
		}

		#endregion

		#region ================== Helpers

		private static void SetDisplacement(PortalLink link, Vector2D d)
		{
			link.HasDisplacement = true;
			link.Displacement = d;
		}

		private static void AddSector(PortalLink link, int sector)
		{
			if(!link.Sectors.Contains(sector)) link.Sectors.Add(sector);
		}

		private static void AddIssue(Context c, PortalLink link, PortalIssue issue)
		{
			link.Issues.Add(issue);
			c.Report.Issues.Add(issue);
		}

		#endregion
	}
}
