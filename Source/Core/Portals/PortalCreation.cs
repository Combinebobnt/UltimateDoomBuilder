using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

namespace CodeImp.DoomBuilder.Portals
{
	public enum PortalCreateKind { QuickPortal, LinePortal, AnchoredSector, StackedSectors }

	// What the user picked in the create form
	public sealed class PortalCreateRequest
	{
		public PortalCreateKind Kind;
		public int A = -1;         // first line, the anchor (arg3 = 0) line, or the Upper Sector thing's sector
		public int B = -1;         // second line, the reference (arg3 = 1) line, or the Lower Sector thing's sector
		public int Type;           // Line_SetPortal type; Sector_SetPortal type 0 or 6; quick portal arg0 (0 linked, 1 visual)
		public int Plane;          // Line_SetPortal plane align, or the Sector_SetPortal plane
		public int Opacity;        // Sector_SetPortal arg4 on both lines, or each stack thing's arg0
		public Vector2D PositionA; // stacked things only
		public Vector2D PositionB;
	}

	// Format facts and the numbers already in use outside the snapshot (tag-typed args)
	public sealed class PortalCreateContext
	{
		public bool MultipleTags;  // UDMF: lines and sectors carry tag lists
		public bool ActionArgs = true;
		public int MaxArgument = int.MaxValue;
		public int MaxTag = int.MaxValue;
		public readonly HashSet<int> UsedLineIds = new HashSet<int>();
		public readonly HashSet<int> UsedSectorTags = new HashSet<int>();
		public readonly HashSet<int> UsedThingIds = new HashSet<int>();
		public Func<int, string> ActionName;
	}

	public sealed class PortalLineEdit
	{
		public int Index;
		public int Action;
		public int[] Args;
		public int[] Ids;          // the line's new tag list; null leaves its tags alone (Hexen ids live in args)
	}

	public sealed class PortalSectorEdit
	{
		public int Index;
		public int[] Tags;
	}

	public sealed class PortalThingAdd
	{
		public int Type;
		public int Tid;
		public int[] Args;
		public Vector2D Position;
		public int Sector;
	}

	public sealed class PortalCreatePlan
	{
		public string Refusal;     // set when nothing can be created
		public readonly List<string> Notes = new List<string>();
		public readonly List<string> Confirms = new List<string>();
		public readonly List<PortalLineEdit> LineEdits = new List<PortalLineEdit>();
		public readonly List<PortalSectorEdit> SectorEdits = new List<PortalSectorEdit>();
		public readonly List<PortalThingAdd> ThingAdds = new List<PortalThingAdd>();
		public readonly List<PortalIssue> NewErrors = new List<PortalIssue>();
		public readonly List<PortalIssue> NewWarnings = new List<PortalIssue>();
		public PortalLink Link;    // the new link as the analyzer sees it once applied

		public bool CanApply { get { return Refusal == null && NewErrors.Count == 0; } }
	}

	// Plans a two-ended portal over the snapshot, then checks it by analysing a copy with the plan applied
	public static class PortalCreation
	{
		public const int SECTOR_VISUAL = 0;
		public const int SECTOR_LINKED = 6;

		public static List<PortalCreateKind> Kinds(PortalCapabilities caps, PortalCreateContext ctx, bool lines)
		{
			List<PortalCreateKind> kinds = new List<PortalCreateKind>();
			if(!caps.Any) return kinds;
			if(!lines)
			{
				if(caps.StackedThings) kinds.Add(PortalCreateKind.StackedSectors);
				return kinds;
			}
			if(!ctx.ActionArgs) return kinds;
			if(caps.LineSetPortal != 0) kinds.Add(PortalCreateKind.LinePortal);
			if(caps.LineQuickPortal != 0 && ctx.MultipleTags) kinds.Add(PortalCreateKind.QuickPortal);
			if(caps.SectorSetPortal != 0) kinds.Add(PortalCreateKind.AnchoredSector);
			return kinds;
		}

