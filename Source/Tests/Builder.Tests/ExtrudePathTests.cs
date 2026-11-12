using System;
using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder.Geometry;
using Xunit;

namespace Builder.Tests
{
	public class ExtrudePathTests
	{
		private static List<ExtrudeChain> Chains(int[,] lines, int[] emptyside, out int branch)
		{
			int count = lines.GetLength(0);
			int[] start = new int[count];
			int[] end = new int[count];
			for(int i = 0; i < count; i++)
			{
				start[i] = lines[i, 0];
				end[i] = lines[i, 1];
			}
			return ExtrudePath.BuildChains(start, end, emptyside ?? new int[count], out branch);
		}

		private static List<Vector2D> Points(params double[] xy)
		{
			List<Vector2D> result = new List<Vector2D>();
			for(int i = 0; i < xy.Length; i += 2) result.Add(new Vector2D(xy[i], xy[i + 1]));
			return result;
		}

		private static void Near(double x, double y, Vector2D actual)
		{
			Assert.True(Math.Abs(actual.x - x) < 0.001 && Math.Abs(actual.y - y) < 0.001, $"expected ({x}, {y}), got {actual}");
		}

		private static ExtrudeProblem Check(List<Vector2D> points, bool closed, double distance)
		{
			return ExtrudePath.Validate(points, closed, ExtrudePath.Offset(points, closed, distance, 3), distance);
		}

		[Fact]
		public void OpenChainWalksFromAnEnd()
		{
			int branch;
			List<ExtrudeChain> chains = Chains(new[,] { { 1, 2 }, { 0, 1 }, { 2, 3 } }, null, out branch);
			Assert.Equal(1, chains.Count);
			Assert.False(chains[0].Closed);
			Assert.Equal(new[] { 0, 1, 2, 3 }, chains[0].Vertices.ToArray());
			Assert.Equal(new[] { 1, 0, 2 }, chains[0].Lines.ToArray());
		}

		[Fact]
		public void LoopIsClosedWithoutRepeatingAVertex()
		{
			int branch;
			List<ExtrudeChain> chains = Chains(new[,] { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 } }, null, out branch);
			Assert.Equal(1, chains.Count);
			Assert.True(chains[0].Closed);
			Assert.Equal(new[] { 0, 1, 2, 3 }, chains[0].Vertices.ToArray());
			Assert.Equal(new[] { 0, 1, 2, 3 }, chains[0].Lines.ToArray());
		}

		[Fact]
		public void DisjointRunsBecomeSeparateChains()
		{
			int branch;
			List<ExtrudeChain> chains = Chains(new[,] { { 0, 1 }, { 5, 6 }, { 1, 2 } }, null, out branch);
			Assert.Equal(2, chains.Count);
			Assert.Equal(new[] { 0, 1, 2 }, chains[0].Vertices.ToArray());
			Assert.Equal(new[] { 5, 6 }, chains[1].Vertices.ToArray());
		}

		[Fact]
		public void BranchIsRefused()
		{
			int branch;
			Assert.Null(Chains(new[,] { { 0, 1 }, { 1, 2 }, { 1, 3 } }, null, out branch));
			Assert.Equal(1, branch);
		}

		[Fact]
		public void MixedLineDirectionsWalkConsistently()
		{
			// Line 1 points against the walk; with no one-sided lines the lowest line walks forward
			int branch;
			ExtrudeChain chain = Chains(new[,] { { 0, 1 }, { 2, 1 } }, null, out branch)[0];
			Assert.Equal(new[] { 0, 1, 2 }, chain.Vertices.ToArray());
			Assert.Equal(new[] { true, false }, chain.Forward.ToArray());
		}

		[Fact]
		public void OneSidedEmptySideIsPositive()
		{
			// Line 1's only sidedef is its back, so its empty side is right of start->end: the walk reverses
			int branch;
			ExtrudeChain chain = Chains(new[,] { { 0, 1 }, { 1, 2 } }, new[] { 0, -1 }, out branch)[0];
			Assert.Equal(new[] { 2, 1, 0 }, chain.Vertices.ToArray());
			Assert.Equal(new[] { false, false }, chain.Forward.ToArray());

			// A reversed loop keeps its first vertex
			chain = Chains(new[,] { { 0, 1 }, { 1, 2 }, { 2, 0 } }, new[] { -1, -1, -1 }, out branch)[0];
			Assert.Equal(new[] { 0, 2, 1 }, chain.Vertices.ToArray());
			Assert.Equal(new[] { 2, 1, 0 }, chain.Lines.ToArray());
		}

