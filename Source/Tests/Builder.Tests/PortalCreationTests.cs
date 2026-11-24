using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Portals;

namespace Builder.Tests
{
	public class PortalCreationTests
	{
		private const int SETPORTAL = 156, QUICKPORTAL = 301, SECTORPORTAL = 57, LINEID = 121;
		private const int UPPER = 9077, LOWER = 9078;

		private static readonly Dictionary<string, int> Actions = new Dictionary<string, int>
		{
			{ "Line_SetPortal", SETPORTAL }, { "Line_QuickPortal", QUICKPORTAL }, { "Sector_SetPortal", SECTORPORTAL },
			{ "Line_SetIdentification", LINEID },
		};

		private static readonly Dictionary<string, int> ThingClasses = new Dictionary<string, int>
		{
			{ "UpperStackLookOnly", UPPER }, { "LowerStackLookOnly", LOWER },
		};

		private static PortalCapabilities Caps(bool hexen = false, bool tids = true, params string[] missing)
		{
			return PortalCapabilities.Create(
				id => Actions.TryGetValue(id, out int n) && !missing.Contains(id) ? n : 0,
				cls => ThingClasses.TryGetValue(cls, out int n) && !missing.Contains(cls) ? n : 0,
				hexen, tids);
		}

		private static PortalCreateContext Udmf() { return new PortalCreateContext { MultipleTags = true }; }
		private static PortalCreateContext Hexen() { return new PortalCreateContext { MultipleTags = false, MaxArgument = 255, MaxTag = 65535 }; }

		// Sectors 0 and 1, joined by no two-sided line; line 0 is a wall of sector 0 and line 1 an equally long, anti-parallel wall of sector 1
		private static PortalMapData TwoRooms(int[] tags0 = null, int[] tags1 = null)
		{
			PortalMapData d = new PortalMapData();
			d.AddSector(tags0 ?? new int[0]);
			d.AddSector(tags1 ?? new int[0]);
			d.AddLine(0, null, null, new Vector2D(0, 0), new Vector2D(0, 128), 0, -1);
			d.AddLine(0, null, null, new Vector2D(1024, 128), new Vector2D(1024, 0), 1, -1);
			return d;
		}

		private static PortalLine Extra(PortalMapData d, int action, int[] args, int[] ids, int front = 0, int back = -1)
		{
			return d.AddLine(action, args, ids, new Vector2D(0, 0), new Vector2D(64, 0), front, back);
		}

		private static PortalCreateRequest Request(PortalCreateKind kind, int a, int b, int type = 0, int plane = 0, int opacity = 0)
		{
			return new PortalCreateRequest { Kind = kind, A = a, B = b, Type = type, Plane = plane, Opacity = opacity };
		}

		private static PortalLineEdit Edit(PortalCreatePlan plan, int line) { return plan.LineEdits.Single(e => e.Index == line); }
		private static PortalIssueCode[] Codes(List<PortalIssue> issues) { return issues.Select(i => i.Code).ToArray(); }

		[Fact]
		public void Kinds_FollowTheConfigurationAndFormat()
		{
			Assert.Equal(new[] { PortalCreateKind.LinePortal, PortalCreateKind.QuickPortal, PortalCreateKind.AnchoredSector }, PortalCreation.Kinds(Caps(), Udmf(), true));
			Assert.Equal(new[] { PortalCreateKind.LinePortal, PortalCreateKind.AnchoredSector }, PortalCreation.Kinds(Caps(true), Hexen(), true));
			Assert.Equal(new[] { PortalCreateKind.AnchoredSector }, PortalCreation.Kinds(Caps(false, true, "Line_SetPortal", "Line_QuickPortal"), Udmf(), true));
			Assert.Empty(PortalCreation.Kinds(Caps(), new PortalCreateContext { ActionArgs = false }, true));
			Assert.Empty(PortalCreation.Kinds(Caps(false, true, "UpperStackLookOnly"), Udmf(), true));
			Assert.Equal(new[] { PortalCreateKind.StackedSectors }, PortalCreation.Kinds(Caps(), Udmf(), false));
			Assert.Empty(PortalCreation.Kinds(Caps(false, false), Udmf(), false));

			PortalCreatePlan plan = PortalCreation.Plan(TwoRooms(), Caps(true), Hexen(), Request(PortalCreateKind.QuickPortal, 0, 1));
			Assert.NotNull(plan.Refusal);
			Assert.Empty(plan.LineEdits);
		}

