using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Geometry;

namespace Builder.Tests
{
	public class FrameTimingTests
	{
		private sealed class Rig
		{
			internal double Time;
			internal readonly List<Dictionary<string, string>> Lines = new List<Dictionary<string, string>>();
			internal readonly FrameTimer Timer;

			internal Rig()
			{
				Timer = new FrameTimer(() => Time, Emit);
			}

			private void Emit(string ev, string[] fields)
			{
				Dictionary<string, string> d = new Dictionary<string, string> { { "ev", ev } };
				for(int i = 0; i + 1 < fields.Length; i += 2) d[fields[i]] = fields[i + 1];
				Lines.Add(d);
			}

			// One tick at the current time: 2 ms of cull, 3 ms of draw, 1 ms of present, then the period's rest idle
			internal void Tick(string mode, double period)
			{
				double start = Time;
				Timer.TickBegin(mode);
				Time += 2; Timer.Lap(FrameBucket.Cull);
				Time += 3; Timer.Lap(FrameBucket.Draw);
				Time += 1; Timer.Lap(FrameBucket.Present);
				Timer.TickEnd();
				Time = start + period;
			}
		}

		[Fact]
		public void PercentileIsNearestRank()
		{
			double[] v = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
			Assert.Equal(5.0, FrameTimer.Percentile(v, 0.5));
			Assert.Equal(10.0, FrameTimer.Percentile(v, 0.95));
			Assert.Equal(1.0, FrameTimer.Percentile(v, 0.0));
			Assert.Equal(7.0, FrameTimer.Percentile(new double[] { 7 }, 0.95));
			double[] collected = FrameTimer.Collect(new[] { 3.0, double.NaN, 1.0, 2.0, 99.0 }, 4);
			Assert.Equal(3, collected.Length);
			Assert.Equal(1.0, collected[0]);
			Assert.Equal(3.0, collected[2]);
		}

		[Fact]
		public void WindowReportsBucketsGapsAndIdle()
		{
			Rig r = new Rig();
			for(int i = 0; i < 100; i++) r.Tick("VisualMode", 16);

			Dictionary<string, string> first = r.Lines[0];
			Assert.Equal("frames", first["ev"]);
			Assert.Equal("VisualMode", first["mode"]);
			Assert.Equal("62.5", first["fps"]);
			Assert.Equal("16.0", first["gap_p50"]);
			Assert.Equal("6.0", first["busy_p50"]);
			Assert.Equal("10.0", first["idle_p95"]);
			Assert.Equal("2.0", first["cull_p50"]);
			Assert.Equal("3.0", first["draw_p95"]);
			Assert.Equal("1.0", first["present_max"]);
			Assert.Equal("0.0", first["pick_max"]);
			Assert.Equal("0", first["picks"]);
			Assert.True(int.Parse(first["n"]) >= 62 && int.Parse(first["n"]) <= 64, first["n"]);
		}

		[Fact]
		public void ChainedTicksAreCountedPerWindow()
		{
			Rig r = new Rig();
			for(int i = 0; i < 10; i++)
			{
				double start = r.Time;
				r.Timer.TickBegin("VisualMode");
				if(i % 2 == 0) r.Timer.NoteChained();
				r.Timer.TickEnd();
				r.Timer.NoteChainIdle(i < 8 ? ChainIdle.Paints : ChainIdle.Unwanted, i < 9);
				r.Time = start + 150;
			}
			r.Timer.TickBegin("ClassicMode");
			Assert.Equal(2, r.Lines.Count);
			int n0 = int.Parse(r.Lines[0]["n"]);
			Assert.Equal((n0 + 1) / 2, int.Parse(r.Lines[0]["chained"]));
			Assert.Equal(5 - (n0 + 1) / 2, int.Parse(r.Lines[1]["chained"]));
			Assert.Equal(8, int.Parse(r.Lines[0]["blk_paints"]) + int.Parse(r.Lines[1]["blk_paints"]));
			Assert.Equal(10, int.Parse(r.Lines[0]["idle_passes"]) + int.Parse(r.Lines[1]["idle_passes"]));
			Assert.Equal("1", r.Lines[1]["blk_other"]);
			Assert.Equal("1", r.Lines[1]["blk_inactive"]);
		}

		[Fact]
		public void PauseAndModeChangeStartNewWindows()
		{
			Rig r = new Rig();
			r.Tick("VisualMode", 16);
			r.Tick("VisualMode", 5000);
			Assert.Equal(0, r.Lines.Count);

			// The tick after a long pause flushes the old window and has no gap of its own
			r.Tick("VisualMode", 16);
			Assert.Equal(1, r.Lines.Count);
			Assert.Equal("2", r.Lines[0]["n"]);
			Assert.Equal("16.0", r.Lines[0]["gap_max"]);

			r.Tick("GZVisualMode", 16);
			Assert.Equal(2, r.Lines.Count);
			Assert.Equal("1", r.Lines[1]["n"]);
			Assert.Equal("-", r.Lines[1]["fps"]);
			Assert.Equal("-", r.Lines[1]["gap_p50"]);
		}

		[Fact]
		public void LongTickIsAStallNotAPause()
		{
			Rig r = new Rig();
			r.Tick("VisualMode", 16);
			r.Timer.TickBegin("VisualMode");
			r.Time += 1500;
			r.Timer.TickEnd();
			r.Time += 15;
			for(int i = 0; i < 80; i++) r.Tick("VisualMode", 16);

			// The stall tick ends the first window; the next window keeps its 1515 ms gap
			Assert.Equal("2", r.Lines[0]["n"]);
			Assert.Equal("1500.0", r.Lines[0]["busy_max"]);
			Assert.Equal("1515.0", r.Lines[1]["gap_max"]);
			Assert.True(r.Lines[1]["fps"] != "-", r.Lines[1]["fps"]);
		}

