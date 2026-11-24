using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;

namespace Builder.Tests
{
	public class LightmapPreviewStateTests
	{
		[Fact]
		public void SourceDecision()
		{
			Assert.NotNull(MapManager.LightmapPreviewUnavailable(false, null, "abc"));
			Assert.NotNull(MapManager.LightmapPreviewUnavailable(true, "abc", null));
			Assert.NotNull(MapManager.LightmapPreviewUnavailable(true, "abc", "abd"));
			Assert.Null(MapManager.LightmapPreviewUnavailable(true, "abc", "abc"));
		}

		[Fact]
		public void StaleUntilCheckedAndAgainAfterAnyVersionMoves()
		{
			using(LightmapPreviewState s = new LightmapPreviewState())
			{
				Assert.True(s.Stale(0, 0));
				s.MarkChecked(5, 2);
				Assert.False(s.Stale(5, 2));
				Assert.True(s.Stale(6, 2));
				Assert.True(s.Stale(5, 3));
			}
		}

		[Fact]
		public void SettlesOnlyAfterTheMapHoldsStillForTheDebounce()
		{
			using(LightmapPreviewState s = new LightmapPreviewState())
			{
				long d = LightmapPreviewState.DEBOUNCE;
				s.MarkChecked(1, 0);
				Assert.False(s.Settled(1, 0, 0));

				// An edit, then another before the debounce ran out: the wait restarts
				Assert.False(s.Settled(2, 0, 100));
				Assert.False(s.Settled(3, 0, 100 + d - 1));
				Assert.False(s.Settled(3, 0, 100 + d));
				Assert.True(s.Settled(3, 0, 100 + 2 * d - 1));

				// Checked: nothing to do however long it holds still
				s.MarkChecked(3, 0);
				Assert.False(s.Settled(3, 0, 100 + 10 * d));

				// A replaced bake counts as a change
				Assert.False(s.Settled(3, 1, 100 + 10 * d));
				Assert.True(s.Settled(3, 1, 100 + 11 * d));
			}
		}

		[Fact]
		public void NothingShownWithoutAValidPreview()
		{
			using(LightmapPreviewState s = new LightmapPreviewState())
			{
				s.MarkChecked(1, 1);
				s.SetResult(null);
				Assert.Null(s.Shown(1, 1, true));
			}
		}

		private static MapTopology Square()
		{
			return new MapTopology
			{
				Vertices = new[] { new Vector2D(0, 0), new Vector2D(0, 64), new Vector2D(64, 64), new Vector2D(64, 0) },
				LineStart = new[] { 0, 1, 2, 3 }, LineEnd = new[] { 1, 2, 3, 0 },
				LineFront = new[] { 0, 1, 2, 3 }, LineBack = new[] { -1, -1, -1, -1 },
				SideSector = new[] { 0, 0, 0, 0 },
				SectorCount = 1,
			};
		}

		[Fact]
		public void TopologySameAsComparesEveryElement()
		{
			Assert.True(Square().SameAs(Square()));
			Assert.False(Square().SameAs(null));

			MapTopology moved = Square();
			moved.Vertices[2] = new Vector2D(64, 65);
			Assert.False(Square().SameAs(moved));

			MapTopology flipped = Square();
			flipped.LineStart[1] = 2;
			flipped.LineEnd[1] = 1;
			Assert.False(Square().SameAs(flipped));

			MapTopology resectored = Square();
			resectored.SectorCount = 2;
			resectored.SideSector[3] = 1;
			Assert.False(Square().SameAs(resectored));

			MapTopology twosided = Square();
			twosided.LineBack[0] = 3;
			Assert.False(Square().SameAs(twosided));
		}
	}
}
