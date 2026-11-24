using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Portals;

namespace Builder.Tests
{
	public class PortalAnalyzerTests
	{
		private const int SETPORTAL = 156, QUICKPORTAL = 301, SECTORPORTAL = 57;
		private const int UPPER = 9077, LOWER = 9078, VIEWPOINT = 9080, PICKER = 9081, SKYCAM = 9083;

		private static readonly Dictionary<string, int> Actions = new Dictionary<string, int>
		{
			{ "Line_SetPortal", SETPORTAL }, { "Line_QuickPortal", QUICKPORTAL }, { "Sector_SetPortal", SECTORPORTAL },
			{ "Line_SetIdentification", 121 }, { "TranslucentLine", 208 }, { "Teleport_Line", 215 }, { "Scroll_Texture_Model", 222 },
			{ "Polyobj_StartLine", 1 }, { "Polyobj_ExplicitLine", 5 }, { "Plane_Align", 181 }, { "Static_Init", 190 },
		};

		private static readonly Dictionary<string, int> Things = new Dictionary<string, int>
		{
			{ "UpperStackLookOnly", UPPER }, { "LowerStackLookOnly", LOWER }, { "SkyViewpoint", VIEWPOINT },
			{ "SkyPicker", PICKER }, { "SkyCamCompat", SKYCAM },
		};

		private static PortalCapabilities Caps(bool hexen = false, bool tids = true, params string[] missing)
		{
			return PortalCapabilities.Create(
				id => Actions.TryGetValue(id, out int n) && !missing.Contains(id) ? n : 0,
				cls => Things.TryGetValue(cls, out int n) && !missing.Contains(cls) ? n : 0,
				hexen, tids);
		}

		private static PortalCapabilities Zandronum() { return Caps(false, true, "Line_SetPortal", "Line_QuickPortal"); }

		private static PortalLine Line(PortalMapData d, int action, int[] args, int[] ids, double x1 = 0, double y1 = 0, double x2 = 0, double y2 = 64, int front = -1, int back = -1)
		{
			return d.AddLine(action, args, ids, new Vector2D(x1, y1), new Vector2D(x2, y2), front, back);
		}

		private static PortalLine Plain(PortalMapData d, params int[] ids) { return Line(d, 0, null, ids); }

		private static PortalThing Thing(PortalMapData d, int type, int tid, double x = 0, double y = 0, int sector = -1, params int[] args)
		{
			return d.AddThing(type, tid, args, new Vector2D(x, y), sector);
		}

		private static PortalMapData Sectors(int count, params int[][] tags)
		{
			PortalMapData d = new PortalMapData();
			for(int i = 0; i < count; i++) d.AddSector(i < tags.Length ? tags[i] : new int[0]);
			return d;
		}

		private static List<PortalLink> Links(PortalReport r, PortalKind kind) { return r.Links.Where(l => l.Kind == kind).ToList(); }
		private static int? End(PortalEnd e) { return e == null ? (int?)null : e.Element.Index; }
		private static PortalIssueCode[] Codes(PortalReport r) { return r.Issues.Select(i => i.Code).ToArray(); }

		// "Code e1,e2;Code e1" for a link's issues, each with its element indices
		private static string IssueText(PortalLink link)
		{
			return string.Join(";", link.Issues.Select(i => i.Code + " " + string.Join(",", i.Elements.Select(e => e.Index))));
		}

		[Fact]
		public void LinePortalPair_IsOneTwoWayLinkWithLinkedDisplacement()
		{
			PortalMapData d = Sectors(4);
			Line(d, SETPORTAL, new[] { 2, 0, 3 }, new[] { 1 }, 0, 0, 0, 128, 0, 2);
			Line(d, SETPORTAL, new[] { 1, 0, 3 }, new[] { 2 }, 512, 128, 512, 0, 1, 3);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			PortalLink link = Assert.Single(r.Links);
			Assert.Equal(PortalKind.LinePortal, link.Kind);
			Assert.True(link.TwoWay);
			Assert.Equal(0, End(link.Origin));
			Assert.Equal(1, End(link.Destination));
			Assert.True(link.HasDisplacement);
			Assert.Equal(new Vector2D(512, 0), link.Displacement);
			Assert.Equal(new[] { 0, 1 }, link.Sectors);
			Assert.Empty(r.Issues);
		}

		[Fact]
		public void LinePortal_TakesTheLowestIndexOtherLineWithTheExitId_AndOnlyLinkedTypesGetADisplacement()
		{
			PortalMapData d = Sectors(1);
			Line(d, SETPORTAL, new[] { 7, 0, 0 }, new[] { 7 });
			Plain(d, 7);
			Plain(d, 7);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			PortalLink link = Assert.Single(r.Links);
			Assert.Equal(1, End(link.Destination));
			Assert.False(link.TwoWay);
			Assert.False(link.HasDisplacement);
		}