		[Fact]
		public void QuickPortal_PutsOneUnusedTagFirstOnBothLines_AndKeepsTheirOtherTags()
		{
			PortalMapData d = TwoRooms();
			d.Lines[0].Ids = new[] { 4 };
			Extra(d, 0, null, new[] { 1 });
			PortalCreateContext ctx = Udmf();
			ctx.UsedLineIds.Add(2);

			PortalCreatePlan plan = PortalCreation.Plan(d, Caps(), ctx, Request(PortalCreateKind.QuickPortal, 0, 1, 1));

			Assert.True(plan.CanApply, plan.Refusal);
			Assert.Equal(new[] { 3, 4 }, Edit(plan, 0).Ids);
			Assert.Equal(new[] { 3 }, Edit(plan, 1).Ids);
			Assert.All(plan.LineEdits, e => Assert.Equal(QUICKPORTAL, e.Action));
			Assert.All(plan.LineEdits, e => Assert.Equal(new[] { 1, 0, 0, 0, 0 }, e.Args));
			Assert.Equal(PortalKind.QuickPortal, plan.Link.Kind);
			Assert.NotEqual(3, plan.Link.Type);
			Assert.Empty(plan.NewWarnings);
		}

		[Fact]
		public void LinePortal_Udmf_ReusesATagOnlyThatLineCarries_ElseAddsANewOne()
		{
			PortalMapData d = TwoRooms();
			d.Lines[0].Ids = new[] { 5, 6 };
			d.Lines[1].Ids = new[] { 5 };
			Extra(d, 0, null, new[] { 5, 1 });

			PortalCreatePlan plan = PortalCreation.Plan(d, Caps(), Udmf(), Request(PortalCreateKind.LinePortal, 0, 1, 0, 2));

			Assert.True(plan.CanApply, plan.Refusal);
			Assert.Null(Edit(plan, 0).Ids);
			Assert.Equal(new[] { 5, 2 }, Edit(plan, 1).Ids);
			Assert.Equal(new[] { 2, 0, 0, 2, 0 }, Edit(plan, 0).Args);
			Assert.Equal(new[] { 6, 0, 0, 2, 0 }, Edit(plan, 1).Args);
			Assert.Equal(PortalKind.LinePortal, plan.Link.Kind);
			Assert.True(plan.Link.TwoWay);
			Assert.Empty(plan.NewWarnings);
		}

		[Fact]
		public void LinePortal_Hexen_WritesEachLinesOwnIdToArg1_AvoidingIdsAnyLineOrArgUses()
		{
			PortalCapabilities caps = Caps(true);
			PortalMapData d = TwoRooms();
			Extra(d, LINEID, new[] { 1 }, null);
			d.AssignHexenIds(caps);
			PortalCreateContext ctx = Hexen();
			ctx.UsedLineIds.Add(3);

			PortalCreatePlan plan = PortalCreation.Plan(d, caps, ctx, Request(PortalCreateKind.LinePortal, 0, 1));

			Assert.True(plan.CanApply, plan.Refusal);
			Assert.Equal(new[] { 4, 2, 0, 0, 0 }, Edit(plan, 0).Args);
			Assert.Equal(new[] { 2, 4, 0, 0, 0 }, Edit(plan, 1).Args);
			Assert.All(plan.LineEdits, e => Assert.Null(e.Ids));
			Assert.True(plan.Link.TwoWay);

			ctx.MaxArgument = 2;
			plan = PortalCreation.Plan(d, caps, ctx, Request(PortalCreateKind.LinePortal, 0, 1));
			Assert.Contains("up to 2", plan.Refusal);
			Assert.False(plan.CanApply);
		}