		[Fact]
		public void StraightRunOffsetsPerpendicular()
		{
			ExtrudeOffset offset = ExtrudePath.Offset(Points(0, 0, 0, 128), false, 64, 3);
			Assert.Equal(2, offset.Points.Count);
			Near(-64, 0, offset.Points[0]);
			Near(-64, 128, offset.Points[1]);
		}

		[Fact]
		public void RightAngleCornersMiter()
		{
			// Right turn: the left side is outside the corner
			ExtrudeOffset convex = ExtrudePath.Offset(Points(0, 0, 100, 0, 100, -100), false, 16, 3);
			Assert.Equal(3, convex.Points.Count);
			Near(116, 16, convex.Points[1]);

			// Left turn: inside the corner
			ExtrudeOffset concave = ExtrudePath.Offset(Points(0, 0, 100, 0, 100, 100), false, 16, 3);
			Assert.Equal(3, concave.Points.Count);
			Near(0, 16, concave.Points[0]);
			Near(84, 16, concave.Points[1]);
			Near(84, 100, concave.Points[2]);
		}

		[Fact]
		public void SharpConvexCornerBevels()
		{
			// 20 degrees between the two segments: the miter would be about 5.8 times the distance
			double a = Angle2D.DegToRad(-160);
			Vector2D far = new Vector2D(100 + Math.Cos(a) * 100, Math.Sin(a) * 100);
			ExtrudeOffset offset = ExtrudePath.Offset(Points(0, 0, 100, 0, far.x, far.y), false, 10, 3);
			Assert.Equal(4, offset.Points.Count);
			Assert.NotEqual(offset.Start[1], offset.End[1]);
			Near(100, 10, offset.Points[offset.End[1]]);
			Near(100 + Math.Sin(a) * -10, Math.Cos(a) * 10, offset.Points[offset.Start[1]]);
			Assert.Null(ExtrudePath.Validate(Points(0, 0, 100, 0, far.x, far.y), false, offset, 10));
		}

		[Fact]
		public void OffsetsRoundToFormatPrecision()
		{
			ExtrudeOffset offset = ExtrudePath.Offset(Points(0, 0, 100, 100), false, 10, 0);
			Assert.Equal(new Vector2D(-7, 7), offset.Points[0]);
			Assert.Equal(new Vector2D(93, 107), offset.Points[1]);
		}

		[Fact]
		public void LoopOffsetsEveryCorner()
		{
			// Clockwise room: left of each wall is outside
			List<Vector2D> room = Points(0, 0, 0, 128, 128, 128, 128, 0);
			ExtrudeOffset offset = ExtrudePath.Offset(room, true, 16, 3);
			Assert.Equal(4, offset.Points.Count);
			Near(-16, -16, offset.Points[0]);
			Near(-16, 144, offset.Points[1]);
			Assert.Null(ExtrudePath.Validate(room, true, offset, 16));
		}

		[Fact]
		public void LoopInsideFollowsWindingAndSign()
		{
			List<Vector2D> clockwise = Points(0, 0, 0, 128, 128, 128, 128, 0);
			Assert.False(ExtrudePath.ExtrudesInside(clockwise, true, 16));
			Assert.True(ExtrudePath.ExtrudesInside(clockwise, true, -16));
			clockwise.Reverse();
			Assert.True(ExtrudePath.ExtrudesInside(clockwise, true, 16));
			Assert.False(ExtrudePath.ExtrudesInside(Points(0, 0, 0, 128), false, -16));
		}

		[Fact]
		public void ValidExtrusionsPass()
		{
			Assert.Null(Check(Points(0, 0, 0, 128), false, 64));
			Assert.Null(Check(Points(0, 0, 0, 128), false, -64));
			Assert.Null(Check(Points(0, 0, 100, 0, 100, 100), false, 50));
			Assert.Null(Check(Points(0, 0, 100, 0, 100, -100), false, 200));
		}