		public static PortalCreatePlan Plan(PortalMapData data, PortalCapabilities caps, PortalCreateContext ctx, PortalCreateRequest req)
		{
			PortalCreatePlan plan = new PortalCreatePlan();
			bool lines = req.Kind != PortalCreateKind.StackedSectors;
			if(!Kinds(caps, ctx, lines).Contains(req.Kind)) return Refuse(plan, "This configuration or map format cannot create this kind of portal.");
			int count = lines ? data.Lines.Count : data.Sectors.Count;
			if(req.A < 0 || req.B < 0 || req.A >= count || req.B >= count) return Refuse(plan, "Select two " + (lines ? "lines." : "sectors."));
			if(req.A == req.B) return Refuse(plan, "The two ends must be different " + (lines ? "lines." : "sectors."));

			switch(req.Kind)
			{
				case PortalCreateKind.QuickPortal: PlanQuick(data, caps, ctx, req, plan); break;
				case PortalCreateKind.LinePortal: PlanLinePortal(data, caps, ctx, req, plan); break;
				case PortalCreateKind.AnchoredSector: PlanAnchored(data, caps, ctx, req, plan); break;
				default: PlanStacked(data, caps, ctx, req, plan); break;
			}
			if(plan.Refusal != null) return plan;

			foreach(PortalLineEdit e in plan.LineEdits)
			{
				int old = data.Lines[e.Index].Action;
				if(old == 0) continue;
				string name = ctx.ActionName != null ? ctx.ActionName(old) : null;
				plan.Confirms.Add("Line " + e.Index + " already has action " + old + (string.IsNullOrEmpty(name) ? "" : " (" + name + ")")
					+ ", which is replaced" + (caps.Hexen ? ", along with any line id it gave the line." : "."));
			}

			Check(data, caps, req, plan);
			return plan;
		}

		// A copy of the snapshot with the plan written into it
		public static PortalMapData Apply(PortalMapData data, PortalCapabilities caps, PortalCreatePlan plan)
		{
			PortalMapData copy = data.Clone();
			foreach(PortalSectorEdit e in plan.SectorEdits) copy.Sectors[e.Index].Tags = (int[])e.Tags.Clone();
			foreach(PortalLineEdit e in plan.LineEdits)
			{
				PortalLine l = copy.Lines[e.Index];
				l.Action = e.Action;
				l.Args = (int[])e.Args.Clone();
				if(e.Ids != null) l.Ids = (int[])e.Ids.Clone();
			}
			foreach(PortalThingAdd a in plan.ThingAdds) copy.AddThing(a.Type, a.Tid, a.Args, a.Position, a.Sector);
			if(caps.Hexen) copy.AssignHexenIds(caps);
			return copy;
		}

		#region ================== Kinds

		private static void PlanQuick(PortalMapData data, PortalCapabilities caps, PortalCreateContext ctx, PortalCreateRequest req, PortalCreatePlan plan)
		{
			// The engine pairs 301 lines by their first id, so a new tag goes first on both
			int tag = NewNumber(LineIdsInUse(data, ctx), ctx.MaxTag, null);
			if(tag == 0) { Refuse(plan, "No free line tag is left."); return; }
			int arg0 = req.Type == 0 ? 0 : 1;
			foreach(int index in new[] { req.A, req.B })
			{
				List<int> ids = new List<int> { tag };
				foreach(int id in data.Lines[index].Ids) if(id != tag) ids.Add(id);
				plan.LineEdits.Add(new PortalLineEdit { Index = index, Action = caps.LineQuickPortal, Args = new[] { arg0, 0, 0, 0, 0 }, Ids = ids.ToArray() });
			}
			plan.Notes.Add("Lines " + req.A + " and " + req.B + " get the new first tag " + tag + ".");
		}

