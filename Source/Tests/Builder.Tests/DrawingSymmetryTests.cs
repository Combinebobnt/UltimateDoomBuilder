using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder.Geometry;
using Xunit;

namespace Builder.Tests
{
	public class DrawingSymmetryTests
	{
		private static readonly Vector2D P = new Vector2D(64, 32);

		private static List<DrawnVertex> Path(params double[] xy)
		{
			List<DrawnVertex> result = new List<DrawnVertex>();
			for(int i = 0; i < xy.Length; i += 2)
			{
				DrawnVertex v = new DrawnVertex();
				v.pos = new Vector2D(xy[i], xy[i + 1]);
				v.stitch = true;
				v.stitchline = true;
				result.Add(v);
			}
			return result;
		}

		private static void Equal(List<DrawnVertex> expected, List<DrawnVertex> actual)
		{
			Assert.True(expected.Count == actual.Count, $"expected {expected.Count} points, got {actual.Count}: {Show(actual)}");
			for(int i = 0; i < expected.Count; i++)
				Assert.True(Math.Abs(expected[i].pos.x - actual[i].pos.x) < 1e-9 && Math.Abs(expected[i].pos.y - actual[i].pos.y) < 1e-9,
					$"point {i}: expected {expected[i].pos}, got {actual[i].pos} in {Show(actual)}");
		}

		private static string Show(List<DrawnVertex> path)
		{
			List<string> parts = new List<string>();
			foreach(DrawnVertex v in path) parts.Add(v.pos.ToString());
			return string.Join(" ", parts);
		}

		private static double SignedArea(List<DrawnVertex> p)
		{
			double a = 0;
			for(int i = 0; i < p.Count - 1; i++) a += p[i].pos.x * p[i + 1].pos.y - p[i + 1].pos.x * p[i].pos.y;
			return a / 2;
		}

		[Fact]
		public void OpenPathOffAxisGetsReversedMirrorWithStitchFlags()
		{
			List<DrawnVertex> p = Path(0, 0, 32, 0, 32, 64);
			DrawnVertex loose = p[1];
			loose.stitch = false;
			p[1] = loose;

			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(p, P, true, false);
			Assert.Equal(2, r.Count);
			Equal(p, r[0]);
			Equal(Path(96, 64, 96, 0, 128, 0), r[1]);
			Assert.False(r[1][1].stitch);
			Assert.True(r[1][0].stitch);
		}

		[Fact]
		public void LastOrFirstPointOnAxisMergesIntoOneOpenPath()
		{
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(0, 0, 0, 64, 64, 64), P, true, false);
			Assert.Single(r);
			Equal(Path(0, 0, 0, 64, 64, 64, 128, 64, 128, 0), r[0]);

			r = DrawingSymmetry.Mirror(Path(64, 64, 0, 64, 0, 0), P, true, false);
			Assert.Single(r);
			Equal(Path(128, 0, 128, 64, 64, 64, 0, 64, 0, 0), r[0]);
		}

		[Fact]
		public void BothEndsOnAxisClosesOneLoopKeepingWinding()
		{
			List<DrawnVertex> p = Path(64, 0, 0, 32, 64, 64);
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(p, P, true, false);
			Assert.Single(r);
			Equal(Path(64, 0, 0, 32, 64, 64, 128, 32, 64, 0), r[0]);

			List<DrawnVertex> closedhalf = new List<DrawnVertex>(p) { p[0] };
			Assert.Equal(Math.Sign(SignedArea(closedhalf)), Math.Sign(SignedArea(r[0])));
		}

		[Fact]
		public void BothEndsOnAxisButCrossingDoesNotMerge()
		{
			List<DrawnVertex> p = Path(64, 0, 0, 32, 96, 48, 64, 64);
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(p, P, true, false);
			Assert.Equal(2, r.Count);
			Equal(p, r[0]);
			Equal(Path(64, 64, 32, 48, 128, 32, 64, 0), r[1]);
		}

		[Fact]
		public void CoreRunningAlongAxisDoesNotMerge()
		{
			List<DrawnVertex> p = Path(64, 0, 0, 16, 64, 32, 64, 48, 0, 56, 64, 64);
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(p, P, true, false);
			Assert.Equal(2, r.Count);
			Equal(p, r[0]);

			// A single interior touch still merges
			r = DrawingSymmetry.Mirror(Path(64, 0, 0, 16, 64, 32, 0, 48, 64, 64), P, true, false);
			Assert.Single(r);
			Assert.Equal(9, r[0].Count);
		}