		[Fact]
		public void ModeChangeDropsCameraContinuity()
		{
			Rig r = new Rig();
			r.Timer.TickBegin("VisualMode");
			r.Timer.NoteCamera(new Vector3D(0, 0, 0), 0, 0);
			r.Timer.TickEnd();
			r.Time += 16;
			r.Tick("VerticesMode", 16);
			r.Timer.TickBegin("VisualMode");
			r.Timer.NoteCamera(new Vector3D(5000, 0, 0), 0, 0);
			r.Timer.TickEnd();
			r.Time += 16;
			r.Tick("VerticesMode", 16);

			Assert.Equal("0.0", r.Lines[0]["cam_dist"]);
			Assert.False(r.Lines[1].ContainsKey("cam_dist"));
			Assert.Equal("0.0", r.Lines[2]["cam_dist"]);
		}

		[Fact]
		public void CameraTravelAndVisibleCountsArePerWindow()
		{
			Rig r = new Rig();
			for(int i = 0; i < 70; i++)
			{
				double start = r.Time;
				r.Timer.TickBegin("VisualMode");
				r.Timer.NoteCamera(new Vector3D(i * 2.0, 0, 0), i * Angle2D.DegToRad(1.0), 0);
				r.Timer.NoteVisible(100 + i, 5);
				r.Timer.NoteLights(12, 300 - i, 40 + i);
				r.Timer.TickEnd();
				r.Time = start + 16;
			}
			Dictionary<string, string> first = r.Lines[0];
			int n = int.Parse(first["n"]);
			Assert.Equal(((n - 1) * 2.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), first["cam_dist"]);
			Assert.Equal((n - 1.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), first["cam_turn_deg"]);
			Assert.Equal((100 + n - 1).ToString(), first["vis_geo"]);
			Assert.Equal("5", first["vis_things"]);
			Assert.Equal("12", first["dyn_lights"]);
			Assert.Equal("300", first["lit_surfaces"]);
			Assert.Equal((40 + n - 1).ToString(), first["light_uploads"]);
		}

		[Fact]
		public void LightCountsAppearOnlyInWindowsThatNotedThem()
		{
			Rig r = new Rig();
			for(int i = 0; i < 140; i++)
			{
				double start = r.Time;
				r.Timer.TickBegin("VisualMode");
				if(i < 30) r.Timer.NoteLights(8, 100, 10); // the second window starts near tick 63
				r.Timer.TickEnd();
				r.Time = start + 16;
			}
			Assert.True(r.Lines[0].ContainsKey("dyn_lights"));
			Assert.False(r.Lines[1].ContainsKey("dyn_lights"));
		}

		[Fact]
		public void DrawCountersArePerWindowMaximaWithRunFractionAndNativeTime()
		{
			Rig r = new Rig();
			for(int i = 0; i < 200; i++)
			{
				double start = r.Time;
				r.Timer.TickBegin("VisualMode");
				// Windows start near ticks 63 and 126: smaller counts in the second, none in the third
				if(i >= 70 && i < 100) r.Timer.NoteDraws(50, 40, new long[] { 100, 100, 1000, 200, 5, -1 });
				if(i < 30)
				{
					// Draw time unmeasured for the first ten ticks, 2 ms after; tick 10 notes twice, which adds up
					long ns = i < 10 ? -1 : 2000000;
					r.Timer.NoteDraws(100, i == 0 ? 40 : 20, new long[] { 300 + i, 300 + i, 5000, 900, 30, ns });
					if(i == 10) r.Timer.NoteDraws(0, 0, new long[] { 0, 0, 0, 0, 0, ns });
				}
				r.Timer.TickEnd();
				r.Time = start + 16;
			}
			Dictionary<string, string> first = r.Lines[0];
			Assert.Equal("329", first["draws"]);
			Assert.Equal("329", first["drawn_surfaces"]);
			Assert.Equal("5000", first["uni_calls"]);
			Assert.Equal("900", first["uni_uploads"]);
			Assert.Equal("30", first["shader_binds"]);
			Assert.Equal("100", first["geo_surfaces"]);
			Assert.Equal("40", first["runs"]);
			// From sums, not maxima: 620 runs over 3000 surfaces
			Assert.Equal("20.7", first["runs_pct"]);
			Assert.Equal("2.0", first["native_p50"]);
			Assert.Equal("4.0", first["native_max"]);

			// A window with no draw notes has none of the fields
			// Each window reports only its own ticks
			Assert.Equal("100", r.Lines[1]["draws"]);
			Assert.Equal("5", r.Lines[1]["shader_binds"]);
			Assert.Equal("50", r.Lines[1]["geo_surfaces"]);
			Assert.Equal("40", r.Lines[1]["runs"]);
			Assert.Equal("80.0", r.Lines[1]["runs_pct"]);
			Assert.False(r.Lines[1].ContainsKey("native_p50"));
			Assert.False(r.Lines[2].ContainsKey("draws"));
		}

		[Fact]
		public void GLFieldsFillMissingParts()
		{
			string[] f = InteractionLog.GLFields("AMD\tAMD Radeon Graphics (gfx1100)\t4.6 Mesa 22.3.6\t1");
			Assert.Equal("renderer", f[2]);
			Assert.Equal("AMD Radeon Graphics (gfx1100)", f[3]);
			Assert.Equal("1", f[7]);
			string[] empty = InteractionLog.GLFields("");
			Assert.Equal(8, empty.Length);
			Assert.Equal("?", empty[1]);
			Assert.Equal("?", empty[7]);
		}
	}
}