		private static void PlanLinePortal(PortalMapData data, PortalCapabilities caps, PortalCreateContext ctx, PortalCreateRequest req, PortalCreatePlan plan)
		{
			if(req.Type < 0 || req.Type > 3) { Refuse(plan, "Line portal type must be 0 to 3."); return; }
			if(req.Plane < 0 || req.Plane > 2) { Refuse(plan, "Plane align must be 0 to 2."); return; }

			HashSet<int> inuse = LineIdsInUse(data, ctx);
			HashSet<int> taken = new HashSet<int>();
			int[] ends = { req.A, req.B };
			int[] ids = new int[2];
			int[][] tags = new int[2][];
			for(int i = 0; i < 2; i++)
			{
				PortalLine l = data.Lines[ends[i]];
				if(!caps.Hexen)
				{
					// Reuse a tag only this line carries, since the engine takes the first line with the exit id
					foreach(int id in l.Ids)
						if(id > 0 && id <= ctx.MaxArgument && !OtherLineCarries(data, id, l.Index)) { ids[i] = id; break; }
					if(ids[i] != 0)
					{
						plan.Notes.Add("Line " + l.Index + " keeps tag " + ids[i] + " as its id.");
						continue;
					}
				}

				ids[i] = NewNumber(inuse, Math.Min(ctx.MaxArgument, ctx.MaxTag), taken);
				if(ids[i] == 0) { Refuse(plan, "No free line id up to " + Math.Min(ctx.MaxArgument, ctx.MaxTag) + " is left."); return; }
				taken.Add(ids[i]);
				if(caps.Hexen)
				{
					plan.Notes.Add("Line " + l.Index + " gets the line id " + ids[i] + " (arg1).");
				}
				else
				{
					List<int> list = new List<int>(l.Ids);
					list.Add(ids[i]);
					tags[i] = list.ToArray();
					plan.Notes.Add("Line " + l.Index + " gets the new tag " + ids[i] + ".");
				}
			}

			for(int i = 0; i < 2; i++)
			{
				int own = ids[i], exit = ids[1 - i];
				plan.LineEdits.Add(new PortalLineEdit
				{
					Index = ends[i],
					Action = caps.LineSetPortal,
					Args = new[] { exit, caps.Hexen ? own : 0, req.Type, req.Plane, 0 },
					Ids = tags[i],
				});
			}
		}

		private static void PlanAnchored(PortalMapData data, PortalCapabilities caps, PortalCreateContext ctx, PortalCreateRequest req, PortalCreatePlan plan)
		{
			if(req.Type != SECTOR_VISUAL && req.Type != SECTOR_LINKED) { Refuse(plan, "Anchored sector portal type must be 0 or 6."); return; }
			if(req.Type == SECTOR_LINKED && !caps.ModernSectorPortals) { Refuse(plan, "This configuration's engine has no linked sector portals."); return; }
			if(req.Plane < 0 || req.Plane > 2 || (req.Type == SECTOR_LINKED && req.Plane == 2)) { Refuse(plan, req.Type == SECTOR_LINKED ? "A linked sector portal takes the floor or the ceiling, not both." : "Plane must be floor, ceiling or both."); return; }
			if(req.Opacity < 0 || req.Opacity > 255) { Refuse(plan, "Opacity must be 0 to 255."); return; }

			PortalLine anchor = data.Lines[req.A];
			if(anchor.Front < 0 || anchor.Front >= data.Sectors.Count) { Refuse(plan, "Line " + anchor.Index + " has no front sector to show the portal in."); return; }
			PortalSector sector = data.Sectors[anchor.Front];
			int max = Math.Min(ctx.MaxArgument, ctx.MaxTag);

			// The front sector's own tag when no other sector carries it and no other sector portal uses it,
			// since the engine pairs an anchor with the first reference of its tag; a sector tag is never overwritten
			int tag = 0;
			foreach(int t in sector.Tags)
				if(t > 0 && t <= max && !OtherSectorCarries(data, t, sector.Index) && !SectorPortalUses(data, caps, t, req)) { tag = t; break; }

			if(tag != 0)
			{
				plan.Notes.Add("Sector " + sector.Index + "'s tag " + tag + " is the portal tag.");
			}
			else if(!ctx.MultipleTags && sector.Tags.Length > 0 && SectorPortalUses(data, caps, sector.Tags[0], req))
			{
				Refuse(plan, "Sector " + sector.Index + "'s tag " + sector.Tags[0] + " already belongs to another sector portal, and this map format allows one tag per sector. Give the sector a tag of its own first.");
				return;
			}
			else if(sector.Tags.Length == 0 || ctx.MultipleTags)
			{
				tag = NewNumber(SectorTagsInUse(data, ctx), max, null);
				if(tag == 0) { Refuse(plan, "No free sector tag up to " + max + " is left."); return; }
				List<int> tags = new List<int>(sector.Tags);
				tags.Add(tag);
				plan.SectorEdits.Add(new PortalSectorEdit { Index = sector.Index, Tags = tags.ToArray() });
				plan.Notes.Add(sector.Tags.Length == 0 ? "Sector " + sector.Index + " gets the new tag " + tag + "."
					: "Sector " + sector.Index + " gets the extra tag " + tag + ", since its tags are shared with other sectors.");
			}
			else
			{
				tag = sector.Tags[0];
				if(tag > max) { Refuse(plan, "Sector " + sector.Index + "'s tag " + tag + " is above " + max + ", which Sector_SetPortal's tag argument cannot hold."); return; }
				List<string> others = new List<string>();
				foreach(PortalSector s in data.Sectors)
					if(s.Index != sector.Index && Array.IndexOf(s.Tags, tag) >= 0) others.Add(s.Index.ToString());
				plan.Confirms.Add("Sector " + sector.Index + "'s tag " + tag + " is shared with " + (others.Count == 1 ? "sector " : "sectors ")
					+ Shorten(others) + ", so the portal shows in all of them.");
				plan.Notes.Add("Sector " + sector.Index + "'s shared tag " + tag + " is the portal tag.");
			}

			plan.LineEdits.Add(new PortalLineEdit { Index = req.A, Action = caps.SectorSetPortal, Args = new[] { tag, req.Type, req.Plane, 0, req.Opacity } });
			plan.LineEdits.Add(new PortalLineEdit { Index = req.B, Action = caps.SectorSetPortal, Args = new[] { tag, req.Type, req.Plane, 1, req.Opacity } });
		}

