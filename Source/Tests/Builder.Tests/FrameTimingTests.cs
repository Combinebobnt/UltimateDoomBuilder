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
		public void CameraTravelAndVisibleCountsArePerWindow()
		{
			Rig r = new Rig();
			for(int i = 0; i < 70; i++)
			{
				double start = r.Time;
				r.Timer.TickBegin("VisualMode");
				r.Timer.NoteCamera(new Vector3D(i * 2.0, 0, 0), i * Angle2D.DegToRad(1.0), 0);
				r.Timer.NoteVisible(100 + i, 5);
				r.Timer.TickEnd();
				r.Time = start + 16;
			}
			Dictionary<string, string> first = r.Lines[0];
			int n = int.Parse(first["n"]);
			Assert.Equal(((n - 1) * 2.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), first["cam_dist"]);
			Assert.Equal((n - 1.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), first["cam_turn_deg"]);
			Assert.Equal((100 + n - 1).ToString(), first["vis_geo"]);
			Assert.Equal("5", first["vis_things"]);
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