		[Theory]
		[InlineData(0, false)]
		[InlineData(5, false)]
		[InlineData(9, true)]
		public void LinePortal_NoExitWhenTheExitIdIsZeroOrCarriedByNoOtherLine(int exitid, bool found)
		{
			PortalMapData d = Sectors(1);
			Line(d, SETPORTAL, new[] { exitid, 0, 1 }, new[] { 5 }, front: 0, back: 0);
			Plain(d, 9);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			PortalLink link = Assert.Single(r.Links);
			Assert.Equal(found, link.Destination != null);
			Assert.Equal(found ? new PortalIssueCode[0] : new[] { PortalIssueCode.NoExit }, Codes(r));
		}

		[Fact]
		public void QuickPortal_PairsOnlyWithAnotherQuickPortalOfItsFirstId()
		{
			PortalMapData d = Sectors(4);
			Plain(d, 4);
			Line(d, QUICKPORTAL, new[] { 0 }, new[] { 4, 8 }, 0, 0, 0, 64, 0, 2);
			Line(d, QUICKPORTAL, new[] { 0 }, new[] { 4 }, 256, 64, 256, 0, 1, 3);
			Line(d, QUICKPORTAL, new[] { 1 }, new[] { 8 });
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			List<PortalLink> links = Links(r, PortalKind.QuickPortal);
			Assert.Equal(2, links.Count);
			Assert.True(links[0].TwoWay);
			Assert.Equal(new int?[] { 1, 2 }, new[] { End(links[0].Origin), End(links[0].Destination) });
			Assert.Equal(new Vector2D(256, 0), links[0].Displacement);

			// Line 3's first id is 8, which only line 1 also carries, but line 1 is already in a pair: a one-way visual link
			Assert.Equal(new int?[] { 3, 1 }, new[] { End(links[1].Origin), End(links[1].Destination) });
			Assert.False(links[1].HasDisplacement);
			Assert.Empty(r.Issues);
		}

		[Theory]
		[InlineData(0)]
		[InlineData(6)]
		public void QuickPortal_NoExitWithoutAPartner(int id)
		{
			PortalMapData d = Sectors(1);
			Line(d, QUICKPORTAL, new[] { 0 }, id == 0 ? new int[0] : new[] { id }, front: 0, back: 0);
			Plain(d, 6);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());
			Assert.Equal(new[] { PortalIssueCode.NoExit }, Codes(r));
		}

		[Fact]
		public void LinkedLineEE_PairsTheArg0ZeroLineWithEveryArg0OneLineOfItsFirstId()
		{
			// Each line is two-sided in its own sector, so every sector is its own portal group
			PortalMapData d = Sectors(5);
			Line(d, SETPORTAL, new[] { 0, 0, 4 }, new[] { 3 }, 0, 0, 0, 64, 0, 0);
			Line(d, SETPORTAL, new[] { 1, 0, 4 }, new[] { 3 }, 128, 64, 128, 0, 1, 1);
			Line(d, SETPORTAL, new[] { 1, 0, 4 }, new[] { 2 }, front: 2, back: 2);
			Line(d, SETPORTAL, new[] { 0, 0, 4 }, new int[0], 0, 0, 0, 64, 3, 3);
			Line(d, SETPORTAL, new[] { 1, 0, 4 }, new int[0], 128, 64, 128, 0, 4, 4);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			List<PortalLink> links = Links(r, PortalKind.LinkedLineEE);
			Assert.Equal(new[] { "0-1", "3-4", "-2" }, links.Select(l => End(l.Origin) + "-" + End(l.Destination)).ToArray());
			Assert.All(links.Take(2), l => Assert.True(l.TwoWay && l.HasDisplacement));
			PortalIssue issue = Assert.Single(r.Issues);
			Assert.Equal(PortalIssueCode.NoExit, issue.Code);
			Assert.Equal(2, issue.Elements[0].Index);
		}

		[Fact]
		public void AnchoredPortal_TakesTheFirstMatchingReference_AndCoversItsTaggedSectors()
		{
			PortalMapData d = Sectors(4, new[] { 5 }, new int[0], new[] { 9, 5 });
			Line(d, SECTORPORTAL, new[] { 5, 0, 1, 0 }, null, 0, 0, 0, 64, 0);
			Line(d, SECTORPORTAL, new[] { 5, 0, 0, 1 }, null, 900, 0, 900, 64);
			Line(d, SECTORPORTAL, new[] { 5, 0, 1, 1 }, null, 300, 0, 300, 64, 1);
			Line(d, SECTORPORTAL, new[] { 5, 0, 1, 1 }, null, 600, 0, 600, 64);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			List<PortalLink> links = Links(r, PortalKind.AnchoredSector);
			Assert.Equal(2, links.Count);
			Assert.Equal(new int?[] { 0, 2 }, new[] { End(links[0].Origin), End(links[0].Destination) });
			Assert.Equal(new Vector2D(300, 0), links[0].Displacement);
			Assert.Equal(new[] { 0, 2 }, links[0].Sectors);

			// Line 1's plane differs, so no anchor builds it; line 3 is a duplicate reference, not a lone anchor
			Assert.Equal(new int?[] { null, 1 }, new[] { End(links[1].Origin), End(links[1].Destination) });
			Assert.Equal(new[] { PortalIssueCode.DuplicateAnchor }, links[0].Issues.Select(i => i.Code).ToArray());
			Assert.Equal(new[] { 2, 3 }, links[0].Issues[0].Elements.Select(e => e.Index).ToArray());
			PortalIssue lone = Assert.Single(links[1].Issues);
			Assert.Equal(PortalIssueCode.LoneAnchor, lone.Code);
			Assert.Equal(1, lone.Elements[0].Index);
		}

