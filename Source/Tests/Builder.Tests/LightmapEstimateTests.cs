using CodeImp.DoomBuilder.Compilers;

namespace Builder.Tests
{
	public class LightmapEstimateTests
	{
		private static LightmapEstimateSector Sec(double floor, double ceiling)
		{
			return new LightmapEstimateSector { Floor = floor, Ceiling = ceiling };
		}

		// A linedef's sides: the front runs x1,y1 to x2,y2, the back the other way
		private static void Line(List<LightmapEstimateSide> sides, double x1, double y1, double x2, double y2, int front, int back = -1)
		{
			sides.Add(new LightmapEstimateSide { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Sector = front, Other = back });
			if(back >= 0) sides.Add(new LightmapEstimateSide { X1 = x2, Y1 = y2, X2 = x1, Y2 = y1, Sector = back, Other = front });
		}

		private static List<LightmapEstimateSide> Box(double size, int sector)
		{
			var sides = new List<LightmapEstimateSide>();
			Line(sides, 0, 0, 0, size, sector);
			Line(sides, 0, size, size, size, sector);
			Line(sides, size, size, size, 0, sector);
			Line(sides, size, 0, 0, 0, sector);
			return sides;
		}

		[Fact]
		public void FixtureEstimateIsCloseToWhatTheBakerLit()
		{
			// The lightmap fixture's geometry: two rooms, a pillar sector carrying a 3D floor, its
			// control sector (which the baker skips and the estimate counts), and a zero length line
			// the baker removes. No upper or lower textures.
			var sectors = new List<LightmapEstimateSector> { Sec(0, 128), Sec(0, 160), Sec(0, 160), Sec(48, 64) };
			var sides = new List<LightmapEstimateSide>();
			Line(sides, 0, 0, 0, 256, 0);
			Line(sides, 0, 256, 256, 256, 0);
			Line(sides, 256, 256, 256, 0, 0, 1);
			Line(sides, 256, 0, 0, 0, 0);
			Line(sides, 256, 256, 512, 256, 1);
			Line(sides, 512, 256, 512, 0, 1);
			Line(sides, 512, 0, 256, 0, 1);
			Line(sides, 320, 64, 320, 192, 2, 1);
			Line(sides, 320, 192, 448, 192, 2, 1);
			Line(sides, 448, 192, 448, 64, 2, 1);
			Line(sides, 448, 64, 320, 64, 2, 1);
			Line(sides, 600, 0, 600, 64, 3);
			Line(sides, 600, 64, 664, 64, 3);
			Line(sides, 664, 64, 664, 0, 3);
			Line(sides, 664, 0, 600, 0, 3);
			Line(sides, 128, 128, 128, 128, 0);

			// The fork's CPU tracer ran 10,587 work items on it: 10,056 surface texels and 531 light probes
			const long BAKED = 10587 - 531;
			long estimate = LightmapBakeEstimate.Texels(sides, sectors, LightmapBakeEstimate.DEFAULT_MAP_DISTANCE, 1.0f, 1024);
			Assert.True(Math.Abs(estimate - BAKED) <= BAKED / 4, estimate + " against " + BAKED);
		}

		[Fact]
		public void SampleDimensionFollowsTheBaker()
		{
			// The baker scales, rounds half away from zero, clamps to 1, then rounds up to a power of two
			Assert.Equal(8, LightmapBakeEstimate.SampleDimension(0, 8, 1.0f));
			Assert.Equal(16, LightmapBakeEstimate.SampleDimension(12, 8, 1.0f));
			Assert.Equal(16, LightmapBakeEstimate.SampleDimension(0, 8, 1.5f));
			Assert.Equal(2, LightmapBakeEstimate.SampleDimension(0, 8, 0.25f));
			Assert.Equal(1, LightmapBakeEstimate.SampleDimension(1, 8, 0.25f));
			Assert.Equal(128, LightmapBakeEstimate.SampleDimension(0, 32, 4.0f));

			// lm_sampledistance is clamped to 8..128 and rounded up to a power of two
			Assert.Equal(8, LightmapBakeEstimate.MapDistance(0));
			Assert.Equal(16, LightmapBakeEstimate.MapDistance(9));
			Assert.Equal(128, LightmapBakeEstimate.MapDistance(4096));
		}

		[Fact]
		public void CoarserSamplingTracesFewerTexels()
		{
			var sectors = new List<LightmapEstimateSector> { Sec(0, 256) };
			List<LightmapEstimateSide> sides = Box(1024, 0);
			long final = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 4096);
			long scaled = LightmapBakeEstimate.Texels(sides, sectors, 8, 2.0f, 4096);
			long coarse = LightmapBakeEstimate.Texels(sides, sectors, 16, 1.0f, 4096);

			// Doubling the spacing quarters the area term; the borders keep it a little above a quarter
			Assert.Equal(scaled, coarse);
			Assert.True(scaled > final / 4 && scaled < final / 3, final + " then " + scaled);
		}

		[Fact]
		public void SurfaceOverridesAndPageClampApply()
		{
			var sectors = new List<LightmapEstimateSector> { Sec(0, 128) };
			List<LightmapEstimateSide> sides = Box(256, 0);
			long plain = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);

