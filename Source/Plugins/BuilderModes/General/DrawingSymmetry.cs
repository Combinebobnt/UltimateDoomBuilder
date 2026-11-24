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
		// The original (or the path merged with its mirror image) comes first.
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

			bool closed = Same(p[0].pos, p[n - 1].pos);
			if(!closed && (on[0] || on[n - 1]))
			{
				// Leading on-axis run ends at a, trailing run starts at b
				int a = -1;
				while(a + 1 < n && on[a + 1]) a++;
				int b = n;
				while(b - 1 >= 0 && on[b - 1]) b--;

				int first = Math.Max(a, 0);
				int last = Math.Min(b, n - 1);
				if(!Crosses(p, first, last, pivot, vertical))
				{
					List<DrawnVertex> core = p.GetRange(first, last - first + 1);
					int k = core.Count;
					List<DrawnVertex> merged = new List<DrawnVertex>();

					if(a >= 0 && b <= n - 1)
					{
						// Both ends on the axis: one closed loop
						merged.AddRange(core);
						for(int i = k - 2; i >= 1; i--) merged.Add(Mirrored(core[i], pivot, vertical));
						merged.Add(core[0]);
					}
					else if(b <= n - 1)
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

					result.Add(Deduped(merged));
					if(a >= 1) result.Add(p.GetRange(0, a + 1));
					if(b <= n - 2) result.Add(p.GetRange(b, n - b));
					return result;
				}
			}

			// Two paths: the original and its reversed mirror image
			result.Add(p);
			result.Add(ReversedMirror(p, pivot, vertical));
			return result;
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

				// A later copy can split an earlier copy's sector; those pieces are never marked
				if(i > 0)
				{
					foreach(Linedef l in General.Map.Map.GetMarkedLinedefs(true))
					{
						if(l.Front != null) newsectors.Add(l.Front.Sector);
						if(l.Back != null) newsectors.Add(l.Back.Sector);
					}
				}
			}

			newsectors = new List<Sector>(new HashSet<Sector>(newsectors));
			newsectors.RemoveAll(s => s.IsDisposed || oldsectors.Contains(s) || s.Sidedefs == null || s.Sidedefs.Count == 0);
			return true;
		}

		#endregion
	}
}