		[Fact]
		public void AnchoredPortal_LoneAnchorWithoutAReference_AndTagZeroCoversUntaggedSectors()
		{
			PortalMapData d = Sectors(3, new[] { 2 });
			Line(d, SECTORPORTAL, new[] { 0, 6, 0, 0 }, null);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			PortalLink link = Assert.Single(r.Links);
			Assert.Null(link.Destination);
			Assert.Equal(new[] { 1, 2 }, link.Sectors);
			Assert.Equal(new[] { PortalIssueCode.ZeroSectorTag, PortalIssueCode.LoneAnchor }, Codes(r));
		}

		[Fact]
		public void StackedThings_EveryPointLinksToTheHighestIndexPartner_AndTheOffsetUsesThePartnersLastPicker()
		{
			PortalMapData d = Sectors(6);
			Thing(d, UPPER, 4, 0, 0, 0);     // 0: picks lower 1, which picks upper 2: one-way
			Thing(d, LOWER, 4, 100, 50, 1);  // 1: mutual with upper 2
			Thing(d, UPPER, 4, 10, 10, 2);
			Thing(d, LOWER, 5, 0, 0, 3);     // 3: picks upper 4, which picks lower 5: one-way
			Thing(d, UPPER, 5, 100, 0, 4);
			Thing(d, LOWER, 5, 30, 0, 5);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			// The engine's offset is partner minus partner->target, the highest-index point that picked the partner
			Assert.Equal(new[] { "0->1 90,40", "2<->1 90,40", "3->4 70,0", "4<->5 -70,0" },
				r.Links.Select(l => End(l.Origin) + (l.TwoWay ? "<->" : "->") + End(l.Destination) + " " + l.Displacement.x + "," + l.Displacement.y).ToArray());
			Assert.Equal(new[] { 0, 1 }, r.Links[0].Sectors);
			Assert.Equal("Upper Sector thing", r.Links[1].Origin.Role);
			Assert.Equal("Lower Sector thing", r.Links[2].Origin.Role);

			// Only the one-way rows come from a duplicate of their class, and each names the partner's pick
			Assert.Equal(new[] { "StackDuplicate 0,2", "", "StackDuplicate 3,5", "" }, r.Links.Select(IssueText).ToArray());
		}