		[Fact]
		public void BothAxesMirrorEndRunsToo()
		{
			// x-pass: loop plus the run (64,-32)-(64,0); y-pass (axis y=32) mirrors each
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(64, -32, 64, 0, 0, 16, 64, 24), P, true, true);
			Assert.Equal(4, r.Count);
			Equal(Path(64, -32, 64, 0), r[2]);
			Equal(Path(64, 64, 64, 96), r[3]);
		}

		[Fact]
		public void ClosedPathsNeverMergeAndKeepWinding()
		{
			List<DrawnVertex> square = Path(0, 0, 32, 0, 32, 32, 0, 32, 0, 0);
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(square, P, true, false);
			Assert.Equal(2, r.Count);
			Assert.Equal(Math.Sign(SignedArea(r[0])), Math.Sign(SignedArea(r[1])));

			List<DrawnVertex> onaxis = Path(64, 0, 96, 0, 96, 32, 64, 32, 64, 0);
			Assert.Equal(2, DrawingSymmetry.Mirror(onaxis, P, true, false).Count);
		}

		[Fact]
		public void OnAxisEndRunsComeOutAsSeparatePaths()
		{
			// Leading run (64,-32)-(64,0), then out to the left and back to the axis
			List<List<DrawnVertex>> lead = DrawingSymmetry.Mirror(Path(64, -32, 64, 0, 0, 32, 64, 64), P, true, false);
			Assert.Equal(2, lead.Count);
			Equal(Path(64, 0, 0, 32, 64, 64, 128, 32, 64, 0), lead[0]);
			Equal(Path(64, -32, 64, 0), lead[1]);

			List<List<DrawnVertex>> trail = DrawingSymmetry.Mirror(Path(64, 0, 0, 32, 64, 64, 64, 96), P, true, false);
			Assert.Equal(2, trail.Count);
			Equal(Path(64, 0, 0, 32, 64, 64, 128, 32, 64, 0), trail[0]);
			Equal(Path(64, 64, 64, 96), trail[1]);

			// No output path doubles back on a segment
			foreach(List<List<DrawnVertex>> r in new[] { lead, trail })
				foreach(List<DrawnVertex> path in r)
					for(int i = 0; i + 2 < path.Count; i++)
						Assert.False(Same(path[i].pos, path[i + 2].pos), Show(path));
		}

		private static bool Same(Vector2D a, Vector2D b)
		{
			return Math.Abs(a.x - b.x) < 1e-9 && Math.Abs(a.y - b.y) < 1e-9;
		}

		[Fact]
		public void ConsecutiveDuplicatesAreDropped()
		{
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(0, 0, 0, 0, 32, 0, 32, 0.0004, 32, 64), P, true, false);
			Assert.Equal(2, r.Count);
			Equal(Path(0, 0, 32, 0, 32, 64), r[0]);
			Assert.Equal(3, r[1].Count);
		}

		[Fact]
		public void SymmetricOrOnAxisPathsAreNotCopied()
		{
			List<DrawnVertex> box = Path(32, 0, 96, 0, 96, 64, 32, 64, 32, 0);
			Assert.Single(DrawingSymmetry.Mirror(box, P, true, false));

			// Same box started mid-edge
			Assert.Single(DrawingSymmetry.Mirror(Path(96, 0, 96, 64, 32, 64, 32, 0, 96, 0), P, true, false));

			Assert.Single(DrawingSymmetry.Mirror(Path(64, 0, 64, 64), P, true, false));
			Assert.Single(DrawingSymmetry.Mirror(Path(0, 0, 128, 0), P, true, false));
		}

		[Fact]
		public void FewerThanTwoPointsUnchanged()
		{
			Assert.Single(DrawingSymmetry.Mirror(Path(), P, true, true));
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(0, 0), P, true, true);
			Assert.Single(r);
			Equal(Path(0, 0), r[0]);
		}

		[Fact]
		public void BothAxesQuarterPathClosesOneLoop()
		{
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(64, 96, 0, 96, 0, 32), P, true, true);
			Assert.Single(r);
			List<DrawnVertex> loop = r[0];
			Assert.True(Same(loop[0].pos, loop[loop.Count - 1].pos), Show(loop));
			Assert.Equal(9, loop.Count);
			foreach(DrawnVertex v in Path(0, 96, 128, 96, 0, -32, 128, -32))
				Assert.True(loop.Exists(x => Same(x.pos, v.pos)), v.pos + " missing from " + Show(loop));
		}

		[Fact]
		public void BothAxesNoContactGivesFourCopies()
		{
			List<DrawnVertex> p = Path(0, 64, 16, 80, 32, 64);
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(p, P, true, true);
			Assert.Equal(4, r.Count);
			Equal(p, r[0]);
			List<DrawnVertex> rotated = new List<DrawnVertex>();
			foreach(DrawnVertex v in p)
			{
				DrawnVertex m = v;
				m.pos = PivotMath.RotatedAbout(v.pos, P, Math.PI);
				rotated.Add(m);
			}
			Equal(rotated, r[3]);
		}

		[Fact]
		public void AxisToleranceKeepsOriginalCoordinate()
		{
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(0, 0, 63.9995, 64), P, true, false);
			Assert.Single(r);
			Assert.Equal(3, r[0].Count);
			Assert.Equal(63.9995, r[0][1].pos.x);

			Assert.Equal(2, DrawingSymmetry.Mirror(Path(0, 0, 63.99, 64), P, true, false).Count);
		}

		[Fact]
		public void AnyOutsideFindsMirrorPastBoundary()
		{
			List<List<DrawnVertex>> r = DrawingSymmetry.Mirror(Path(-100, 0, -100, 64), new Vector2D(500, 0), true, false);
			Assert.True(DrawingSymmetry.AnyOutside(r, -1000, 1000, 1000, -1000));
			Assert.False(DrawingSymmetry.AnyOutside(r, -1000, 1200, 1000, -1000));
		}
	}
}
