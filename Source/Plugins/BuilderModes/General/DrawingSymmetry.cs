#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	// Replicates a drawn path across symmetry axes and draws the copies in one undo step
	public static class DrawingSymmetry
	{
		#region ================== Constants

		// Same tolerance DrawPointAt uses for coincident points
		public const double EPSILON = 0.001;

		#endregion

		#region ================== Mirror

		// Mirrors path across the vertical (mirrorx) and/or horizontal (mirrory) line through pivot.
		// Off-axis pieces (each merged with its mirror image where it touches the axis) come before on-axis runs.
		public static List<List<DrawnVertex>> Mirror(IList<DrawnVertex> path, Vector2D pivot, bool mirrorx, bool mirrory)
		{
			List<List<DrawnVertex>> result = new List<List<DrawnVertex>> { Deduped(path) };
			if(mirrorx) result = MirrorAll(result, pivot, true);
			if(mirrory) result = MirrorAll(result, pivot, false);
			return result;
		}

		private static List<List<DrawnVertex>> MirrorAll(List<List<DrawnVertex>> paths, Vector2D pivot, bool vertical)
		{
			List<List<DrawnVertex>> result = new List<List<DrawnVertex>>();
			foreach(List<DrawnVertex> p in paths) result.AddRange(MirrorOne(p, pivot, vertical));
			return result;
		}

		private static List<List<DrawnVertex>> MirrorOne(List<DrawnVertex> p, Vector2D pivot, bool vertical)
		{
			List<List<DrawnVertex>> result = new List<List<DrawnVertex>>();
			int n = p.Count;
			bool[] on = new bool[n];
			bool allon = true;
			for(int i = 0; i < n; i++)
			{
				on[i] = Math.Abs(Offset(p[i].pos, pivot, vertical)) <= EPSILON;
				allon &= on[i];
			}

			if(n < 2 || allon || Coincides(p, pivot, vertical))
			{
				result.Add(p);
				return result;
			}

			if(Same(p[0].pos, p[n - 1].pos))
			{
				// Closed: the original and its reversed mirror image
				result.Add(p);
				result.Add(ReversedMirror(p, pivot, vertical));
				return result;
			}

			// Split at on-axis runs of 2+ points: each piece merges with its mirror on its own and
			// each run is drawn once, since a merged path would retrace it
			List<List<DrawnVertex>> runs = new List<List<DrawnVertex>>();
			int start = 0;
			for(int i = 0; i + 1 < n; i++)
			{
				if(!on[i] || !on[i + 1]) continue;
				int j = i + 1;
				while(j + 1 < n && on[j + 1]) j++;
				if(i > start) result.AddRange(MirrorPiece(p.GetRange(start, i - start + 1), pivot, vertical));
				runs.Add(p.GetRange(i, j - i + 1));
				start = j;
				i = j;
			}
			if(start < n - 1) result.AddRange(MirrorPiece(p.GetRange(start, n - start), pivot, vertical));

			result.AddRange(runs);
			return result;
		}

		// Open piece with no on-axis segment, merged with its mirror when an end touches the axis
		private static List<List<DrawnVertex>> MirrorPiece(List<DrawnVertex> core, Vector2D pivot, bool vertical)
		{
			int k = core.Count;
			bool firston = Math.Abs(Offset(core[0].pos, pivot, vertical)) <= EPSILON;
			bool laston = Math.Abs(Offset(core[k - 1].pos, pivot, vertical)) <= EPSILON;
			if((!firston && !laston) || Crosses(core, 0, k - 1, pivot, vertical))
				return new List<List<DrawnVertex>> { core, ReversedMirror(core, pivot, vertical) };

			List<DrawnVertex> merged = new List<DrawnVertex>();
			if(firston && laston)
			{
				// Both ends on the axis: one closed loop
				merged.AddRange(core);
				for(int i = k - 2; i >= 1; i--) merged.Add(Mirrored(core[i], pivot, vertical));
				merged.Add(core[0]);
			}
			else if(laston)
			{
				// Only the last point on the axis
				merged.AddRange(core);
				for(int i = k - 2; i >= 0; i--) merged.Add(Mirrored(core[i], pivot, vertical));
			}
			else
			{
				// Only the first point on the axis
				for(int i = k - 1; i >= 1; i--) merged.Add(Mirrored(core[i], pivot, vertical));
				merged.AddRange(core);
			}
			return new List<List<DrawnVertex>> { Deduped(merged) };
		}

		// True when the off-axis points of p[first..last] lie on both sides of the axis
		private static bool Crosses(List<DrawnVertex> p, int first, int last, Vector2D pivot, bool vertical)
		{
			bool below = false, above = false;
			for(int i = first; i <= last; i++)
			{
				double d = Offset(p[i].pos, pivot, vertical);
				if(d < -EPSILON) below = true;
				else if(d > EPSILON) above = true;
			}
			return below && above;
		}

		// True when the reversed mirror image draws the same lines as p
		private static bool Coincides(List<DrawnVertex> p, Vector2D pivot, bool vertical)
		{
			List<DrawnVertex> r = ReversedMirror(p, pivot, vertical);
			int n = p.Count;
			if(!Same(p[0].pos, p[n - 1].pos))
			{
				for(int i = 0; i < n; i++) if(!Same(p[i].pos, r[i].pos)) return false;
				return true;
			}

			// Closed: compare the unique vertices up to a cyclic shift
			int k = n - 1;
			for(int s = 0; s < k; s++)
			{
				bool match = true;
				for(int i = 0; i < k && match; i++) match = Same(p[i].pos, r[(i + s) % k].pos);
				if(match) return true;
			}
			return false;
		}

		private static List<DrawnVertex> ReversedMirror(List<DrawnVertex> p, Vector2D pivot, bool vertical)
		{
			List<DrawnVertex> r = new List<DrawnVertex>(p.Count);
			for(int i = p.Count - 1; i >= 0; i--) r.Add(Mirrored(p[i], pivot, vertical));
			return r;
		}

		private static DrawnVertex Mirrored(DrawnVertex v, Vector2D pivot, bool vertical)
		{
			DrawnVertex m = v;
			m.pos = PivotMath.MirroredAcross(v.pos, pivot, vertical);
			return m;
		}

		private static double Offset(Vector2D pos, Vector2D pivot, bool vertical)
		{
			return vertical ? pos.x - pivot.x : pos.y - pivot.y;
		}

		private static bool Same(Vector2D a, Vector2D b)
		{
			return Math.Abs(a.x - b.x) <= EPSILON && Math.Abs(a.y - b.y) <= EPSILON;
		}

		private static List<DrawnVertex> Deduped(IList<DrawnVertex> path)
		{
			List<DrawnVertex> result = new List<DrawnVertex>(path.Count);
			foreach(DrawnVertex v in path)
				if(result.Count == 0 || !Same(result[result.Count - 1].pos, v.pos)) result.Add(v);
			return result;
		}

		#endregion

		#region ================== Radial

		public static List<List<DrawnVertex>> Radial(IList<DrawnVertex> path, Vector2D pivot, int count, int decimals, out int skipped)
		{
			bool originalkept;
			return Radial(path, pivot, count, decimals, out skipped, out originalkept);
		}

		// Path and count-1 CCW copies about pivot, clustered then rounded so seams match. Paths that
		// collapse or repeat an earlier path's lines are skipped; originalkept says if path 0 survived.
		public static List<List<DrawnVertex>> Radial(IList<DrawnVertex> path, Vector2D pivot, int count, int decimals, out int skipped, out bool originalkept)
		{
			skipped = 0;
			originalkept = true;
			if(path.Count < 2 || count < 2) return new List<List<DrawnVertex>> { new List<DrawnVertex>(path) };

			List<List<DrawnVertex>> copies = new List<List<DrawnVertex>>(count);
			for(int k = 0; k < count; k++)
			{
				List<DrawnVertex> c = new List<DrawnVertex>(path.Count);
				foreach(DrawnVertex v in path)
				{
					DrawnVertex r = v;
					if(k > 0) r.pos = RadialCopy(v.pos, pivot, k, count);
					c.Add(r);
				}
				copies.Add(c);
			}

			// Cluster to the first position seen, then round that one value
			StitchClusters clusters = new StitchClusters(MapSet.STITCH_DISTANCE);
			foreach(List<DrawnVertex> c in copies)
			{
				for(int i = 0; i < c.Count; i++)
				{
					DrawnVertex v = c[i];
					Vector2D canon = clusters.Canonical(v.pos);
					v.pos = new Vector2D(Math.Round(canon.x, decimals), Math.Round(canon.y, decimals));
					c[i] = v;
				}
			}

			List<List<DrawnVertex>> result = new List<List<DrawnVertex>>(count);
			List<HashSet<Segment>> keptsegments = new List<HashSet<Segment>>(count);
			foreach(List<DrawnVertex> c in copies)
			{
				List<DrawnVertex> d = new List<DrawnVertex>(c.Count);
				foreach(DrawnVertex v in c)
					if(d.Count == 0 || d[d.Count - 1].pos != v.pos) d.Add(v);
				if(d.Count < 2)
				{
					if(c == copies[0]) originalkept = false;
					skipped++;
					continue;
				}

				HashSet<Segment> segments = new HashSet<Segment>();
				for(int i = 1; i < d.Count; i++) segments.Add(new Segment(d[i - 1].pos, d[i].pos));
				bool repeat = false;
				foreach(HashSet<Segment> s in keptsegments)
					if(s.SetEquals(segments)) { repeat = true; break; }
				if(repeat)
				{
					skipped++;
					continue;
				}

				keptsegments.Add(segments);
				result.Add(d);
			}

			return result;
		}

		// Copy k of count, unrounded. Snapping uses this too, so a snapped point equals the copy exactly.
		public static Vector2D RadialCopy(Vector2D p, Vector2D pivot, int k, int count)
		{
			return PivotMath.RotatedAbout(p, pivot, k * Angle2D.PI2 / count);
		}

		// Unordered pair of exact positions
		private struct Segment : IEquatable<Segment>
		{
			private readonly Vector2D a, b;

			public Segment(Vector2D p, Vector2D q)
			{
				bool swap = p.x > q.x || (p.x == q.x && p.y > q.y);
				a = swap ? q : p;
				b = swap ? p : q;
			}

			public bool Equals(Segment o) { return a == o.a && b == o.b; }
			public override bool Equals(object obj) { return obj is Segment && Equals((Segment)obj); }
			// Adding 0.0 turns -0 into +0, which == treats as equal
			public override int GetHashCode()
			{
				return (a.x + 0.0).GetHashCode() ^ ((a.y + 0.0).GetHashCode() * 31) ^ ((b.x + 0.0).GetHashCode() * 397) ^ ((b.y + 0.0).GetHashCode() * 7919);
			}
		}

		// First-seen positions in a hash grid, so clustering stays linear in the point count
		private class StitchClusters
		{
			private readonly double range;
			private readonly Dictionary<long, List<Vector2D>> cells = new Dictionary<long, List<Vector2D>>();

			public StitchClusters(double range) { this.range = range; }

			public Vector2D Canonical(Vector2D p)
			{
				long cx = (long)Math.Floor(p.x / range), cy = (long)Math.Floor(p.y / range);
				for(long x = cx - 1; x <= cx + 1; x++)
				{
					for(long y = cy - 1; y <= cy + 1; y++)
					{
						List<Vector2D> cell;
						if(!cells.TryGetValue(Key(x, y), out cell)) continue;
						foreach(Vector2D c in cell)
							if(Vector2D.DistanceSq(c, p) <= range * range) return c;
					}
				}

				List<Vector2D> home;
				if(!cells.TryGetValue(Key(cx, cy), out home)) cells[Key(cx, cy)] = home = new List<Vector2D>();
				home.Add(p);
				return p;
			}

			private static long Key(long x, long y) { return (x << 32) ^ (y & 0xFFFFFFFFL); }
		}

		#endregion

		#region ================== Bounds

		public static bool AnyOutside(List<List<DrawnVertex>> paths, double left, double right, double top, double bottom)
		{
			foreach(List<DrawnVertex> p in paths)
				foreach(DrawnVertex v in p)
					if(v.pos.x < left || v.pos.x > right || v.pos.y > top || v.pos.y < bottom) return true;
			return false;
		}

		#endregion

		#region ================== Drawing

		// Draws each path as if accepted by hand in turn. The caller creates the undo and runs the
		// once-only steps; a failed draw withdraws the undo (restoring the whole map) and returns false.
		public static bool DrawPaths(List<List<DrawnVertex>> paths, bool autoalign, out List<Sector> newsectors)
		{
			newsectors = new List<Sector>();
			HashSet<Sector> oldsectors = new HashSet<Sector>(General.Map.Map.Sectors);

			for(int i = 0; i < paths.Count; i++)
			{
				// Lines bounding the sectors earlier paths created, to recognize pieces split off them
				HashSet<Linedef> earlierlines = new HashSet<Linedef>();
				foreach(Sector s in newsectors)
					if(!s.IsDisposed) foreach(Sidedef sd in s.Sidedefs) earlierlines.Add(sd.Line);

				if(!Tools.DrawLines(paths[i], true, autoalign))
				{
					General.Map.UndoRedo.WithdrawUndo();
					newsectors.Clear();
					return false;
				}

				General.Map.Map.SnapAllToAccuracy();
				General.Map.Map.ClearAllSelected();
				General.Map.Map.Update();
				if(General.Settings.SplitJoinedSectors) Tools.SplitOuterSectors(General.Map.Map.GetMarkedLinedefs(true));
				newsectors.AddRange(General.Map.Map.GetMarkedSectors(true));

				// A later path can split an earlier path's sector; those pieces are never marked.
				// Pieces of rooms that existed before the draw stay out, as when drawing by hand.
				if(earlierlines.Count > 0)
				{
					foreach(Linedef l in General.Map.Map.GetMarkedLinedefs(true))
					{
						if(l.Front != null && BordersAny(l.Front.Sector, earlierlines)) newsectors.Add(l.Front.Sector);
						if(l.Back != null && BordersAny(l.Back.Sector, earlierlines)) newsectors.Add(l.Back.Sector);
					}
				}
			}

			newsectors = new List<Sector>(new HashSet<Sector>(newsectors));
			newsectors.RemoveAll(s => s.IsDisposed || oldsectors.Contains(s) || s.Sidedefs == null || s.Sidedefs.Count == 0);
			return true;
		}

		private static bool BordersAny(Sector s, HashSet<Linedef> lines)
		{
			foreach(Sidedef sd in s.Sidedefs) if(lines.Contains(sd.Line)) return true;
			return false;
		}

		#endregion
	}
}