		[Fact]
		public void LinePortal_NewEngineErrorsBlock_PreExistingOnesDoNot()
		{
			// Linked on one-sided lines: the engine makes both visual
			PortalMapData d = TwoRooms();
			PortalCreatePlan plan = PortalCreation.Plan(d, Caps(), Udmf(), Request(PortalCreateKind.LinePortal, 0, 1, 3));
			Assert.Null(plan.Refusal);
			Assert.Contains(PortalIssueCode.OneSidedTraversable, Codes(plan.NewErrors));
			Assert.False(plan.CanApply);

			// A broken portal elsewhere is not the new pair's problem
			Extra(d, SETPORTAL, new[] { 99 }, null);
			plan = PortalCreation.Plan(d, Caps(), Udmf(), Request(PortalCreateKind.LinePortal, 0, 1));
			Assert.Empty(plan.NewErrors);
			Assert.True(plan.CanApply);
		}

		[Fact]
		public void Anchored_UsesTheFrontSectorsOwnTag_AndWritesRolesAndOpacityOnBothLines()
		{
			PortalMapData d = TwoRooms(new[] { 3 });
			PortalCreatePlan plan = PortalCreation.Plan(d, Caps(), Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1, 0, 1, 40));

			Assert.True(plan.CanApply, plan.Refusal);
			Assert.Empty(plan.SectorEdits);
			Assert.Equal(new[] { 3, 0, 1, 0, 40 }, Edit(plan, 0).Args);
			Assert.Equal(new[] { 3, 0, 1, 1, 40 }, Edit(plan, 1).Args);
			Assert.All(plan.LineEdits, e => Assert.Equal(SECTORPORTAL, e.Action));
			Assert.Equal(PortalKind.AnchoredSector, plan.Link.Kind);
			Assert.Equal(0, plan.Link.Origin.Element.Index);
			Assert.Equal(1, plan.Link.Destination.Element.Index);
			Assert.Equal(new Vector2D(1024, 0), plan.Link.Displacement);
		}

		[Fact]
		public void Anchored_SectorTagChoice_NeverOverwritesATag()
		{
			// Untagged: a new tag, skipping one a special refers to
			PortalCreateContext ctx = Udmf();
			ctx.UsedSectorTags.Add(1);
			PortalCreatePlan plan = PortalCreation.Plan(TwoRooms(), Caps(), ctx, Request(PortalCreateKind.AnchoredSector, 0, 1));
			Assert.Equal(new[] { 2 }, plan.SectorEdits.Single(e => e.Index == 0).Tags);
			Assert.Equal(2, Edit(plan, 0).Args[0]);

			// Shared in UDMF: an extra tag after the existing one
			plan = PortalCreation.Plan(TwoRooms(new[] { 3 }, new[] { 3 }), Caps(), Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1));
			Assert.Equal(new[] { 3, 1 }, plan.SectorEdits.Single().Tags);
			Assert.Empty(plan.Confirms);

			// Shared in a single-tag format: the shared tag, after a confirm
			PortalCapabilities hexen = Caps(true);
			plan = PortalCreation.Plan(TwoRooms(new[] { 3 }, new[] { 3 }), hexen, Hexen(), Request(PortalCreateKind.AnchoredSector, 0, 1));
			Assert.Empty(plan.SectorEdits);
			Assert.Equal(3, Edit(plan, 0).Args[0]);
			Assert.Contains("shared with sector 1", Assert.Single(plan.Confirms));