		[Fact]
		public void StackedThings_EveryPointWithoutAPartnerIsUnpaired()
		{
			PortalMapData d = Sectors(1);
			Thing(d, UPPER, 6);
			Thing(d, UPPER, 6);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { 0, 1 }, r.Issues.Where(i => i.Code == PortalIssueCode.StackUnpaired).Select(i => i.Elements[0].Index).ToArray());
		}

		[Fact]
		public void StackedThings_UnpairedWithoutThePartnerClassOrWithTidZero()
		{
			PortalMapData d = Sectors(1);
			Thing(d, UPPER, 4);
			Thing(d, LOWER, 0);
			Thing(d, UPPER, 0);
			Thing(d, LOWER, 5);
			Thing(d, VIEWPOINT, 5);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(4, r.Links.Count);
			Assert.All(r.Links, l => Assert.Equal(PortalIssueCode.StackUnpaired, Assert.Single(l.Issues).Code));
			Assert.Equal(new[] { 0, 1, 2, 3 }, r.Issues.Select(i => i.Elements[0].Index).OrderBy(i => i).ToArray());
		}

		[Fact]
		public void Skybox_TheViewpointTakesTheFirstTypeTwoLineOfItsSector_AndCopiesAttachToMatchingSources()
		{
			PortalMapData d = Sectors(3, new[] { 7 }, new int[0], new[] { 8 });
			Thing(d, SKYCAM, 0, 0, 0, 1);
			Line(d, SECTORPORTAL, new[] { 7, 2, 1 }, null, front: 2, back: 1);  // 0: picked, the viewpoint's sector is its back
			Line(d, SECTORPORTAL, new[] { 7, 2, 1 }, null, front: 1);           // 1: second in the sector, a lone line
			Line(d, SECTORPORTAL, new[] { 8, 0, 0, 0 }, null);                  // 2: anchored floor portal on tag 8
			Line(d, SECTORPORTAL, new[] { 8, 0, 0, 1 }, null);                  // 3: its reference
			Line(d, SECTORPORTAL, new[] { 0, 1, 1, 7 }, null);                  // 4: copies the skybox (plane matches)
			Line(d, SECTORPORTAL, new[] { 0, 1, 3, 7 }, null);                  // 5: copies the skybox (plane 3 = any)
			Line(d, SECTORPORTAL, new[] { 0, 1, 1, 8 }, null);                  // 6: plane mismatch with tag 8's floor portal
			Line(d, SECTORPORTAL, new[] { 0, 5, 0, 7 }, null);                  // 7: copy to line, from the skybox
			Line(d, SECTORPORTAL, new[] { 0, 5, 0, 8 }, null);                  // 8: copy to line ignores anchored portals
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			List<PortalLink> sky = Links(r, PortalKind.Skybox);
			Assert.Equal(2, sky.Count);
			Assert.Equal(new int?[] { 0, 0 }, new[] { End(sky[0].Origin), End(sky[0].Destination) });
			Assert.Equal(PortalElementType.Thing, sky[0].Destination.Element.Type);
			Assert.Equal(new[] { 4, 5, 7 }, sky[0].Dependents.Select(e => e.Element.Index).ToArray());
			Assert.Equal(new[] { 0 }, sky[0].Sectors);
			Assert.Equal(1, End(sky[1].Origin));
			Assert.Null(sky[1].Destination);

			PortalLink anchored = Assert.Single(Links(r, PortalKind.AnchoredSector));
			Assert.Empty(anchored.Dependents);
			Assert.Equal(new[] { "CopyNoSource 6", "CopyToLineAnchored 8" }, Links(r, PortalKind.Copy).Select(IssueText).ToArray());
		}

		[Fact]
		public void PlaneAndHorizonPortals_StandAlone()
		{
			PortalMapData d = Sectors(2, new[] { 3 });
			Line(d, SECTORPORTAL, new[] { 3, 3, 0 }, null);
			Line(d, SECTORPORTAL, new[] { 3, 4, 1 }, null);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			List<PortalLink> links = Links(r, PortalKind.PlaneHorizon);
			Assert.Equal(new[] { 3, 4 }, links.Select(l => l.Type).ToArray());
			Assert.All(links, l => Assert.Equal(new[] { 0 }, l.Sectors));
			Assert.Empty(r.Issues);
		}

		[Fact]
		public void Copies_OnZandronumOnlyTypeOneCopiesTypeZeroPortalsOnTheExactPlane()
		{
			PortalMapData d = Sectors(3, new[] { 7 }, new int[0], new[] { 8 });
			Thing(d, SKYCAM, 0, 0, 0, 1);
			Line(d, SECTORPORTAL, new[] { 7, 2, 1 }, null, front: 1);  // 0: skybox, which Zandronum never copies
			Line(d, SECTORPORTAL, new[] { 8, 0, 0, 0 }, null);         // 1: anchored floor portal on tag 8
			Line(d, SECTORPORTAL, new[] { 8, 0, 0, 1 }, null);         // 2: its reference
			Line(d, SECTORPORTAL, new[] { 0, 1, 0, 8 }, null);         // 3: copies it
			Line(d, SECTORPORTAL, new[] { 0, 1, 3, 8 }, null);         // 4: plane 3 = any is UZDoom only
			Line(d, SECTORPORTAL, new[] { 0, 1, 1, 7 }, null);         // 5: skybox copy
			Line(d, SECTORPORTAL, new[] { 0, 5, 0, 7 }, null);         // 6: copy to line
			PortalReport zan = PortalAnalyzer.Analyze(d, Zandronum());
			PortalReport uz = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { 3 }, Assert.Single(Links(zan, PortalKind.AnchoredSector)).Dependents.Select(e => e.Element.Index).ToArray());
			Assert.Empty(Assert.Single(Links(zan, PortalKind.Skybox)).Dependents);
			Assert.Equal(new[] { "CopyNoSource 4", "CopyNoSource 5" }, Links(zan, PortalKind.Copy).Select(IssueText).ToArray());
			Assert.Equal("TypeUnsupported 6", IssueText(Assert.Single(Links(zan, PortalKind.Ignored))));

			Assert.Equal(new[] { 3, 4 }, Assert.Single(Links(uz, PortalKind.AnchoredSector)).Dependents.Select(e => e.Element.Index).ToArray());
			Assert.Equal(new[] { 5, 6 }, Assert.Single(Links(uz, PortalKind.Skybox)).Dependents.Select(e => e.Element.Index).ToArray());
			Assert.Empty(Links(uz, PortalKind.Copy));
		}

		[Theory]
		[InlineData(0, 1, true)]
		[InlineData(6, 1, true)]
		[InlineData(0, 2, true)]
		[InlineData(6, 2, false)]
		[InlineData(0, 3, false)]
		public void AnchoredPortal_ThePlaneDecidesWhetherItIsBuilt(int type, int plane, bool built)
		{
			PortalMapData d = Sectors(1, new[] { 5 });
			Line(d, SECTORPORTAL, new[] { 5, type, plane, 0 }, null, 0, 0, 0, 64);
			Line(d, SECTORPORTAL, new[] { 5, type, plane, 1 }, null, 128, 0, 128, 64);
			Line(d, SECTORPORTAL, new[] { 0, 1, plane, 5 }, null);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			PortalLink link = Assert.Single(Links(r, PortalKind.AnchoredSector));
			Assert.Equal(1, End(link.Destination));
			Assert.Equal(built, link.HasDisplacement);
			Assert.Equal(built ? 1 : 0, link.Dependents.Count);
			Assert.Equal(built ? 0 : 1, Links(r, PortalKind.Copy).Count);
		}

		[Fact]
		public void MutualLinesOfDifferentTypes_StayTwoOneWayRows()
		{
			PortalMapData d = Sectors(2);
			Line(d, SETPORTAL, new[] { 2, 0, 0 }, new[] { 1 }, 0, 0, 0, 128, 0);
			Line(d, SETPORTAL, new[] { 1, 0, 3 }, new[] { 2 }, 512, 128, 512, 0, 1);
			Line(d, QUICKPORTAL, new[] { 0 }, new[] { 9 });
			Line(d, QUICKPORTAL, new[] { 1 }, new[] { 9 });
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { "0->1 0 False", "1->0 3 True" },
				Links(r, PortalKind.LinePortal).Select(l => End(l.Origin) + "->" + End(l.Destination) + " " + l.Type + " " + l.HasDisplacement).ToArray());
			Assert.Equal(new[] { "2->3 3", "3->2 0" },
				Links(r, PortalKind.QuickPortal).Select(l => End(l.Origin) + "->" + End(l.Destination) + " " + l.Type).ToArray());
			Assert.All(r.Links, l => Assert.False(l.TwoWay));
		}

		[Fact]
		public void SkyPicker_TakesTheHighestIndexViewpointClassThingWithItsTid()
		{
			PortalMapData d = Sectors(1);
			Thing(d, PICKER, 0, 0, 0, 0, 3);
			Thing(d, VIEWPOINT, 3);
			Thing(d, SKYCAM, 3);
			Thing(d, PICKER, 0, 0, 0, 0, 0);
			Thing(d, PICKER, 0, 0, 0, 0, 44);
			Thing(d, 1, 44);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			List<PortalLink> links = Links(r, PortalKind.SkyPicker);
			Assert.Equal(new[] { "0-2", "4-" }, links.Select(l => End(l.Origin) + "-" + End(l.Destination)).ToArray());
			Assert.Equal(new[] { "", "PickerNoViewpoint 4" }, links.Select(IssueText).ToArray());
		}

		[Fact]
		public void HexenLineIds_FollowTheEngineTable()
		{
			PortalCapabilities caps = Caps(hexen: true);
			var rows = new (int action, int[] args, int id)[]
			{
				(121, new[] { 9, 1, 0, 0, 2 }, 9 + 512),
				(208, new[] { 11, 0, 0, 0, 0 }, 11),
				(215, new[] { 12, 0, 0, 0, 0 }, 12),
				(222, new[] { 13, 0, 0, 0, 0 }, 13),
				(1, new[] { 0, 0, 0, 14, 0 }, 14),
				(5, new[] { 0, 0, 0, 0, 15 }, 15),
				(181, new[] { 0, 0, 16, 0, 0 }, 16),
				(190, new[] { 17, 3, 0, 0, 0 }, 17),
				(190, new[] { 17, 1, 0, 0, 0 }, 0),
				(156, new[] { 5, 18, 0, 0, 0 }, 18),
				(301, new[] { 19, 19, 19, 19, 19 }, 0),
				(0, new[] { 20, 20, 20, 20, 20 }, 0),
			};
			List<string> failures = new List<string>();
			foreach(var row in rows)
			{
				int got = PortalLineIds.ForHexen(caps, row.action, row.args);
				if(got != row.id) failures.Add(row.action + ": expected " + row.id + ", got " + got);
			}
			Assert.Empty(failures);
		}

		[Fact]
		public void HexenFormat_LinePortalFindsAnExitThatCarriesItsIdThroughLineSetIdentification()
		{
			PortalCapabilities caps = Caps(hexen: true);
			PortalMapData d = Sectors(1);
			Line(d, SETPORTAL, new[] { 9, 1, 3 }, new[] { 99 });
			Line(d, 121, new[] { 9, 0, 0, 0, 0 }, null);
			Line(d, SETPORTAL, new[] { 1, 9, 3 }, null);
			d.AssignHexenIds(caps);
			PortalReport r = PortalAnalyzer.Analyze(d, caps);

			// The 121 line has the lower index, so the engine exits there, not at the other portal line
			Assert.Equal(new[] { 1 }, d.Lines[0].Ids);
			PortalLink first = r.Links[0];
			Assert.Equal(new int?[] { 0, 1 }, new[] { End(first.Origin), End(first.Destination) });
			Assert.False(first.TwoWay);
		}

		[Fact]
		public void Capabilities_GateKinds()
		{
			PortalMapData d = Sectors(2, new[] { 5 });
			Line(d, SETPORTAL, new[] { 2, 1, 0 }, new[] { 1 });
			Line(d, SETPORTAL, new[] { 1, 2, 0 }, new[] { 2 });
			Line(d, QUICKPORTAL, new[] { 0 }, new[] { 3 });
			Line(d, QUICKPORTAL, new[] { 0 }, new[] { 3 });
			Line(d, SECTORPORTAL, new[] { 5, 0, 0, 0 }, null);
			Line(d, SECTORPORTAL, new[] { 5, 0, 0, 1 }, null);
			Thing(d, UPPER, 6);
			Thing(d, LOWER, 6);

			Assert.Equal(new[] { PortalKind.LinePortal, PortalKind.QuickPortal, PortalKind.AnchoredSector, PortalKind.StackedSectors },
				PortalAnalyzer.Analyze(d, Caps()).Links.Select(l => l.Kind).ToArray());
			Assert.Equal(new[] { PortalKind.AnchoredSector, PortalKind.StackedSectors },
				PortalAnalyzer.Analyze(d, Zandronum()).Links.Select(l => l.Kind).ToArray());
			Assert.Equal(new[] { PortalKind.LinePortal, PortalKind.QuickPortal, PortalKind.AnchoredSector },
				PortalAnalyzer.Analyze(d, Caps(tids: false)).Links.Select(l => l.Kind).ToArray());

			PortalCapabilities eternity = Caps(false, true, "UpperStackLookOnly");
			Assert.False(eternity.Any);
			Assert.Equal(0, eternity.SectorSetPortal);
			Assert.Empty(PortalAnalyzer.Analyze(d, eternity).Links);
		}

		// Line 0 (type a, plane align) exits at line 1 (type b, or a plain line when b is -1). A line two-sided in its own
		// sector keeps that sector a portal group of its own.
		[Theory]
		[InlineData(3, 3, 0, false, "")]
		[InlineData(3, 3, 0, true, "OneSidedTraversable 0;ReturnNotLinked 1,0")]
		[InlineData(2, 2, 1, false, "InteractivePlaneAlign 0")]
		[InlineData(2, 2, 0, false, "")]
		[InlineData(3, -1, 0, false, "NoReturn 0,1")]
		[InlineData(2, -1, 0, false, "NoReturn 0,1")]
		[InlineData(1, -1, 0, false, "")]
		[InlineData(3, 2, 0, false, "ReturnNotLinked 0,1")]
		public void LinePortal_TheEnginesDemotions(int typea, int typeb, int align, bool onesided, string expected)
		{
			PortalMapData d = Sectors(2);
			Line(d, SETPORTAL, new[] { 2, 0, typea, align }, new[] { 1 }, 0, 0, 0, 128, 0, onesided ? -1 : 0);
			if(typeb >= 0) Line(d, SETPORTAL, new[] { 1, 0, typeb }, new[] { 2 }, 512, 128, 512, 0, 1, 1);
			else Line(d, 0, null, new[] { 2 }, 512, 128, 512, 0, 1, 1);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(expected, string.Join(";", r.Links.Select(IssueText).Where(s => s.Length > 0)));
		}

		[Fact]
		public void AmbiguousExit_WarnsWhenSeveralLinesCarryTheExitId_AndNamesTheEnginesPick()
		{
			PortalMapData d = Sectors(3);
			Line(d, SETPORTAL, new[] { 7, 0, 0 }, new[] { 1 }, front: 0, back: 0);
			Plain(d, 7);
			Plain(d, 7);
			Line(d, SETPORTAL, new[] { 8, 0, 0 }, new[] { 2 }, front: 0, back: 0);
			Plain(d, 8);
			Line(d, QUICKPORTAL, new[] { 1 }, new[] { 4 }, front: 0, back: 0);
			Line(d, QUICKPORTAL, new[] { 1 }, new[] { 4 }, front: 1, back: 1);
			Line(d, QUICKPORTAL, new[] { 1 }, new[] { 4 }, front: 2, back: 2);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { "AmbiguousExit 0,1,2", "" }, Links(r, PortalKind.LinePortal).Select(IssueText).ToArray());
			Assert.Equal(new[] { "AmbiguousExit 5,6,7;AmbiguousExit 6,5,7", "AmbiguousExit 7,5,6" }, Links(r, PortalKind.QuickPortal).Select(IssueText).ToArray());
		}

		// Line 0 runs (0,0)-(0,128); line 1 runs (x1,y1)-(x2,y2)
		[Theory]
		[InlineData(3, 512, 128, 512, 0, "")]
		[InlineData(3, 512, 0, 512, 128, "LinkedGeometry 0,1")]
		[InlineData(3, 512, 128, 512, 64, "LinkedGeometry 0,1")]
		[InlineData(0, 512, 128, 512, 64, "LengthMismatch 0,1")]
		[InlineData(0, 512, 0, 512, 128, "")]
		public void LinkedLinesMustBeReversedCopies_OtherTypesOnlyWarnOnLength(int type, double x1, double y1, double x2, double y2, string expected)
		{
			PortalMapData d = Sectors(2);
			Line(d, SETPORTAL, new[] { 2, 0, type }, new[] { 1 }, 0, 0, 0, 128, 0, 0);
			Line(d, SETPORTAL, new[] { 1, 0, type }, new[] { 2 }, x1, y1, x2, y2, 1, 1);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(expected, IssueText(Assert.Single(r.Links)));
		}

		[Theory]
		[InlineData(false, true, "SameGroup 0,1")]
		[InlineData(false, false, "")]
		[InlineData(true, true, "SameGroup 0,1")]
		[InlineData(true, false, "")]
		public void SameGroup_LinkedEndsWhoseSectorsConnectThroughTwoSidedLines(bool sectorportal, bool joined, string expected)
		{
			PortalMapData d = Sectors(3, new[] { 5 });
			if(sectorportal)
			{
				Line(d, SECTORPORTAL, new[] { 5, 6, 0, 0 }, null, 0, 0, 0, 64, 1, 1);
				Line(d, SECTORPORTAL, new[] { 5, 6, 0, 1 }, null, 512, 0, 512, 64, 2, 2);
			}
			else
			{
				Line(d, SETPORTAL, new[] { 2, 0, 3 }, new[] { 1 }, 0, 0, 0, 128, 1, 1);
				Line(d, SETPORTAL, new[] { 1, 0, 3 }, new[] { 2 }, 512, 128, 512, 0, 2, 2);
			}
			if(joined) Line(d, 0, null, null, front: 1, back: 2);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(expected, IssueText(Assert.Single(r.Links)));
		}

		// Two linked sector portals from sector 1's group to sector 2's: the engine disables the second when it disagrees
		[Theory]
		[InlineData(256, "")]
		[InlineData(300, "DisplacementConflict 2,3")]
		public void DisplacementConflict_LinkedPortalsBetweenTheSameGroupsShareOneOffset(double secondx, string expected)
		{
			PortalMapData d = Sectors(3, new[] { 5 }, new[] { 6 });
			Line(d, SECTORPORTAL, new[] { 5, 6, 0, 0 }, null, 0, 0, 0, 64, 1, 1);
			Line(d, SECTORPORTAL, new[] { 5, 6, 0, 1 }, null, 256, 0, 256, 64, 2, 2);
			Line(d, SECTORPORTAL, new[] { 6, 6, 0, 0 }, null, 10, 0, 10, 64, 1, 1);
			Line(d, SECTORPORTAL, new[] { 6, 6, 0, 1 }, null, 10 + secondx, 0, 10 + secondx, 64, 2, 2);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { "", expected }, r.Links.Select(IssueText).ToArray());
		}

		// A portal from sector 1's group to sector 2's, and one back: the way back must be the exact reverse
		[Theory]
		[InlineData(-256, "")]
		[InlineData(-200, "DisplacementConflict 2,3,0,1")]
		public void DisplacementConflict_TheWayBackMustReverseTheWayThere(double backx, string expected)
		{
			PortalMapData d = Sectors(3, new[] { 5 }, new[] { 6 });
			Line(d, SECTORPORTAL, new[] { 5, 6, 0, 0 }, null, 0, 0, 0, 64, 1, 1);
			Line(d, SECTORPORTAL, new[] { 5, 6, 0, 1 }, null, 256, 0, 256, 64, 2, 2);
			Line(d, SECTORPORTAL, new[] { 6, 6, 1, 0 }, null, 300, 0, 300, 64, 2, 2);
			Line(d, SECTORPORTAL, new[] { 6, 6, 1, 1 }, null, 300 + backx, 0, 300 + backx, 64, 1, 1);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { "", expected }, r.Links.Select(IssueText).ToArray());
		}

		[Fact]
		public void UnknownAndUnsupportedTypes_GetARowOfLinesTheEngineIgnores()
		{
			PortalMapData d = Sectors(1, new[] { 3 });
			Line(d, SETPORTAL, new[] { 1, 0, 7 }, null);
			Line(d, SETPORTAL, new[] { 2, 0, 4 }, null);     // type 4 needs arg0 0 or 1
			Line(d, SECTORPORTAL, new[] { 3, 9, 0 }, null);
			Line(d, SECTORPORTAL, new[] { 3, 3, 0 }, null);  // a plane portal, which Zandronum lacks

			Assert.Equal(new[] { "UnknownType 0", "UnknownType 1", "UnknownType 2" },
				Links(PortalAnalyzer.Analyze(d, Caps()), PortalKind.Ignored).Select(IssueText).ToArray());
			Assert.Equal(new[] { "UnknownType 2", "TypeUnsupported 3" },
				Links(PortalAnalyzer.Analyze(d, Zandronum()), PortalKind.Ignored).Select(IssueText).ToArray());
		}

		[Fact]
		public void SectorTag_ZeroHitsEveryUntaggedSector_AndATagNoSectorHasShowsNowhere()
		{
			PortalMapData d = Sectors(2, new[] { 4 });
			Line(d, SECTORPORTAL, new[] { 0, 3, 0 }, null);
			Line(d, SECTORPORTAL, new[] { 7, 4, 1 }, null);
			Line(d, SECTORPORTAL, new[] { 4, 4, 1 }, null);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(new[] { "ZeroSectorTag 0", "NoTaggedSector 1", "" }, Links(r, PortalKind.PlaneHorizon).Select(IssueText).ToArray());
		}

		[Theory]
		[InlineData(true, 0, 3, "BadPlane Error", false)]
		[InlineData(true, 6, 2, "BadPlane Error", false)]
		[InlineData(true, 0, 2, "", true)]
		[InlineData(false, 0, 3, "BadPlane Warning", true)]
		[InlineData(false, 0, -1, "BadPlane Error", false)]
		public void BadPlane_UZDoomBuildsOnlyPlanesZeroToTwo_ZandronumTakesAboveTwoAsTheCeiling(bool uzdoom, int type, int plane, string expected, bool built)
		{
			PortalMapData d = Sectors(1, new[] { 5 });
			Line(d, SECTORPORTAL, new[] { 5, type, plane, 0 }, null, 0, 0, 0, 64);
			Line(d, SECTORPORTAL, new[] { 5, type, plane, 1 }, null, 128, 0, 128, 64);
			PortalLink link = Assert.Single(Links(PortalAnalyzer.Analyze(d, uzdoom ? Caps() : Zandronum()), PortalKind.AnchoredSector));

			Assert.Equal(expected, string.Join(";", link.Issues.Select(i => i.Code + " " + i.Severity)));
			Assert.Equal(built, link.HasDisplacement);
		}

		[Fact]
		public void DuplicateAnchors_UZDoomKeepsTheFirstPortalOnASector_ZandronumTheLast()
		{
			PortalMapData d = Sectors(1, new[] { 5 });
			Line(d, SECTORPORTAL, new[] { 5, 0, 1, 0 }, null);
			Line(d, SECTORPORTAL, new[] { 5, 0, 1, 0 }, null);
			Line(d, SECTORPORTAL, new[] { 5, 0, 1, 1 }, null);

			Assert.Equal(new[] { "", "DuplicateAnchor 1,0" }, Links(PortalAnalyzer.Analyze(d, Caps()), PortalKind.AnchoredSector).Select(IssueText).ToArray());
			Assert.Equal(new[] { "DuplicateAnchor 0,1", "" }, Links(PortalAnalyzer.Analyze(d, Zandronum()), PortalKind.AnchoredSector).Select(IssueText).ToArray());
		}

		// Sector 1, tagged 5 with sector 0, has a sloped ceiling and a flat floor
		[Theory]
		[InlineData(1, "LinkedSloped 1")]
		[InlineData(0, "")]
		public void LinkedSloped_ALinkedPortalOnASlopedPlaneIsDisabled(int plane, string expected)
		{
			PortalMapData d = Sectors(2, new[] { 5 }, new[] { 5 });
			d.Sectors[1].CeilingSloped = true;
			Line(d, SECTORPORTAL, new[] { 5, 6, plane, 0 }, null, 0, 0, 0, 64);
			Line(d, SECTORPORTAL, new[] { 5, 6, plane, 1 }, null, 128, 0, 128, 64);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(expected, IssueText(Assert.Single(r.Links)));
		}

		[Theory]
		[InlineData(0, 128, 0, "OpacityMismatch 0,1")]
		[InlineData(0, 128, 128, "")]
		[InlineData(6, 128, 0, "")]
		public void OpacityMismatch_TheEnginesReadOpacityFromDifferentAnchors(int type, int anchoralpha, int refalpha, string expected)
		{
			PortalMapData d = Sectors(1, new[] { 5 });
			Line(d, SECTORPORTAL, new[] { 5, type, 0, 0, anchoralpha }, null, 0, 0, 0, 64);
			Line(d, SECTORPORTAL, new[] { 5, type, 0, 1, refalpha }, null, 128, 0, 128, 64);
			PortalReport r = PortalAnalyzer.Analyze(d, Caps());

			Assert.Equal(expected, IssueText(Assert.Single(r.Links)));
		}

		[Fact]
		public void PortalGroups_JoinSectorsThroughTwoSidedLinesOnly()
		{
			PortalMapData d = Sectors(4);
			Line(d, 0, null, null, front: 2, back: 1);
			Line(d, 0, null, null, front: 3);
			Line(d, 0, null, null, front: 3, back: 2);
			Assert.Equal(new[] { 0, 1, 1, 1 }, PortalAnalyzer.ComputeGroups(d));

			d.Lines.RemoveAt(2);
			Assert.Equal(new[] { 0, 1, 1, 2 }, PortalAnalyzer.ComputeGroups(d));
		}
	}
}
