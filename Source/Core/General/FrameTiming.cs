#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder
{
	// Where a processor tick's time goes; each lap charges the time since the previous lap to one bucket
	public enum FrameBucket
	{
		Input,
		Move,
		Cull,
		Start,
		Gather,
		Lights,
		Geometry,
		Things,
		Models,
		Translucent,
		Draw,
		Present,
		Ui,
		Post,
		Pick,
		Info,
	}

	// Per-tick timing of the main processor loop, written to the interaction log once a second as a "frames" line.
	// Inert unless the interaction log is on; probes outside a tick do nothing.
	public static class FrameTiming
	{
		private static FrameTimer timer;
		private static bool intick;

		internal static void TickBegin(string mode)
		{
			if(!InteractionLog.Enabled) { intick = false; return; }
			if(timer == null) timer = new FrameTimer(() => InteractionLog.Now, InteractionLog.Write);
			timer.TickBegin(mode);
			intick = true;
		}

		public static void Lap(FrameBucket bucket)
		{
			if(intick) timer.Lap(bucket);
		}

		internal static void TickEnd()
		{
			if(!intick) return;
			intick = false;
			timer.TickEnd();
		}

		internal static void NoteCamera(Vector3D position, double anglexy, double anglez)
		{
			if(intick) timer.NoteCamera(position, anglexy, anglez);
		}

		internal static void NoteVisible(int geometry, int things)
		{
			if(intick) timer.NoteVisible(geometry, things);
		}

		internal static void NoteChained()
		{
			if(intick) timer.NoteChained();
		}

		internal static void NoteLights(int lights, int litsurfaces, int uploads)
		{
			if(intick) timer.NoteLights(lights, litsurfaces, uploads);
		}

		// True while a logged tick runs, so callers can skip work that only feeds the log
		internal static bool Counting { get { return intick; } }

		internal static void NoteDraws(int geosurfaces, int runs, long[] counters)
		{
			if(intick) timer.NoteDraws(geosurfaces, runs, counters);
		}

		// Counted between ticks too; passes during a pause are dropped when the next tick starts a new window
		internal static void NoteChainIdle(ChainIdle result, bool windowactive)
		{
			if(timer != null && InteractionLog.Enabled) timer.NoteChainIdle(result, windowactive);
		}
	}

	// The accumulator behind FrameTiming, with an injectable clock and sink so tests can drive it
	internal sealed class FrameTimer
	{
		internal const double WINDOW_MS = 1000.0;
		internal const double BREAK_MS = 1000.0; // a longer pause between ticks starts a new window
		internal const int CAPACITY = 512;
		private static readonly int BUCKETS = Enum.GetValues(typeof(FrameBucket)).Length;
		private static readonly string[] BUCKETNAMES = BuildBucketNames();

		private readonly Func<double> clock;
		private readonly Action<string, string[]> emit;

		// Per tick in the current window
		private readonly double[][] buckets;
		private readonly double[] gaps = new double[CAPACITY];
		private readonly double[] idles = new double[CAPACITY];
		private readonly double[] busy = new double[CAPACITY];
		private readonly double[] nativems = new double[CAPACITY];
		private int count;
		private int gapcount;
		private int picks;
		private int chained;
		private int idlepasses, blockpaints, blockinactive, blockother;
		private string mode;
		private double windowstart;

		// Current tick
		private double tickstart = -1;
		private double lastlap;
		private double prevtickstart = -1;
		private double prevtickend = -1;
		private readonly double[] current;
		private double currentnative;

		// Camera and culling, per window
		private bool hascamera;
		private bool windowcamera;
		private Vector3D lastpos;
		private double lastanglexy, lastanglez;
		private double distance, turn;
		private int maxgeometry = -1, maxthings = -1;
		private int maxlights = -1, maxlit = -1, maxuploads = -1;

		// Draw counters, per window: maxima, plus sums for the run fraction
		private static readonly string[] COUNTERNAMES = { "draws", "drawn_surfaces", "uni_calls", "uni_uploads", "shader_binds" };
		private readonly long[] maxcounters = new long[COUNTERNAMES.Length];
		private bool hasdraws;
		private int maxgeosurfaces, maxruns;
		private long sumgeosurfaces, sumruns;

		internal FrameTimer(Func<double> clock, Action<string, string[]> emit)
		{
			this.clock = clock;
			this.emit = emit;
			buckets = new double[BUCKETS][];
			for(int i = 0; i < BUCKETS; i++) buckets[i] = new double[CAPACITY];
			current = new double[BUCKETS];
		}

		private static string[] BuildBucketNames()
		{
			string[] names = Enum.GetNames(typeof(FrameBucket));
			for(int i = 0; i < names.Length; i++) names[i] = names[i].ToLowerInvariant();
			return names;
		}

		internal void TickBegin(string tickmode)
		{
			double now = clock();
			// Measured from the previous tick's end, so one long tick is a stall in the data, not a pause
			bool broken = prevtickend < 0 || now - prevtickend > BREAK_MS;
			if(tickmode != mode) hascamera = false;
			if(broken) idlepasses = blockpaints = blockinactive = blockother = 0;
			if(count > 0 && (broken || tickmode != mode)) Flush(prevtickend);
			if(count == 0)
			{
				mode = tickmode;
				windowstart = now;
			}
			if(!broken)
			{
				gaps[count] = now - prevtickstart;
				idles[count] = Math.Max(0.0, now - prevtickend);
				gapcount++;
			}
			else
			{
				gaps[count] = double.NaN;
				idles[count] = double.NaN;
				hascamera = false;
			}
			tickstart = now;
			lastlap = now;
			Array.Clear(current, 0, BUCKETS);
			currentnative = double.NaN;
		}

		internal void Lap(FrameBucket bucket)
		{
			double now = clock();
			current[(int)bucket] += now - lastlap;
			lastlap = now;
		}

		internal void TickEnd()
		{
			Lap(FrameBucket.Post);
			double now = lastlap;
			for(int i = 0; i < BUCKETS; i++) buckets[i][count] = current[i];
			nativems[count] = currentnative;
			if(current[(int)FrameBucket.Pick] > 0) picks++;
			busy[count] = now - tickstart;
			count++;
			prevtickstart = tickstart;
			prevtickend = now;
			if(count >= CAPACITY || now - windowstart >= WINDOW_MS) Flush(now);
		}

		internal void NoteCamera(Vector3D position, double anglexy, double anglez)
		{
			if(hascamera)
			{
				distance += (position - lastpos).GetLength();
				turn += Math.Abs(Angle2D.Difference(anglexy, lastanglexy)) + Math.Abs(anglez - lastanglez);
			}
			hascamera = true;
			windowcamera = true;
			lastpos = position;
			lastanglexy = anglexy;
			lastanglez = anglez;
		}

		internal void NoteChained()
		{
			chained++;
		}

		internal void NoteChainIdle(ChainIdle result, bool windowactive)
		{
			idlepasses++;
			if(result == ChainIdle.Paints) blockpaints++;
			else if(result == ChainIdle.Unwanted && !windowactive) blockinactive++;
			else if(result == ChainIdle.Unwanted) blockother++;
		}

		internal void NoteVisible(int geometry, int things)
		{
			maxgeometry = Math.Max(maxgeometry, geometry);
			maxthings = Math.Max(maxthings, things);
		}

		internal void NoteLights(int lights, int litsurfaces, int uploads)
		{
			maxlights = Math.Max(maxlights, lights);
			maxlit = Math.Max(maxlit, litsurfaces);
			maxuploads = Math.Max(maxuploads, uploads);
		}

		// counters is indexed by DrawCounter; a negative draw time means it wasn't measured
		internal void NoteDraws(int geosurfaces, int runs, long[] counters)
		{
			hasdraws = true;
			for(int i = 0; i < COUNTERNAMES.Length; i++) maxcounters[i] = Math.Max(maxcounters[i], counters[i]);
			maxgeosurfaces = Math.Max(maxgeosurfaces, geosurfaces);
			maxruns = Math.Max(maxruns, runs);
			sumgeosurfaces += geosurfaces;
			sumruns += runs;
			long ns = counters[(int)Rendering.DrawCounter.DrawNanoseconds];
			if(ns >= 0) currentnative = (double.IsNaN(currentnative) ? 0.0 : currentnative) + ns / 1e6;
		}

		private void Flush(double now)
		{
			double gapsum = 0;
			for(int i = 0; i < count; i++) if(!double.IsNaN(gaps[i])) gapsum += gaps[i];

			List<string> f = new List<string>(8 + (BUCKETS + 3) * 6);
			f.Add("mode"); f.Add(mode);
			f.Add("n"); f.Add(Num(count));
			f.Add("ms"); f.Add(Ms(now - windowstart));
			f.Add("fps"); f.Add(gapcount > 0 && gapsum > 0 ? (gapcount * 1000.0 / gapsum).ToString("0.0", CultureInfo.InvariantCulture) : "-");
			AddStats(f, "gap", gaps);
			AddStats(f, "busy", busy);
			AddStats(f, "idle", idles);
			for(int i = 0; i < BUCKETS; i++) AddStats(f, BUCKETNAMES[i], buckets[i]);
			f.Add("picks"); f.Add(Num(picks));
			f.Add("chained"); f.Add(Num(chained));
			f.Add("idle_passes"); f.Add(Num(idlepasses));
			f.Add("blk_paints"); f.Add(Num(blockpaints));
			f.Add("blk_inactive"); f.Add(Num(blockinactive));
			f.Add("blk_other"); f.Add(Num(blockother));
			if(maxgeometry >= 0)
			{
				f.Add("vis_geo"); f.Add(Num(maxgeometry));
				f.Add("vis_things"); f.Add(Num(maxthings));
			}
			if(maxlights >= 0)
			{
				f.Add("dyn_lights"); f.Add(Num(maxlights));
				f.Add("lit_surfaces"); f.Add(Num(maxlit));
				f.Add("light_uploads"); f.Add(Num(maxuploads));
			}
			if(hasdraws)
			{
				for(int i = 0; i < COUNTERNAMES.Length; i++) { f.Add(COUNTERNAMES[i]); f.Add(maxcounters[i].ToString(CultureInfo.InvariantCulture)); }
				f.Add("geo_surfaces"); f.Add(Num(maxgeosurfaces));
				f.Add("runs"); f.Add(Num(maxruns));
				f.Add("runs_pct"); f.Add(sumgeosurfaces > 0 ? (100.0 * sumruns / sumgeosurfaces).ToString("0.0", CultureInfo.InvariantCulture) : "-");
			}
			if(Collect(nativems, count).Length > 0) AddStats(f, "native", nativems);
			if(windowcamera)
			{
				f.Add("cam_dist"); f.Add(distance.ToString("0.0", CultureInfo.InvariantCulture));
				f.Add("cam_turn_deg"); f.Add(Angle2D.RadToDeg(turn).ToString("0.0", CultureInfo.InvariantCulture));
			}
			emit("frames", f.ToArray());

			count = 0;
			gapcount = 0;
			picks = 0;
			chained = 0;
			idlepasses = blockpaints = blockinactive = blockother = 0;
			distance = 0;
			turn = 0;
			windowcamera = false;
			maxgeometry = -1;
			maxthings = -1;
			maxlights = maxlit = maxuploads = -1;
			hasdraws = false;
			Array.Clear(maxcounters, 0, maxcounters.Length);
			maxgeosurfaces = maxruns = 0;
			sumgeosurfaces = sumruns = 0;
		}

		private void AddStats(List<string> f, string name, double[] values)
		{
			double[] sorted = Collect(values, count);
			f.Add(name + "_p50"); f.Add(sorted.Length > 0 ? Ms(Percentile(sorted, 0.5)) : "-");
			f.Add(name + "_p95"); f.Add(sorted.Length > 0 ? Ms(Percentile(sorted, 0.95)) : "-");
			f.Add(name + "_max"); f.Add(sorted.Length > 0 ? Ms(sorted[sorted.Length - 1]) : "-");
		}

		// The first n values without NaNs, sorted ascending
		internal static double[] Collect(double[] values, int n)
		{
			List<double> kept = new List<double>(n);
			for(int i = 0; i < n; i++) if(!double.IsNaN(values[i])) kept.Add(values[i]);
			double[] result = kept.ToArray();
			Array.Sort(result);
			return result;
		}

		// Nearest-rank percentile of an ascending, non-empty array
		internal static double Percentile(double[] sorted, double p)
		{
			int rank = (int)Math.Ceiling(p * sorted.Length);
			return sorted[Math.Min(sorted.Length, Math.Max(1, rank)) - 1];
		}

		private static string Ms(double ms) { return InteractionLog.Ms(ms); }
		private static string Num(int n) { return n.ToString(CultureInfo.InvariantCulture); }
	}
}
