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
		Ignored,   // a portal special with a type the engine does not build
	}

	public enum PortalElementType { Linedef, Sector, Thing }

	public enum PortalSeverity { Warning, Error }

	public enum PortalIssueCode
	{
		NoExit,
		NoReturn,
		ReturnNotLinked,
		AmbiguousExit,
		OneSidedTraversable,
		InteractivePlaneAlign,
		LinkedGeometry,
		LengthMismatch,
		SameGroup,
		DisplacementConflict,
		UnknownType,
		TypeUnsupported,
		ZeroSectorTag,
		BadPlane,
		LoneAnchor,
		DuplicateAnchor,
		NoTaggedSector,
		LinkedSloped,
		OpacityMismatch,
		CopyNoSource,
		CopyToLineAnchored,
		StackUnpaired,
		StackDuplicate,
		PickerNoViewpoint,
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
		private const int LINE_VISUAL = 0;
		private const int LINE_TELEPORT = 1;
		private const int LINE_INTERACTIVE = 2;
		private const int LINE_LINKED = 3;
		private const int LINE_LINKED_EE = 4;
		private const int SECTOR_ANCHORED = 0;
		private const int SECTOR_COPY = 1;
		private const int SECTOR_SKYBOX = 2;
		private const int SECTOR_PLANE = 3;
		private const int SECTOR_HORIZON = 4;
		private const int SECTOR_COPY_TO_LINE = 5;
		private const int SECTOR_LINKED = 6;

		// The engine compares offsets exactly; this only absorbs the editor's float noise
		private const double EPSILON = 1e-6;

		private sealed class Context
		{
			public PortalMapData Data;
			public PortalCapabilities Caps;
			public PortalReport Report;
			public Dictionary<int, List<PortalLine>> LinesById;
			public readonly List<LineEntry> Entries = new List<LineEntry>();
			public readonly Dictionary<PortalLine, LineEntry> LastEntry = new Dictionary<PortalLine, LineEntry>();
		}

		// One engine line portal (one direction), in the order the map loader creates them
		private sealed class LineEntry
		{
			public int Order;          // the index of the line whose special creates it
			public int Sub;            // creation order within that line (Eternity-style pairs make several)
			public PortalLine Origin;
			public PortalLine Dest;
			public int Type;           // the current type, demoted as the engine demotes it
			public int Align;          // plane align (Line_SetPortal arg3), 0 for the other kinds
			public PortalLink Link;
		}

		// One direction's offset between two portal groups, and the link that set it first
		private sealed class GroupOffset
		{
			public Vector2D Offset;
			public PortalLink Link;
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

			UpdateLinePortals(c);
			if(caps.ModernSectorPortals) CheckLinkedGroups(c);
			CheckLengths(c);
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

		// CreateLinkedPortals: linked sector portals first, then linked line portals, each fixing the offset between
		// its two groups; then every pair of groups must have mirrored offsets both ways
		private static void CheckLinkedGroups(Context c)
		{
			int[] group = c.Report.SectorGroup;
			Dictionary<long, GroupOffset> offsets = new Dictionary<long, GroupOffset>();

			foreach(PortalLink link in c.Report.Links)
			{
				if(link.Kind != PortalKind.AnchoredSector || link.Type != SECTOR_LINKED || !link.HasDisplacement) continue;
				PortalLine a = c.Data.Lines[link.Origin.Element.Index], r = c.Data.Lines[link.Destination.Element.Index];
				if(a.Front < 0 || r.Front < 0) continue;
				int ga = group[a.Front], gb = group[r.Front];
				if(ga == gb)
				{
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.SameGroup,
						"The anchors' front sectors " + a.Front + " and " + r.Front + " connect through two-sided lines, so the engine disables this linked portal",
						Line(a), Line(r)));
					continue;
				}
				GroupOffset first = SetGroupOffset(offsets, ga, gb, link.Displacement, link);
				if(first != null)
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.DisplacementConflict,
						OffsetConflictText(link, first) + ", so the engine disables this one", Line(a), Line(r)));
			}

			foreach(LineEntry e in c.Entries)
			{
				if(e.Type != LINE_LINKED || e.Dest == null || e.Origin.Front < 0 || e.Dest.Front < 0) continue;
				LineEntry back;
				c.LastEntry.TryGetValue(e.Dest, out back);
				int ga = group[e.Origin.Front], gb = group[e.Dest.Front];
				if(ga == gb)
				{
					e.Type = LINE_TELEPORT;
					if(back != null) back.Type = LINE_TELEPORT;
					AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.SameGroup,
						"The front sectors " + e.Origin.Front + " and " + e.Dest.Front + " of lines " + e.Origin.Index + " and " + e.Dest.Index
						+ " connect through two-sided lines, so the engine makes both teleport portals", Line(e.Origin), Line(e.Dest)));
					continue;
				}
				Vector2D d = e.Dest.V2 - e.Origin.V1;
				GroupOffset first = SetGroupOffset(offsets, ga, gb, d, e.Link);
				if(first != null)
				{
					e.Type = LINE_TELEPORT;
					if(back != null) back.Type = LINE_TELEPORT;
					AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.DisplacementConflict,
						"Line " + e.Origin.Index + "'s offset " + Num(d) + " disagrees with " + Describe(first.Link) + " (" + Num(first.Offset)
						+ ") between the same groups, so the engine makes lines " + e.Origin.Index + " and " + e.Dest.Index + " teleport portals",
						Line(e.Origin), Line(e.Dest)));
				}
			}

			// "Link offset mismatch": the way back between two groups must be the exact reverse of the way there
			foreach(KeyValuePair<long, GroupOffset> pair in offsets)
			{
				int x = (int)(pair.Key >> 32), y = (int)(pair.Key & 0xffffffff);
				GroupOffset there = pair.Value, back;
				if(x >= y || !offsets.TryGetValue(GroupKey(y, x), out back) || Near(there.Offset, -back.Offset)) continue;

				if(there.Link == back.Link)
				{
					PortalLine a = c.Data.Lines[there.Link.Origin.Element.Index], b = c.Data.Lines[there.Link.Destination.Element.Index];
					AddIssue(c, there.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.LinkedGeometry,
						"Linked lines " + a.Index + " and " + b.Index + " are not exactly reversed copies of each other (equal length, opposite direction), "
						+ "so their offsets there (" + Num(there.Offset) + ") and back (" + Num(back.Offset) + ") disagree and the engine reports a link offset mismatch",
						Line(a), Line(b)));
				}
				else
				{
					List<PortalElement> elements = new List<PortalElement>();
					foreach(PortalEnd end in back.Link.Ends) elements.Add(end.Element);
					foreach(PortalEnd end in there.Link.Ends) elements.Add(end.Element);
					AddIssue(c, back.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.DisplacementConflict,
						"This portal's offset back (" + Num(back.Offset) + ") is not the reverse of " + Describe(there.Link) + " (" + Num(there.Offset)
						+ "), so the engine reports a link offset mismatch", elements.ToArray()));
				}
			}
		}

		private static long GroupKey(int from, int to) { return ((long)from << 32) | (uint)to; }

		// Records the first offset from one group to another; returns that first record when it disagrees
		private static GroupOffset SetGroupOffset(Dictionary<long, GroupOffset> offsets, int from, int to, Vector2D offset, PortalLink link)
		{
			GroupOffset first;
			long key = GroupKey(from, to);
			if(!offsets.TryGetValue(key, out first))
			{
				offsets.Add(key, new GroupOffset { Offset = offset, Link = link });
				return null;
			}
			return Near(first.Offset, offset) ? null : first;
		}

		private static string OffsetConflictText(PortalLink link, GroupOffset first)
		{
			return "Its offset " + Num(link.Displacement) + " disagrees with " + Describe(first.Link) + " (" + Num(first.Offset) + ") between the same groups";
		}

		private static string Describe(PortalLink link)
		{
			string a = link.Origin != null ? link.Origin.Element.ToString() : "?";
			string b = link.Destination != null ? link.Destination.Element.ToString() : "?";
			return "the portal " + a + " - " + b;
		}

		private static bool Near(Vector2D a, Vector2D b) { return Math.Abs(a.x - b.x) <= EPSILON && Math.Abs(a.y - b.y) <= EPSILON; }

		private static string Num(double v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
		private static string Num(Vector2D v) { return Num(v.x) + ", " + Num(v.y); }

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
				if(l.Action != action) continue;
				int type = l.Args[2];
				if(type == LINE_LINKED_EE && (l.Args[0] == 0 || l.Args[0] == 1)) continue;  // AddLinkedLinesEE
				if(type < LINE_VISUAL || type > LINE_LINKED)
				{
					AddIgnored(c, l, type, PortalIssueCode.UnknownType, type == LINE_LINKED_EE
						? "Line_SetPortal type 4 needs arg0 0 or 1, so the engine ignores this line"
						: "Line_SetPortal type " + type + " is not 0 to 4, so the engine ignores this line");
					continue;
				}
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
				AddEntry(c, l, exit, l.Args[2], l.Args[3], link, 0);

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
					// Ends of different types stay two one-way rows, so each row shows its own type
					link.TwoWay = exits.TryGetValue(exit, out back) && back == l && exit.Args[2] == l.Args[2];
					link.Destination = new PortalEnd(Line(exit), link.TwoWay ? "Portal line" : "Exit line");
					if(l.Args[2] == LINE_LINKED) SetDisplacement(link, exit.V2 - l.V1);
					CheckAmbiguousExit(c, link, l, l.Args[0], -1, exit);
					if(link.TwoWay)
					{
						done.Add(exit);
						AddEntry(c, exit, l, exit.Args[2], exit.Args[3], link, 0);
						CheckAmbiguousExit(c, link, exit, exit.Args[0], -1, l);
					}
				}
				AddFrontSectors(c, link, l, exit);
				c.Report.Links.Add(link);
			}
		}

		// FindDestination takes the lowest index; more candidates are worth a warning, since reordering lines changes the pick
		private static void CheckAmbiguousExit(Context c, PortalLink link, PortalLine src, int id, int matchaction, PortalLine picked)
		{
			List<PortalLine> list;
			if(id == 0 || !c.LinesById.TryGetValue(id, out list)) return;
			List<PortalElement> others = new List<PortalElement>();
			foreach(PortalLine l in list)
				if(l != src && l != picked && (matchaction == -1 || l.Action == matchaction)) others.Add(Line(l));
			if(others.Count == 0) return;

			others.Insert(0, Line(picked));
			others.Insert(0, Line(src));
			AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.AmbiguousExit,
				(others.Count - 1) + " other lines carry id " + id + "; line " + src.Index + " exits at line " + picked.Index + ", the lowest index",
				others.ToArray()));
		}

		private static void AddEntry(Context c, PortalLine origin, PortalLine dest, int type, int align, PortalLink link, int sub)
		{
			c.Entries.Add(new LineEntry { Order = origin.Index, Sub = sub, Origin = origin, Dest = dest, Type = type, Align = align, Link = link });
		}

		// Type 4: the arg0 = 0 line pairs with every Line_SetPortal line of the same first id with arg0 = 1, whatever its type
		private static void AddLinkedLinesEE(Context c)
		{
			int action = c.Caps.LineSetPortal;
			HashSet<PortalLine> used = new HashSet<PortalLine>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || l.Args[2] != LINE_LINKED_EE || l.Args[0] != 0) continue;
				int sub = 0;
				foreach(PortalLine p in c.Data.Lines)
				{
					if(p.Action != action || p.Args[0] != 1 || p.FirstId != l.FirstId) continue;
					used.Add(p);
					PortalLink link = new PortalLink { Kind = PortalKind.LinkedLineEE, Type = LINE_LINKED_EE, Id = l.FirstId, TwoWay = true };
					link.Origin = new PortalEnd(Line(l), "Portal line (arg0 = 0)");
					link.Destination = new PortalEnd(Line(p), "Portal line (arg0 = 1)");
					SetDisplacement(link, p.V2 - l.V1);

					// The engine creates the portal and its backlink here, both linked
					c.Entries.Add(new LineEntry { Order = l.Index, Sub = sub++, Origin = l, Dest = p, Type = LINE_LINKED, Link = link });
					c.Entries.Add(new LineEntry { Order = l.Index, Sub = sub++, Origin = p, Dest = l, Type = LINE_LINKED, Link = link });
					AddFrontSectors(c, link, l, p);
					c.Report.Links.Add(link);
				}
				if(sub == 0)
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
				PortalLink link = new PortalLink { Kind = PortalKind.QuickPortal, Type = linked ? LINE_LINKED : LINE_VISUAL, Id = l.FirstId };
				link.Origin = new PortalEnd(Line(l), "Portal line");
				AddEntry(c, l, partner, link.Type, 0, link, 0);

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
					link.TwoWay = partners.TryGetValue(partner, out back) && back == l && (partner.Args[0] == 0) == linked;
					link.Destination = new PortalEnd(Line(partner), link.TwoWay ? "Portal line" : "Exit line");
					if(linked) SetDisplacement(link, partner.V2 - l.V1);
					CheckAmbiguousExit(c, link, l, l.FirstId, action, partner);
					if(link.TwoWay)
					{
						done.Add(partner);
						AddEntry(c, partner, l, link.Type, 0, link, 0);
						CheckAmbiguousExit(c, link, partner, partner.FirstId, action, l);
					}
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

		private static string LineTypeName(int type)
		{
			switch(type)
			{
				case LINE_VISUAL: return "visual";
				case LINE_TELEPORT: return "teleport";
				case LINE_INTERACTIVE: return "interactive";
				default: return "linked";
			}
		}

		// The map loader's spawn-time plane align check, then UpdatePortal for each line portal in creation order
		private static void UpdateLinePortals(Context c)
		{
			c.Entries.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Sub.CompareTo(b.Sub));
			foreach(LineEntry e in c.Entries) c.LastEntry[e.Origin] = e;  // line_t::portalindex: the last portal made for it

			foreach(LineEntry e in c.Entries)
			{
				if(e.Type == LINE_INTERACTIVE && (e.Align == 1 || e.Align == 2))
				{
					e.Type = LINE_TELEPORT;
					AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.InteractivePlaneAlign,
						"Interactive portals cannot align planes (arg3 = " + e.Align + "), so the engine makes line " + e.Origin.Index + " a teleport portal", Line(e.Origin)));
				}
			}

			foreach(LineEntry e in c.Entries)
			{
				if(e.Type != LINE_VISUAL && e.Origin.Back < 0)
				{
					AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.OneSidedTraversable,
						"A " + LineTypeName(e.Type) + " portal needs a back sector, so the engine makes one-sided line " + e.Origin.Index
						+ " a visual portal (unless the line belongs to a polyobject)", Line(e.Origin)));
					e.Type = LINE_VISUAL;
				}
				if(e.Dest == null) continue;

				LineEntry back;
				c.LastEntry.TryGetValue(e.Dest, out back);
				if(back == null || back.Dest != e.Origin)
				{
					string why = "Line " + e.Dest.Index + (back == null ? " has no portal back to line " + e.Origin.Index : " has its portal pointing elsewhere (line " + (back.Dest != null ? back.Dest.Index.ToString() : "none") + ")");
					if(e.Type == LINE_LINKED)
					{
						e.Type = LINE_TELEPORT;
						AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.NoReturn,
							why + ", so the engine makes linked line " + e.Origin.Index + " a teleport portal", Line(e.Origin), Line(e.Dest)));
					}
					else if(e.Type == LINE_INTERACTIVE)
					{
						AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.NoReturn,
							why + ", so interactive line " + e.Origin.Index + " acts as a one-way teleporter", Line(e.Origin), Line(e.Dest)));
					}
				}
				else if(e.Type == LINE_LINKED && back.Type != LINE_LINKED)
				{
					e.Type = LINE_INTERACTIVE;
					AddIssue(c, e.Link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.ReturnNotLinked,
						"The portal back from line " + e.Dest.Index + " is " + LineTypeName(back.Type) + ", so the engine makes linked line " + e.Origin.Index + " interactive",
						Line(e.Origin), Line(e.Dest)));
				}
			}
		}

		// Non-linked portals neither scale nor need equal ends, but unequal ends show a view that does not fit the exit
		private static void CheckLengths(Context c)
		{
			foreach(PortalLink link in c.Report.Links)
			{
				if((link.Kind != PortalKind.LinePortal && link.Kind != PortalKind.QuickPortal) || link.Type == LINE_LINKED || link.Destination == null) continue;
				PortalLine a = c.Data.Lines[link.Origin.Element.Index], b = c.Data.Lines[link.Destination.Element.Index];
				double la = Vector2D.Distance(a.V1, a.V2), lb = Vector2D.Distance(b.V1, b.V2);
				if(Math.Abs(la - lb) <= EPSILON) continue;
				AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.LengthMismatch,
					"Line " + a.Index + " is " + Num(la) + " long and line " + b.Index + " is " + Num(lb) + ", so the view through the portal does not fit its exit",
					Line(a), Line(b)));
			}
		}

		#endregion

		#region ================== Sector portals

		private static void AddSectorPortals(Context c)
		{
			int action = c.Caps.SectorSetPortal;
			bool modern = c.Caps.ModernSectorPortals;
			List<CopySource> copysources = new List<CopySource>();

			// Types the engine does not build get a row of their own and no other check
			HashSet<PortalLine> ignored = new HashSet<PortalLine>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action) continue;
				int type = l.Args[1];
				if(type < SECTOR_ANCHORED || type > SECTOR_LINKED)
					AddIgnored(c, l, type, PortalIssueCode.UnknownType, "Sector_SetPortal type " + type + " is not 0 to 6, so the engine ignores this line");
				else if(type >= SECTOR_PLANE && !modern)
					AddIgnored(c, l, type, PortalIssueCode.TypeUnsupported, "This engine supports Sector_SetPortal types 0 to 2 only, so it ignores this type " + type + " line");
				else
					continue;
				ignored.Add(l);
			}

			// Anchors (arg3 = 0) and references (arg3 = 1) by tag, type and plane, in index order
			Dictionary<string, List<PortalLine>> anchors = new Dictionary<string, List<PortalLine>>();
			Dictionary<string, List<PortalLine>> references = new Dictionary<string, List<PortalLine>>();
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || !IsAnchoredType(l.Args[1]) || ignored.Contains(l) || (l.Args[3] != 0 && l.Args[3] != 1)) continue;
				Dictionary<string, List<PortalLine>> bykey = l.Args[3] == 0 ? anchors : references;
				List<PortalLine> list;
				if(!bykey.TryGetValue(AnchorKey(l), out list)) bykey.Add(AnchorKey(l), list = new List<PortalLine>());
				list.Add(l);
			}

			// Anchored portals: only the arg3 = 0 line builds; it takes the first matching arg3 = 1 line in index order
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || !IsAnchoredType(l.Args[1]) || l.Args[3] != 0 || ignored.Contains(l)) continue;
				List<PortalLine> refs;
				references.TryGetValue(AnchorKey(l), out refs);
				PortalLine reference = refs != null ? refs[0] : null;
				int type = l.Args[1], plane = l.Args[2];

				PortalLink link = new PortalLink { Kind = PortalKind.AnchoredSector, Type = type, Plane = plane, Id = l.Args[0] };
				link.Origin = new PortalEnd(Line(l), "Anchor (arg3 = 0)");
				AddTaggedSectors(c, link, l.Args[0]);
				CheckSectorTag(c, link, l);

				// UZDoom's SpawnPortal builds nothing on a plane outside 0 to 2 or a linked portal on plane 2.
				// Zandronum checks no plane: above 0 sets the ceiling, so 3 and up mean the ceiling and a negative plane sets nothing.
				bool built = modern ? plane >= 0 && plane <= 2 && !(type == SECTOR_LINKED && plane == 2) : plane >= 0;
				if(plane < 0 || plane > 2)
				{
					AddIssue(c, link, new PortalIssue(modern || plane < 0 ? PortalSeverity.Error : PortalSeverity.Warning, PortalIssueCode.BadPlane,
						"Plane " + plane + " is not 0 (floor), 1 (ceiling) or 2 (both), so " + (built ? "this engine treats it as the ceiling and UZDoom builds nothing" : "the engine builds nothing"),
						Line(l)));
				}
				else if(!built)
				{
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.BadPlane,
						"A linked portal cannot use plane 2 (both), so the engine builds nothing", Line(l)));
				}

				if(reference == null)
				{
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.LoneAnchor,
						"No arg3 = 1 line with the same tag, type and plane, so the portal is never built", Line(l)));
				}
				else
				{
					link.Destination = new PortalEnd(Line(reference), "Reference (arg3 = 1)");
					CheckDuplicateAnchors(c, link, l, anchors[AnchorKey(l)], refs);
					if(type == SECTOR_ANCHORED && l.Args[4] != reference.Args[4])
					{
						AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.OpacityMismatch,
							"Opacity (arg4) is " + l.Args[4] + " on the arg3 = 0 line, which UZDoom uses, and " + reference.Args[4] + " on the arg3 = 1 line, which Zandronum uses",
							Line(l), Line(reference)));
					}
					if(built)
					{
						SetDisplacement(link, reference.Middle - l.Middle);
						copysources.Add(new CopySource { Link = link, Tag = l.Args[0], Plane = plane, ToLines = false });
						if(type == SECTOR_LINKED) CheckSloped(c, link, plane);
					}
				}
				c.Report.Links.Add(link);
			}

			// Reference lines no anchor of their tag, type and plane exists for
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || !IsAnchoredType(l.Args[1]) || l.Args[3] != 1 || ignored.Contains(l) || anchors.ContainsKey(AnchorKey(l))) continue;
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
						CheckSectorTag(c, link, l);
						if(c.Caps.ModernSectorPortals) copysources.Add(new CopySource { Link = link, Tag = l.Args[0], Plane = l.Args[2], ToLines = true });
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
				if(l.Action != action || (l.Args[1] != SECTOR_PLANE && l.Args[1] != SECTOR_HORIZON) || ignored.Contains(l)) continue;
				PortalLink link = new PortalLink { Kind = PortalKind.PlaneHorizon, Type = l.Args[1], Plane = l.Args[2], Id = l.Args[0] };
				link.Origin = new PortalEnd(Line(l), l.Args[1] == SECTOR_PLANE ? "Plane portal line" : "Horizon portal line");
				AddTaggedSectors(c, link, l.Args[0]);
				CheckSectorTag(c, link, l);
				copysources.Add(new CopySource { Link = link, Tag = l.Args[0], Plane = l.Args[2], ToLines = true });
				c.Report.Links.Add(link);
			}

			// Copies (CopyPortal): type 1 matches the plane or plane 3; type 5 copies only portals built with tolines.
			// Older engines (Zandronum) copy only type 0 portals, by type 1 with the exact plane.
			foreach(PortalLine l in c.Data.Lines)
			{
				if(l.Action != action || (l.Args[1] != SECTOR_COPY && l.Args[1] != SECTOR_COPY_TO_LINE) || ignored.Contains(l)) continue;
				bool copied = false;
				bool anyplane = l.Args[2] == 3 && modern;
				foreach(CopySource s in copysources)
				{
					if(s.Tag != l.Args[3]) continue;
					if(l.Args[1] == SECTOR_COPY ? (l.Args[2] != s.Plane && !anyplane) : !s.ToLines) continue;
					s.Link.Dependents.Add(new PortalEnd(Line(l), CopyRole(l)));
					copied = true;
				}
				if(copied) continue;

				PortalLink link = new PortalLink { Kind = PortalKind.Copy, Type = l.Args[1], Plane = l.Args[2], Id = l.Args[3] };
				link.Origin = new PortalEnd(Line(l), CopyRole(l));
				if(l.Args[1] == SECTOR_COPY_TO_LINE && copysources.Exists(s => s.Tag == l.Args[3]))
				{
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.CopyToLineAnchored,
						"Copy to line (type 5) copies only skybox, plane and horizon portals, and tag " + l.Args[3] + " holds only an anchored portal, so this line does nothing",
						Line(l)));
				}
				else
				{
					string msg = l.Args[1] == SECTOR_COPY_TO_LINE
						? "No skybox, plane or horizon portal has tag " + l.Args[3] + ", so this line copies nothing"
						: "No portal on tag " + l.Args[3] + (anyplane ? "" : " with plane " + l.Args[2]) + " for this line to copy"
							+ (modern ? "" : " (this engine copies only anchored type 0 portals)") + ", so it does nothing";
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.CopyNoSource, msg, Line(l)));
				}
				c.Report.Links.Add(link);
			}
		}

		// Tag 0 means every untagged sector to the engine's tag iterator
		private static void CheckSectorTag(Context c, PortalLink link, PortalLine l)
		{
			int tag = l.Args[0];
			if(tag == 0)
				AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.ZeroSectorTag,
					"Sector tag 0 applies this portal to every untagged sector (" + link.Sectors.Count + ")", Line(l)));
			else if(link.Sectors.Count == 0)
				AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.NoTaggedSector,
					"No sector has tag " + tag + ", so this portal appears nowhere", Line(l)));
		}

		// Several anchors or references of one tag, type and plane: the engine takes the first reference, and the sectors
		// keep the first portal applied on UZDoom (SetPortal skips a plane that has one) but the last on Zandronum
		private static void CheckDuplicateAnchors(Context c, PortalLink link, PortalLine anchor, List<PortalLine> anchors, List<PortalLine> refs)
		{
			if(refs.Count > 1)
			{
				List<PortalElement> elements = new List<PortalElement>();
				foreach(PortalLine r in refs) elements.Add(Line(r));
				AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.DuplicateAnchor,
					refs.Count + " arg3 = 1 lines share this tag, type and plane; the engine uses line " + refs[0].Index + ", the lowest index", elements.ToArray()));
			}
			if(anchors.Count > 1)
			{
				PortalLine kept = c.Caps.ModernSectorPortals ? anchors[0] : anchors[anchors.Count - 1];
				if(kept != anchor)
					AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.DuplicateAnchor,
						"Line " + kept.Index + " applies a portal with the same tag, type and plane, and the sectors keep the "
						+ (c.Caps.ModernSectorPortals ? "first" : "last") + " one applied, so this one shows nowhere", Line(anchor), Line(kept)));
			}
		}

		private static void CheckSloped(Context c, PortalLink link, int plane)
		{
			List<PortalElement> sloped = new List<PortalElement>();
			foreach(int s in link.Sectors)
				if(plane == 0 ? c.Data.Sectors[s].FloorSloped : c.Data.Sectors[s].CeilingSloped) sloped.Add(new PortalElement(PortalElementType.Sector, s));
			if(sloped.Count == 0) return;

			List<string> names = sloped.ConvertAll(e => e.Index.ToString());
			AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.LinkedSloped,
				"A linked portal cannot sit on a sloped " + (plane == 0 ? "floor" : "ceiling") + ", so the engine disables it in sector" + (sloped.Count > 1 ? "s " : " ") + string.Join(", ", names.ToArray()),
				sloped.ToArray()));
		}

		private static void AddIgnored(Context c, PortalLine l, int type, PortalIssueCode code, string message)
		{
			PortalLink link = new PortalLink { Kind = PortalKind.Ignored, Type = type };
			link.Origin = new PortalEnd(Line(l), l.Action == c.Caps.LineSetPortal ? "Line_SetPortal line" : "Sector_SetPortal line");
			AddIssue(c, link, new PortalIssue(PortalSeverity.Error, code, message, Line(l)));
			c.Report.Links.Add(link);
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

		// Every stack point builds its own portal to the other class's thing with its TID, and the TID hash
		// yields the highest index first. A mutual pair is one two-way row; any other point is a one-way row.
		private static void AddStackedSectors(Context c)
		{
			Dictionary<int, PortalThing> highestupper = new Dictionary<int, PortalThing>();
			Dictionary<int, PortalThing> highestlower = new Dictionary<int, PortalThing>();
			List<PortalThing> points = new List<PortalThing>();
			foreach(PortalThing t in c.Data.Things)
			{
				bool upper = t.Type == c.Caps.UpperStack;
				if(!upper && t.Type != c.Caps.LowerStack) continue;
				points.Add(t);
				if(t.Tid != 0) (upper ? highestupper : highestlower)[t.Tid] = t;
			}

			foreach(PortalThing t in points)
			{
				bool upper = t.Type == c.Caps.UpperStack;
				PortalLink link = new PortalLink { Kind = PortalKind.StackedSectors, Id = t.Tid };
				PortalThing partner = null;
				if(t.Tid != 0) (upper ? highestlower : highestupper).TryGetValue(t.Tid, out partner);
				if(partner == null)
				{
					SetStackEnd(c, link, t, true);
					string msg = t.Tid == 0 ? "Stacked sector things with TID 0 never pair"
						: upper ? "No Lower Sector thing (LowerStackLookOnly) has TID " + t.Tid + ", so this floor portal is never built"
						: "No Upper Sector thing (UpperStackLookOnly) has TID " + t.Tid + ", so this ceiling portal is never built";
					AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.StackUnpaired, msg, Thing(t)));
					c.Report.Links.Add(link);
					continue;
				}

				// The partner's pick is the highest-index point of t's class on the TID
				PortalThing partnerpick = (upper ? highestupper : highestlower)[t.Tid];
				link.TwoWay = partnerpick == t;
				if(link.TwoWay && partner.Index < t.Index) continue;  // already added from the partner's side

				// A two-way row always runs upper to lower
				PortalThing from = link.TwoWay && !upper ? partner : t;
				PortalThing to = from == t ? partner : t;
				SetStackEnd(c, link, from, true);
				SetStackEnd(c, link, to, false);

				// SetupPortals takes the offset from to->target, which the last point to pick it (the highest index) overwrites
				PortalThing totarget = (to.Type == c.Caps.UpperStack ? highestlower : highestupper)[t.Tid];
				SetDisplacement(link, to.Position - totarget.Position);

				if(!link.TwoWay)
				{
					string cls = upper ? "Upper Sector" : "Lower Sector";
					AddIssue(c, link, new PortalIssue(PortalSeverity.Warning, PortalIssueCode.StackDuplicate,
						"Several " + cls + " things have TID " + t.Tid + "; thing " + partner.Index + " pairs only with thing " + partnerpick.Index
						+ ", the highest index, and this portal's offset is measured from that thing", Thing(t), Thing(partnerpick)));
				}
				c.Report.Links.Add(link);
			}
		}

		private static void SetStackEnd(Context c, PortalLink link, PortalThing t, bool origin)
		{
			PortalEnd end = new PortalEnd(Thing(t), t.Type == c.Caps.UpperStack ? "Upper Sector thing" : "Lower Sector thing");
			if(origin) link.Origin = end; else link.Destination = end;
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
				else AddIssue(c, link, new PortalIssue(PortalSeverity.Error, PortalIssueCode.PickerNoViewpoint,
					"No skybox viewpoint thing has TID " + p.Args[0] + ", so the engine logs \"Can't find SkyViewpoint\" and the picker does nothing", Thing(p)));
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