			// A tag Sector_SetPortal's byte argument cannot hold
			plan = PortalCreation.Plan(TwoRooms(new[] { 300 }), hexen, Hexen(), Request(PortalCreateKind.AnchoredSector, 0, 1));
			Assert.Contains("above 255", plan.Refusal);
		}

		[Fact]
		public void Anchored_RefusesWhatTheEngineWouldIgnore()
		{
			PortalCapabilities zandronum = Caps(false, true, "Line_SetPortal", "Line_QuickPortal");
			Assert.NotNull(PortalCreation.Plan(TwoRooms(), zandronum, Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1, 6)).Refusal);
			Assert.NotNull(PortalCreation.Plan(TwoRooms(), Caps(), Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1, 6, 2)).Refusal);
			Assert.Null(PortalCreation.Plan(TwoRooms(), Caps(), Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1, 0, 2)).Refusal);

			PortalMapData d = TwoRooms();
			d.Lines[0].Front = -1;
			Assert.Contains("no front sector", PortalCreation.Plan(d, Caps(), Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1)).Refusal);
		}

		[Fact]
		public void Anchored_LinkedInOnePortalGroup_IsBlocked()
		{
			PortalMapData d = TwoRooms();
			Extra(d, 0, null, null, 0, 1);
			PortalCreatePlan plan = PortalCreation.Plan(d, Caps(), Udmf(), Request(PortalCreateKind.AnchoredSector, 0, 1, 6));
			Assert.Contains(PortalIssueCode.SameGroup, Codes(plan.NewErrors));
			Assert.False(plan.CanApply);
		}

		[Fact]
		public void ReplacingAnExitsSpecial_AsksAndIsBlockedWhenItBreaksAnotherPortal()
		{
			// Hexen: line 1's id 9 comes from its 121, which an existing line portal exits through
			PortalCapabilities caps = Caps(true);
			PortalMapData d = TwoRooms();
			d.Lines[1].Action = LINEID;
			d.Lines[1].Args = new[] { 9, 0, 0, 0, 0 };
			Extra(d, SETPORTAL, new[] { 9, 5 }, null);
			d.AssignHexenIds(caps);
			PortalCreateContext ctx = Hexen();
			ctx.ActionName = a => a == LINEID ? "Line Identification" : null;

			PortalCreatePlan plan = PortalCreation.Plan(d, caps, ctx, Request(PortalCreateKind.AnchoredSector, 0, 1));

			Assert.Contains("Line 1 already has action 121 (Line Identification)", Assert.Single(plan.Confirms));
			Assert.Contains(PortalIssueCode.NoExit, Codes(plan.NewErrors));
			Assert.False(plan.CanApply);
			Assert.Equal(LINEID, d.Lines[1].Action);
			Assert.Equal(new[] { 9 }, d.Lines[1].Ids);
		}

		[Fact]
		public void Stacked_PairsTwoNewThingsOnAnUnusedTid()
		{
			PortalMapData d = TwoRooms();
			d.AddThing(1, 1, null, new Vector2D(32, 32), 0);
			PortalCreateContext ctx = Udmf();
			ctx.UsedThingIds.Add(2);
			PortalCreateRequest req = Request(PortalCreateKind.StackedSectors, 0, 1, 0, 0, 128);
			req.PositionA = new Vector2D(64, 64);
			req.PositionB = new Vector2D(1088, 64);

			PortalCreatePlan plan = PortalCreation.Plan(d, Caps(), ctx, req);

			Assert.True(plan.CanApply, plan.Refusal);
			Assert.Equal(new[] { UPPER, LOWER }, plan.ThingAdds.Select(t => t.Type));
			Assert.Equal(new[] { 0, 1 }, plan.ThingAdds.Select(t => t.Sector));
			Assert.All(plan.ThingAdds, t => Assert.Equal(3, t.Tid));
			Assert.All(plan.ThingAdds, t => Assert.Equal(128, t.Args[0]));
			Assert.Equal(PortalKind.StackedSectors, plan.Link.Kind);
			Assert.Equal(1, plan.Link.Origin.Element.Index);
			Assert.Equal(new Vector2D(1024, 0), plan.Link.Displacement);
			Assert.Single(d.Things);
		}

		[Fact]
		public void Plan_RefusesTheSameElementTwiceOrAnOutOfRangeIndex()
		{
			Assert.NotNull(PortalCreation.Plan(TwoRooms(), Caps(), Udmf(), Request(PortalCreateKind.LinePortal, 1, 1)).Refusal);
			Assert.NotNull(PortalCreation.Plan(TwoRooms(), Caps(), Udmf(), Request(PortalCreateKind.LinePortal, 0, 2)).Refusal);
			Assert.NotNull(PortalCreation.Plan(TwoRooms(), Caps(), Udmf(), Request(PortalCreateKind.StackedSectors, 0, 2)).Refusal);
		}
	}
}