		private static void PlanStacked(PortalMapData data, PortalCapabilities caps, PortalCreateContext ctx, PortalCreateRequest req, PortalCreatePlan plan)
		{
			if(req.Opacity < 0 || req.Opacity > 255) { Refuse(plan, "Opacity must be 0 to 255."); return; }

			// A skybox picker's arg0 names a viewpoint TID, which a stack thing would also answer to
			HashSet<int> inuse = new HashSet<int>(ctx.UsedThingIds);
			foreach(PortalThing t in data.Things)
			{
				if(t.Tid != 0) inuse.Add(t.Tid);
				if(t.Type == caps.SkyPicker && caps.SkyPicker != 0 && t.Args[0] != 0) inuse.Add(t.Args[0]);
			}
			int tid = NewNumber(inuse, ctx.MaxTag, null);
			if(tid == 0) { Refuse(plan, "No free thing TID is left."); return; }

			plan.ThingAdds.Add(new PortalThingAdd { Type = caps.UpperStack, Tid = tid, Args = new[] { req.Opacity, 0, 0, 0, 0 }, Position = req.PositionA, Sector = req.A });
			plan.ThingAdds.Add(new PortalThingAdd { Type = caps.LowerStack, Tid = tid, Args = new[] { req.Opacity, 0, 0, 0, 0 }, Position = req.PositionB, Sector = req.B });
			plan.Notes.Add("An Upper Sector thing in sector " + req.A + " and a Lower Sector thing in sector " + req.B + " share the new TID " + tid + ".");
		}

		#endregion

		#region ================== Checks