			// A coarser distance on one wall, then on the floor, each lowers the count
			LightmapEstimateSide wall = sides[0];
			wall.Mid = 32;
			sides[0] = wall;
			long coarsewall = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);
			Assert.True(coarsewall < plain);
			sectors[0] = new LightmapEstimateSector { Floor = 0, Ceiling = 128, FloorDistance = 32 };
			long coarsefloor = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);
			Assert.True(coarsefloor < coarsewall);

			// lm_sampledist is the side's fallback for its parts, a part's own key wins over it
			wall = sides[1];
			wall.Distance = 32;
			sides[1] = wall;
			long fallback = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);
			Assert.True(fallback < coarsefloor);
			wall.Mid = 8;
			sides[1] = wall;
			Assert.Equal(coarsefloor, LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024));

			// The baker clamps each axis of a surface to a page less its border: a 65536 x 8 wall at
			// size 128 is 126 x 3 texels; its sector's flats have no area, only the perimeter's border
			var longwall = new List<LightmapEstimateSide>();
			Line(longwall, 0, 0, 65536, 0, 0);
			long clamped = LightmapBakeEstimate.Texels(longwall,new List<LightmapEstimateSector> { Sec(0, 8) }, 8, 1.0f, 128);
			Assert.Equal(126L * 3 + 2 * (65536 / 8 + 4), clamped);
		}

		[Fact]
		public void OnlyTexturedStepsAreLit()
		{
			// A higher floor and lower ceiling across a two-sided line
			var sectors = new List<LightmapEstimateSector> { Sec(0, 128), Sec(32, 96) };
			var sides = new List<LightmapEstimateSide>();
			Line(sides, 0, 0, 0, 128, 0, 1);
			long bare = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);

			LightmapEstimateSide front = sides[0];
			front.Upper = true;
			sides[0] = front;
			long upper = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);
			front.Lower = true;
			sides[0] = front;
			long both = LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024);
			Assert.True(upper > bare && both > upper, bare + ", " + upper + ", " + both);

			// The back side faces down from the lower ceiling and up from the higher floor: no step to light
			LightmapEstimateSide back = sides[1];
			back.Upper = back.Lower = true;
			sides[1] = back;
			Assert.Equal(both, LightmapBakeEstimate.Texels(sides, sectors, 8, 1.0f, 1024));
		}

		[Fact]
		public void NoCostMeansNoEstimate()
		{
			Assert.True(LightmapBakeEstimate.Seconds(100000, 0, 4) < 0);
			Assert.Null(LightmapBakeEstimate.Describe(LightmapBakeEstimate.Seconds(100000, 0, 4)));
			Assert.Equal(25.0, LightmapBakeEstimate.Seconds(100000, 0.001, 4), 6);
			Assert.Equal(100.0, LightmapBakeEstimate.Seconds(100000, 0.001, 1), 6);

			var settings = new LightmapBakeSettings();
			Assert.Equal(0.0, settings.CostOf(new LightmapBakeQuality()));
		}

		[Fact]
		public void CostIsKeptPerPresetAndPerCustomSampleMix()
		{
			Assert.Equal("final", LightmapBakeEstimate.CostKey(new LightmapBakeQuality()));
			Assert.Equal("draft", LightmapBakeEstimate.CostKey(LightmapBakeQuality.Quick(1024)));

			// The scale changes the texel count, not the cost per texel; the sample counts change the cost
			var a = new LightmapBakeQuality { SampleScale = 0.25f, Samples = 1024 };
			var b = new LightmapBakeQuality { SampleScale = 1.0f, Samples = 1024 };
			var c = new LightmapBakeQuality { SampleScale = 0.25f, Samples = 16 };
			Assert.Equal(LightmapBakeEstimate.CostKey(a), LightmapBakeEstimate.CostKey(b));
			Assert.NotEqual(LightmapBakeEstimate.CostKey(a), LightmapBakeEstimate.CostKey(c));

			var settings = new LightmapBakeSettings();
			settings.Cost[LightmapBakeEstimate.CostKey(a)] = 0.001;
			Assert.Equal(0.001, settings.CostOf(b));
			Assert.Equal(0.0, settings.CostOf(c));
		}

		[Fact]
		public void CalibrationStartsAtTheFirstBakeAndMovesHalfwayAfter()
		{
			// 10,000 texels in 2 s on 4 threads: 0.0008 thread seconds each
			double cost = LightmapBakeEstimate.Calibrate(0, 10000, 2.0, 4);
			Assert.Equal(0.0008, cost, 9);
			cost = LightmapBakeEstimate.Calibrate(cost, 10000, 1.0, 4);
			Assert.Equal(0.0006, cost, 9);

			// A bake with nothing to learn from keeps the cost
			Assert.Equal(cost, LightmapBakeEstimate.Calibrate(cost, 0, 1.0, 4));
			Assert.Equal(cost, LightmapBakeEstimate.Calibrate(cost, 10000, 0, 4));
			Assert.Equal(cost, LightmapBakeEstimate.Calibrate(cost, 10000, 1.0, 0));

			// The calibrated cost predicts the bake it came from
			Assert.Equal(2.0, LightmapBakeEstimate.Seconds(10000, LightmapBakeEstimate.Calibrate(0, 10000, 2.0, 4), 4), 6);
		}

		[Fact]
		public void ProgressCountsTheTracedTexelsAndTellsTheTracer()
		{
			var p = new LightmapBakeProgress();
			p.Feed("Building level mesh...\nRay tracing in progress...\n0.0%\t0/10587\r", false);
			Assert.False(p.CpuTraced);
			p.Feed("100.0%\t10587/10587\n\nRay tracing complete\n   1.278 seconds.\n", false);
			Assert.Equal(10587L, p.TracedTexels);
			Assert.True(p.CpuTraced);

			// The GPU tracer ends with its own line, and its time says nothing about the CPU's
			p = new LightmapBakeProgress();
			p.Feed("Ray tracing in progress...\n0.0%\t0/500\r100.0%\t500/500\nRay trace complete\n", false);
			Assert.Equal("Writing output", p.Stage);
			Assert.Equal(500L, p.TracedTexels);
			Assert.False(p.CpuTraced);
		}
	}
}
