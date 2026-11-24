using System.Diagnostics;
using System.Reflection;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	public class PlotterThicknessTests
	{
		private const int W = 64;
		private const int H = 48;
		private static PixelColor ink = new PixelColor(255, 200, 100, 50);

		// DrawLineSolid flips y (row = height - y); these take buffer rows
		private static void Line(Plotter p, int x1, int row1, int x2, int row2, int thickness, uint mask = 0xffffffff)
		{
			p.DrawLineSolid(x1, p.Height - row1, x2, p.Height - row2, ref ink, mask, thickness);
		}

		private static bool[,] Pixels(Plotter p)
		{
			bool[,] set = new bool[p.Width, p.Height];
			for(int y = 0; y < p.Height; y++)
				for(int x = 0; x < p.Width; x++)
					set[x, y] = p.GetPixel(x, y).ToInt() != 0;
			return set;
		}

		// The 1 px line before thickness existed (its checked branch, which the unchecked one matches on screen)
		private static bool[,] FrozenThinLine(int x1, int y1, int x2, int y2, uint mask)
		{
			bool[,] set = new bool[W, H];
			if(((x1 < 0) && (x2 < 0)) || ((x1 > W) && (x2 > W)) || ((y1 < 0) && (y2 < 0)) || ((y1 > H) && (y2 > H))) return set;
			int dx = x2 - x1, dy = y2 - y1;
			int dxabs = Math.Abs(dx), dyabs = Math.Abs(dy);
			int x = dyabs >> 1, y = dxabs >> 1;
			int sdx = Math.Sign(dx), sdy = Math.Sign(dy);
			int px = x1, py = y1;
			Action plot = () => { if((px >= 0) && (px < W) && (py >= 0) && (py < H)) set[px, py] = true; };
			plot();
			if(dxabs >= dyabs)
			{
				for(int i = 0; i < dxabs; i++)
				{
					y += dyabs;
					if(y >= dxabs) { y -= dxabs; py += sdy; }
					px += sdx;
					if((mask & (1 << (i & 0x7))) != 0) plot();
				}
			}
			else
			{
				for(int i = 0; i < dyabs; i++)
				{
					x += dxabs;
					if(x >= dyabs) { x -= dyabs; px += sdx; }
					py += sdy;
					if((mask & (1 << (i & 0x7))) != 0) plot();
				}
			}
			return set;
		}

		private static string Diff(bool[,] expected, bool[,] actual)
		{
			for(int y = 0; y < H; y++)
				for(int x = 0; x < W; x++)
					if(expected[x, y] != actual[x, y]) return "first difference at " + x + "," + y + " expected " + expected[x, y];
			return null;
		}

		private static int CountRow(bool[,] set, int row, int x1, int x2)
		{
			int n = 0;
			for(int x = x1; x <= x2; x++) if(set[x, row]) n++;
			return n;
		}

		private static int CountColumn(bool[,] set, int column)
		{
			int n = 0;
			for(int y = 0; y < H; y++) if(set[column, y]) n++;
			return n;
		}

		[Fact]
		public void ThicknessOneMatchesTheFrozenThinLine()
		{
			int[][] lines =
			{
				new[] { 5, 20, 50, 20 }, new[] { 50, 20, 5, 20 }, new[] { 30, 2, 30, 40 }, new[] { 30, 40, 30, 2 },
				new[] { 4, 4, 40, 40 }, new[] { 40, 4, 4, 40 }, new[] { 10, 2, 22, 45 }, new[] { 3, 30, 60, 11 },
				new[] { -20, -5, 70, 60 }, new[] { -10, 10, 80, 12 }, new[] { 63, 0, 0, 47 }, new[] { 7, 7, 7, 7 },
			};
			List<string> failures = new List<string>();
			foreach(uint mask in new uint[] { 0xffffffff, 0x0f, 0xaa })
			{
				foreach(int[] l in lines)
				{
					Plotter p = new Plotter(W, H, false);
					Line(p, l[0], l[1], l[2], l[3], 1, mask);
					string diff = Diff(FrozenThinLine(l[0], l[1], l[2], l[3], mask), Pixels(p));
					if(diff != null) failures.Add(string.Join(",", l) + " mask " + mask.ToString("x") + ": " + diff);
				}
			}
			Assert.True(failures.Count == 0, string.Join("; ", failures));
		}

		[Theory]
		[InlineData(3, -1, 1)]
		[InlineData(4, -1, 2)]
		[InlineData(5, -2, 2)]
		public void HorizontalAndVerticalLinesSpanTheThickness(int thickness, int lo, int hi)
		{
			Plotter p = new Plotter(W, H, false);
			Line(p, 10, 20, 50, 20, thickness);
			bool[,] set = Pixels(p);
			for(int y = 0; y < H; y++)
			{
				int expected = (y >= 20 + lo && y <= 20 + hi) ? 41 : 0;
				Assert.Equal(expected, CountRow(set, y, 0, W - 1));
			}

			p = new Plotter(W, H, false);
			Line(p, 30, 5, 30, 40, thickness);
			set = Pixels(p);
			for(int x = 0; x < W; x++)
				Assert.Equal((x >= 30 + lo && x <= 30 + hi) ? 36 : 0, CountColumn(set, x));
		}

		[Fact]
		public void DiagonalSpanIsTheThicknessAcrossTheLine()
		{
			// 45 degrees: a vertical span of round(3 * sqrt 2) = 4 per column, about 2.8 px perpendicular
			Plotter p = new Plotter(W, H, false);
			Line(p, 5, 5, 40, 40, 3);
			bool[,] set = Pixels(p);
			for(int x = 10; x <= 35; x++) Assert.Equal(4, CountColumn(set, x));
		}

		[Fact]
		public void DashMaskSkipsTheSameStepsAsTheThinLine()
		{
			Plotter thin = new Plotter(W, H, false);
			Plotter thick = new Plotter(W, H, false);
			Line(thin, 5, 20, 60, 20, 1, 0x0f);
			Line(thick, 5, 20, 60, 20, 3, 0x0f);
			bool[,] a = Pixels(thin), b = Pixels(thick);
			for(int x = 0; x < W; x++)
				Assert.Equal(a[x, 20] ? 3 : 0, CountColumn(b, x));
		}

		[Fact]
		public void ThickLinesAlongEveryEdgeClipToTheBuffer()
		{
			// Thickness 5 spans -2..2: an edge line keeps its 3 on-screen rows or columns
			Plotter p = new Plotter(W, H, false);
			Line(p, 0, 0, W - 1, 0, 5);
			Assert.Equal(3 * W, CountAll(Pixels(p)));
			p = new Plotter(W, H, false);
			Line(p, 0, H - 1, W - 1, H - 1, 5);
			Assert.Equal(3 * W, CountAll(Pixels(p)));
			p = new Plotter(W, H, false);
			Line(p, 0, 0, 0, H - 1, 5);
			Assert.Equal(3 * H, CountAll(Pixels(p)));
			p = new Plotter(W, H, false);
			Line(p, W - 1, 0, W - 1, H - 1, 5);
			Assert.Equal(3 * H, CountAll(Pixels(p)));
		}

		[Fact]
		public void HugeLinesDrawOnlyTheirVisiblePartQuickly()
		{
			// Endpoints as far out as the deepest zoom produces, crossing the buffer at slope 0.3
			int expected = (int)Math.Round(5 * Math.Sqrt(1 + 0.09));
			foreach(int reach in new[] { 5000000, 1000000000 })
			{
				Plotter p = new Plotter(W, H, false);
				Stopwatch watch = Stopwatch.StartNew();
				Line(p, 32 - reach, 24 - reach / 10 * 3, 32 + reach, 24 + reach / 10 * 3, 5);
				watch.Stop();
				// Walking all 2e9 steps would take seconds
				Assert.True(watch.ElapsedMilliseconds < 1000, "reach " + reach + " took " + watch.ElapsedMilliseconds + " ms");
				bool[,] set = Pixels(p);
				for(int x = 12; x <= 52; x++) Assert.Equal(expected, CountColumn(set, x));
			}

			// Fully off screen
			Plotter off = new Plotter(W, H, false);
			Line(off, -5000000, -100, 5000000, -90, 5);
			Assert.Equal(0, CountAll(Pixels(off)));
		}

		private static int CountAll(bool[,] set)
		{
			int n = 0;
			foreach(bool b in set) if(b) n++;
			return n;
		}
	}

	[Collection("GeneralStatics")]
	public class VertexDrawingTests
	{
		[Fact]
		public void VerticesAtTheEdgeDrawTheirVisiblePart()
		{
			FieldInfo settings = typeof(General).GetField("settings", BindingFlags.NonPublic | BindingFlags.Static);
			object previous = settings.GetValue(null);
			try
			{
				settings.SetValue(null, new ProgramConfiguration());
				PixelColor c = new PixelColor(255, 10, 20, 30), l = new PixelColor(255, 40, 50, 60), d = new PixelColor(255, 70, 80, 90);
				foreach(int[] v in new[] { new[] { 3, 20 }, new[] { 60, 20 }, new[] { 30, 2 }, new[] { 30, 45 }, new[] { 1, 1 }, new[] { -20, 20 }, new[] { 30, 24 } })
				{
					Plotter p = new Plotter(64, 48, false);
					p.DrawVertexSolid(v[0], p.Height - v[1], 10, ref c, ref l, ref d);
					for(int y = 0; y < 48; y++)
					{
						for(int x = 0; x < 64; x++)
						{
							bool inside = Math.Abs(x - v[0]) <= 10 && Math.Abs(y - v[1]) <= 10;
							Assert.True(inside == (p.GetPixel(x, y).ToInt() != 0), "vertex " + v[0] + "," + v[1] + " pixel " + x + "," + y);
						}
					}
				}
			}
			finally { settings.SetValue(null, previous); }
		}

		[Fact]
		public void VertexSizeKeepsTheOldValuesAtTheDefaults()
		{
			foreach(float zoom in new[] { 0.01f, 0.1f, 0.5f, 1f, 1.3f, 2f, 3f, 8f, 90f })
			{
				int old = Math.Min(4, Math.Max(0, (int)(1.7f * 1f * zoom + 0.5f)));
				Assert.Equal(old, Renderer2D.ComputeVertexSize(1f, zoom, 1));
			}
		}

		[Fact]
		public void VertexSizeCapFollowsTheScaleAndTheLineWidth()
		{
			Assert.True(Renderer2D.ComputeVertexSize(4f, 4f, 1) > 4);
			Assert.Equal(40, Renderer2D.ComputeVertexSize(10f, 90f, 1));
			Assert.True(Renderer2D.ComputeVertexSize(1f, 0.1f, 5) >= 2);
			foreach(float scale in new[] { 0.25f, 1f, 2.5f, 10f })
				foreach(float zoom in new[] { 0.01f, 0.5f, 1f, 4f, 90f })
					Assert.Equal(Renderer2D.ComputeVertexSize(scale, zoom, 0), Renderer2D.ComputeVertexSize(scale, zoom, 1));
		}
	}

	public class View2DSettingsTests
	{
		private static ProgramConfiguration Load(string body)
		{
			string path = Path.Combine(Path.GetTempPath(), "view2d-" + Guid.NewGuid().ToString("N") + ".cfg");
			try
			{
				File.WriteAllText(path, "currentversion = 1;\n" + body);
				ProgramConfiguration config = new ProgramConfiguration();
				Assert.True(config.Load(path, path, path));
				return config;
			}
			finally { File.Delete(path); }
		}

		[Theory]
		[InlineData(0, 1)]
		[InlineData(1, 1)]
		[InlineData(3, 3)]
		[InlineData(9, ProgramConfiguration.MAX_LINEDEF_THICKNESS)]
		public void LinedefThicknessClamps(int value, int expected)
		{
			Assert.Equal(expected, Load("linedefthickness = " + value + ";\n").LinedefThickness);
		}

		[Fact]
		public void MissingKeysKeepTodaysLook()
		{
			ProgramConfiguration config = Load("textlabelfontsize = 12;\n");
			Assert.Equal(1, config.LinedefThickness);
			Assert.Equal(100, config.TextLabelBackAlpha);
			foreach(TextLabelKind kind in Enum.GetValues(typeof(TextLabelKind)))
				Assert.Equal(12, config.GetTextLabelFontSize(kind));
		}

		[Fact]
		public void KindSizesOverrideTheGeneralSize()
		{
			ProgramConfiguration config = Load("textlabelfontsize = 12;\ntextlabelbackalpha = 150;\n"
				+ "textlabelsize { sectorlabels = 0; selectionnumbers = 8; measurements = 100; eventlabels = -3; }\n");
			Assert.Equal(12, config.GetTextLabelFontSize(TextLabelKind.General));
			Assert.Equal(12, config.GetTextLabelFontSize(TextLabelKind.SectorLabel));
			Assert.Equal(8, config.GetTextLabelFontSize(TextLabelKind.SelectionNumber));
			Assert.Equal(ProgramConfiguration.MAX_TEXT_LABEL_KIND_SIZE, config.GetTextLabelFontSize(TextLabelKind.Measurement));
			Assert.Equal(12, config.GetTextLabelFontSize(TextLabelKind.EventLabel));
			Assert.Equal(100, config.TextLabelBackAlpha);
			Assert.Equal(0, Load("textlabelbackalpha = -5;\n").TextLabelBackAlpha);
		}

		[Fact]
		public void LabelColoursSeedFromTheColoursTheyUsedToBorrow()
		{
			PixelColor eventlines = PixelColor.FromInt(0x123456 | unchecked((int)0xff000000));
			Assert.Equal(eventlines.ToInt(), ColorCollection.SeedLabelColor(0, eventlines).ToInt());
			int stored = 0x00ff00 | unchecked((int)0xff000000);
			Assert.Equal(stored, ColorCollection.SeedLabelColor(stored, eventlines).ToInt());
		}
	}
}