		// Analyses before and after; issues the creation adds block (errors) or are shown (warnings)
		private static void Check(PortalMapData data, PortalCapabilities caps, PortalCreateRequest req, PortalCreatePlan plan)
		{
			PortalReport before = PortalAnalyzer.Analyze(data, caps);
			PortalMapData after = Apply(data, caps, plan);
			PortalReport report = PortalAnalyzer.Analyze(after, caps);

			// An issue naming an element the plan writes is the new portal's, even when the old state had the same one
			HashSet<PortalElement> touched = new HashSet<PortalElement>();
			foreach(PortalLineEdit e in plan.LineEdits) touched.Add(new PortalElement(PortalElementType.Linedef, e.Index));
			for(int t = 0; t < plan.ThingAdds.Count; t++) touched.Add(new PortalElement(PortalElementType.Thing, data.Things.Count + t));
			HashSet<string> old = new HashSet<string>();
			foreach(PortalIssue i in before.Issues) old.Add(IssueKey(i));
			foreach(PortalIssue i in report.Issues)
			{
				if(old.Contains(IssueKey(i)) && !i.Elements.Exists(touched.Contains)) continue;
				(i.Severity == PortalSeverity.Error ? plan.NewErrors : plan.NewWarnings).Add(i);
			}

			PortalElement a, b;
			if(req.Kind == PortalCreateKind.StackedSectors)
			{
				a = new PortalElement(PortalElementType.Thing, data.Things.Count);
				b = new PortalElement(PortalElementType.Thing, data.Things.Count + 1);
			}
			else
			{
				a = new PortalElement(PortalElementType.Linedef, req.A);
				b = new PortalElement(PortalElementType.Linedef, req.B);
			}
			foreach(PortalLink link in report.Links)
			{
				if(link.Origin == null || link.Destination == null || !link.Origin.Element.Equals(a) || !link.Destination.Element.Equals(b)) continue;
				plan.Link = link;
				break;
			}
			if(plan.Link == null)
			{
				foreach(PortalLink link in report.Links)
				{
					bool hasa = false, hasb = false;
					foreach(PortalEnd e in link.Ends) { hasa |= e.Element.Equals(a); hasb |= e.Element.Equals(b); }
					if(hasa && hasb) { plan.Link = link; break; }
				}
			}
		}

		internal static string IssueKey(PortalIssue issue)
		{
			List<string> parts = new List<string>();
			foreach(PortalElement e in issue.Elements) parts.Add(e.ToString());
			parts.Sort(StringComparer.Ordinal);
			return issue.Code + ":" + string.Join(",", parts);
		}

		#endregion

		#region ================== Numbers

		// The lowest number from 1 to max that nothing uses yet; 0 when none is left
		public static int NewNumber(HashSet<int> inuse, int max, HashSet<int> taken)
		{
			for(int n = 1; n <= max && n > 0; n++)
				if(!inuse.Contains(n) && (taken == null || !taken.Contains(n))) return n;
			return 0;
		}

		private static HashSet<int> LineIdsInUse(PortalMapData data, PortalCreateContext ctx)
		{
			HashSet<int> inuse = new HashSet<int>(ctx.UsedLineIds);
			foreach(PortalLine l in data.Lines) foreach(int id in l.Ids) if(id != 0) inuse.Add(id);
			return inuse;
		}

		private static HashSet<int> SectorTagsInUse(PortalMapData data, PortalCreateContext ctx)
		{
			HashSet<int> inuse = new HashSet<int>(ctx.UsedSectorTags);
			foreach(PortalSector s in data.Sectors) foreach(int t in s.Tags) if(t != 0) inuse.Add(t);
			return inuse;
		}

		private static bool OtherLineCarries(PortalMapData data, int id, int line)
		{
			foreach(PortalLine l in data.Lines)
				if(l.Index != line && Array.IndexOf(l.Ids, id) >= 0) return true;
			return false;
		}

		// Any Sector_SetPortal line besides the two being written that takes this tag as its sector tag or copy source
		private static bool SectorPortalUses(PortalMapData data, PortalCapabilities caps, int tag, PortalCreateRequest req)
		{
			foreach(PortalLine l in data.Lines)
			{
				if(l.Action != caps.SectorSetPortal || l.Index == req.A || l.Index == req.B) continue;
				bool copy = l.Args[1] == 1 || l.Args[1] == 5;
				if(l.Args[0] == tag || (copy && l.Args[3] == tag)) return true;
			}
			return false;
		}

		private static bool OtherSectorCarries(PortalMapData data, int tag, int sector)
		{
			foreach(PortalSector s in data.Sectors)
				if(s.Index != sector && Array.IndexOf(s.Tags, tag) >= 0) return true;
			return false;
		}

		private static string Shorten(List<string> items)
		{
			const int max = 8;
			if(items.Count <= max) return string.Join(", ", items);
			return string.Join(", ", items.GetRange(0, max)) + " and " + (items.Count - max) + " more";
		}

		private static PortalCreatePlan Refuse(PortalCreatePlan plan, string reason)
		{
			plan.Refusal = reason;
			return plan;
		}

		#endregion
	}
}