		[Fact]
		public void ZeroDistanceIsRefused()
		{
			Assert.Equal(ExtrudeProblemKind.ZeroDistance, Check(Points(0, 0, 0, 128), false, 0).Kind);
		}

		[Fact]
		public void ConcaveCollapseIsRefused()
		{
			ExtrudeProblem problem = Check(Points(0, 0, 100, 0, 100, 40), false, 50);
			Assert.Equal(ExtrudeProblemKind.Collapse, problem.Kind);
		}

		[Fact]
		public void SelfIntersectionIsRefused()
		{
			// The pocket's last leg comes back down across the first leg's offset
			ExtrudeProblem problem = Check(Points(0, 0, 100, 0, 100, 100, 20, 100, 20, 10), false, 30);
			Assert.Equal(ExtrudeProblemKind.SelfIntersection, problem.Kind);
		}

		[Fact]
		public void CrossingTheSourceIsRefused()
		{
			// A clockwise spiral whose last leg passes over the start's side wall
			ExtrudeProblem problem = Check(Points(0, 0, 50, 0, 50, -50, -50, -50, -50, 50, 10, 100), false, 95);
			Assert.Equal(ExtrudeProblemKind.CrossesSource, problem.Kind);
		}

		private static ExtrudeProblem Translated(List<Vector2D> points, double x, double y, out int side)
		{
			Vector2D v = new Vector2D(x, y);
			return ExtrudePath.ValidateTranslation(points, ExtrudePath.Translate(points, v, 3), v, out side);
		}

		[Fact]
		public void DiagonalTranslationKeepsGridVertices()
		{
			// A line about 30 degrees off the x axis, moved off its normal
			List<Vector2D> points = Points(0, 0, 112, 64);
			ExtrudeOffset offset = ExtrudePath.Translate(points, new Vector2D(32, 48), 3);
			Assert.True(offset.Points[0].x == 32 && offset.Points[0].y == 48, offset.Points[0].ToString());
			Assert.True(offset.Points[1].x == 144 && offset.Points[1].y == 112, offset.Points[1].ToString());

			int side;
			Assert.Null(Translated(points, 32, 48, out side));
			Assert.Equal(1, side);
			Assert.Null(Translated(points, -32, -48, out side));
			Assert.Equal(-1, side);
		}

		[Fact]
		public void TranslationNeedsWidthOnOneSide()
		{
			int side;
			Assert.Equal(ExtrudeProblemKind.ZeroDistance, Translated(Points(0, 0, 100, 0), 0, 0, out side).Kind);
			Assert.Equal(ExtrudeProblemKind.MixedSides, Translated(Points(0, 0, 100, 0), 64, 0, out side).Kind);
			Assert.Equal(ExtrudeProblemKind.MixedSides, Translated(Points(0, 0, 100, 0), 64, 0.5, out side).Kind);
			Assert.Equal(0, side);

			// L chain: legs' normals are up and left
			List<Vector2D> l = Points(0, 0, 100, 0, 100, 100);
			Assert.Null(Translated(l, -32, 32, out side));
			Assert.Equal(1, side);
			Assert.Equal(ExtrudeProblemKind.MixedSides, Translated(l, 32, 32, out side).Kind);

			// A U's normals span a half turn, so any offset folds one leg over the chain
			List<Vector2D> u = Points(0, 100, 0, 0, 100, 0, 100, 100);
			Assert.Equal(ExtrudeProblemKind.MixedSides, Translated(u, 150, 50, out side).Kind);
			Assert.Equal(ExtrudeProblemKind.MixedSides, Translated(u, 0, 50, out side).Kind);
		}

		[Fact]
		public void FacingLinesTranslateToOppositeSides()
		{
			int sidea, sideb;
			Assert.Null(Translated(Points(0, 0, 100, 0), 0, 32, out sidea));
			Assert.Null(Translated(Points(100, 64, 0, 64), 0, 32, out sideb));
			Assert.Equal(1, sidea);
			Assert.Equal(-1, sideb);
		}
	}
}
