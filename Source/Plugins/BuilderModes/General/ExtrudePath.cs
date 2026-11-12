#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	// One connected run of selected lines, walked so that positive distances extrude to its left
	public sealed class ExtrudeChain
	{
		// Vertex indices in walk order. A closed chain doesn't repeat its first vertex.
		public readonly List<int> Vertices = new List<int>();

		// Line indices in walk order, Lines[i] joins Vertices[i] and Vertices[i + 1] (wrapping when closed)
		public readonly List<int> Lines = new List<int>();

		// Whether each line is walked from its start to its end
		public readonly List<bool> Forward = new List<bool>();

		public bool Closed;
	}

	// Offset points for one chain. Start[i] and End[i] index Points: where the offset segments
	// leaving and arriving at source vertex i begin and end. They differ only at a bevel.
	public sealed class ExtrudeOffset
	{
		public readonly List<Vector2D> Points = new List<Vector2D>();
		public int[] Start;
		public int[] End;
	}

	public enum ExtrudeProblemKind
	{
		ZeroDistance,
		Collapse,
		SelfIntersection,
		CrossesSource,
		EmptyInside,
		MixedSides,
	}

	public sealed class ExtrudeProblem
	{
		public readonly ExtrudeProblemKind Kind;

		public string Reason
		{
			get
			{
				switch(Kind)
				{
					case ExtrudeProblemKind.ZeroDistance: return "the offset is 0";
					case ExtrudeProblemKind.MixedSides: return "the offset runs along or across a selected line";
					case ExtrudeProblemKind.Collapse: return "the distance collapses a segment";
					case ExtrudeProblemKind.SelfIntersection: return "the extrusion crosses itself";
					case ExtrudeProblemKind.CrossesSource: return "the extrusion crosses the selected lines";
					default: return "a loop can't be extruded into its empty inside";
				}
			}
		}

		public ExtrudeProblem(ExtrudeProblemKind kind)
		{
			Kind = kind;
		}
	}

	public static class ExtrudePath
	{
		#region ================== Constants

		// A convex miter longer than this many times the distance becomes a bevel
		public const double MITER_LIMIT = 4.0;

		public const double MIN_SEGMENT_LENGTH = 1.0;

		private const double EPSILON = 1e-6;

		#endregion

		#region ================== Chains

		// Builds chains from line endpoint pairs. emptyside per line: +1 when only the side left of
		// start->end is empty (a one-sided line with a front), -1 when only the right side is, 0 otherwise.
		// Returns null with branchvertex set when a vertex joins more than two selected lines.
		public static List<ExtrudeChain> BuildChains(IList<int> linestart, IList<int> lineend, IList<int> emptyside, out int branchvertex)
		{
			branchvertex = -1;
			Dictionary<int, List<int>> lines = new Dictionary<int, List<int>>();
			List<int> order = new List<int>();
			for(int i = 0; i < linestart.Count; i++)
			{
				foreach(int v in new[] { linestart[i], lineend[i] })
				{
					List<int> list;
					if(!lines.TryGetValue(v, out list))
					{
						list = new List<int>();
						lines.Add(v, list);
						order.Add(v);
					}
					list.Add(i);
				}
			}

			foreach(int v in order)
			{
				if(lines[v].Count > 2)
				{
					branchvertex = v;
					return null;
				}
			}

			List<ExtrudeChain> chains = new List<ExtrudeChain>();
			bool[] used = new bool[linestart.Count];
			for(int first = 0; first < linestart.Count; first++)
			{
				if(used[first]) continue;

				// Back up to an end of the chain, or once around a loop
				int line = first;
				int vertex = linestart[first];
				bool closed = false;
				while(true)
				{
					int other = OtherLine(lines[vertex], line);
					if(other == -1) break;
					if(other == first)
					{
						closed = true;
						break;
					}
					line = other;
					vertex = (linestart[line] == vertex ? lineend[line] : linestart[line]);
				}

				ExtrudeChain chain = new ExtrudeChain();
				chain.Closed = closed;
				if(closed)
				{
					line = first;
					vertex = linestart[first];
				}

				// Walk forward from the end found, starting with the line leaving it
				while(true)
				{
					used[line] = true;
					bool forward = (linestart[line] == vertex);
					chain.Vertices.Add(vertex);
					chain.Lines.Add(line);
					chain.Forward.Add(forward);
					vertex = (forward ? lineend[line] : linestart[line]);
					int next = OtherLine(lines[vertex], line);
					if(next == -1 || used[next])
					{
						if(!closed) chain.Vertices.Add(vertex);
						break;
					}
					line = next;
				}

				Orient(chain, emptyside);
				chains.Add(chain);
			}

			return chains;
		}

		private static int OtherLine(List<int> lines, int line)
		{
			foreach(int l in lines) if(l != line) return l;
			return -1;
		}

		// Puts the first one-sided line's empty side on the left; with none, walks the lowest line forward
		private static void Orient(ExtrudeChain chain, IList<int> emptyside)
		{
			bool reverse = false;
			bool found = false;
			for(int i = 0; i < chain.Lines.Count; i++)
			{
				int side = emptyside[chain.Lines[i]];
				if(side == 0) continue;
				reverse = ((side > 0) != chain.Forward[i]);
				found = true;
				break;
			}

			if(!found)
			{
				int lowest = 0;
				for(int i = 1; i < chain.Lines.Count; i++)
					if(chain.Lines[i] < chain.Lines[lowest]) lowest = i;
				reverse = !chain.Forward[lowest];
			}

			if(!reverse) return;

			if(chain.Closed)
			{
				// Vertex 0 stays first: lines reverse, vertices 1..n-1 reverse
				chain.Vertices.Reverse(1, chain.Vertices.Count - 1);
				chain.Lines.Reverse();
				chain.Forward.Reverse();
			}
			else
			{
				chain.Vertices.Reverse();
				chain.Lines.Reverse();
				chain.Forward.Reverse();
			}
			for(int i = 0; i < chain.Forward.Count; i++) chain.Forward[i] = !chain.Forward[i];
		}

		#endregion

		#region ================== Offsets

		// Offsets a chain's points (in walk order, first not repeated when closed) to their left by distance
		public static ExtrudeOffset Offset(IList<Vector2D> points, bool closed, double distance, int decimals)
		{
			int count = points.Count;
			int segments = (closed ? count : count - 1);
			Vector2D[] normals = new Vector2D[segments];
			for(int i = 0; i < segments; i++)
				normals[i] = (points[(i + 1) % count] - points[i]).GetNormal().GetPerpendicular();

			ExtrudeOffset result = new ExtrudeOffset();
			result.Start = new int[count];
			result.End = new int[count];
			for(int i = 0; i < count; i++)
			{
				Vector2D p = points[i];
				bool hasin = (closed || i > 0);
				bool hasout = (closed || i < count - 1);
				if(!hasin || !hasout)
				{
					// Open end: perpendicular side wall
					Vector2D n = normals[hasout ? i : segments - 1];
					result.Start[i] = result.End[i] = Add(result, p + n * distance, decimals);
					continue;
				}

				Vector2D n1 = normals[(i - 1 + segments) % segments];
				Vector2D n2 = normals[i];
				Vector2D sum = n1 + n2;
				double cos = (sum.GetLengthSq() < EPSILON ? 0.0 : Vector2D.DotProduct(sum.GetNormal(), n1));

				// Turning right puts the left side outside the turn
				Vector2D d1 = p - points[(i - 1 + count) % count];
				Vector2D d2 = points[(i + 1) % count] - p;
				double cross = d1.x * d2.y - d1.y * d2.x;
				bool convex = (cross * distance < 0.0);

				// Bevel a long convex miter. A full reversal (cos 0) has no miter on either side.
				if(cos < EPSILON || (convex && cos < 1.0 / MITER_LIMIT))
				{
					result.End[i] = Add(result, p + n1 * distance, decimals);
					result.Start[i] = Add(result, p + n2 * distance, decimals);
				}
				else
				{
					result.Start[i] = result.End[i] = Add(result, p + sum.GetNormal() * (distance / cos), decimals);
				}
			}

			return result;
		}

		// Moves an open chain's points by v, keeping its shape
		public static ExtrudeOffset Translate(IList<Vector2D> points, Vector2D v, int decimals)
		{
			ExtrudeOffset result = new ExtrudeOffset();
			result.Start = new int[points.Count];
			result.End = new int[points.Count];
			for(int i = 0; i < points.Count; i++)
				result.Start[i] = result.End[i] = Add(result, points[i] + v, decimals);
			return result;
		}

		private static int Add(ExtrudeOffset offset, Vector2D p, int decimals)
		{
			offset.Points.Add(new Vector2D(Math.Round(p.x, decimals), Math.Round(p.y, decimals)));
			return offset.Points.Count - 1;
		}

		// Whether a positive or negative distance moves a loop towards its own inside
		public static bool ExtrudesInside(IList<Vector2D> points, bool closed, double distance)
		{
			if(!closed) return false;
			double area = 0.0;
			for(int i = 0; i < points.Count; i++)
			{
				Vector2D a = points[i];
				Vector2D b = points[(i + 1) % points.Count];
				area += a.x * b.y - b.x * a.y;
			}

			// Counter-clockwise (positive area) has its inside on the left, where positive distances go
			return area * distance > 0.0;
		}

		#endregion

		#region ================== Validation

		// Returns null when the extrusion can be applied
		public static ExtrudeProblem Validate(IList<Vector2D> points, bool closed, ExtrudeOffset offset, double distance)
		{
			if(Math.Abs(distance) < EPSILON) return new ExtrudeProblem(ExtrudeProblemKind.ZeroDistance);

			int count = points.Count;
			int segments = (closed ? count : count - 1);

			// Offset segments that shrank to nothing or turned around
			for(int i = 0; i < segments; i++)
			{
				int j = (i + 1) % count;
				Vector2D source = points[j] - points[i];
				Vector2D moved = offset.Points[offset.End[j]] - offset.Points[offset.Start[i]];
				if(moved.GetLength() < MIN_SEGMENT_LENGTH || Vector2D.DotProduct(moved, source) <= 0.0)
					return new ExtrudeProblem(ExtrudeProblemKind.Collapse);
			}

			return ValidateOutline(points, closed, offset);
		}

		// Validates an open chain translated by v. side is +1 when every segment extrudes to its
		// left (as a positive distance would), -1 when every one extrudes to its right, else 0.
		public static ExtrudeProblem ValidateTranslation(IList<Vector2D> points, ExtrudeOffset offset, Vector2D v, out int side)
		{
			side = 0;
			if(v.GetLength() < EPSILON) return new ExtrudeProblem(ExtrudeProblemKind.ZeroDistance);

			// Every strip needs some width on the same side of its own segment
			for(int i = 0; i < points.Count - 1; i++)
			{
				Vector2D normal = (points[i + 1] - points[i]).GetNormal().GetPerpendicular();
				double along = Vector2D.DotProduct(v, normal);
				int s = (along >= MIN_SEGMENT_LENGTH ? 1 : along <= -MIN_SEGMENT_LENGTH ? -1 : 0);
				if(s == 0 || (side != 0 && s != side))
				{
					side = 0;
					return new ExtrudeProblem(ExtrudeProblemKind.MixedSides);
				}
				side = s;
			}

			ExtrudeProblem problem = ValidateOutline(points, false, offset);
			if(problem != null) side = 0;
			return problem;
		}

		// Checks the drawn outline against itself and the source chain
		private static ExtrudeProblem ValidateOutline(IList<Vector2D> points, bool closed, ExtrudeOffset offset)
		{
			int count = points.Count;
			int segments = (closed ? count : count - 1);

			// The drawn outline: offset polyline, plus side walls for an open chain
			List<Vector2D> outline = new List<Vector2D>();
			if(!closed) outline.Add(points[0]);
			outline.AddRange(offset.Points);
			if(!closed) outline.Add(points[count - 1]);
			int outlinesegments = (closed ? outline.Count : outline.Count - 1);

			for(int a = 0; a < outlinesegments; a++)
			{
				for(int b = a + 2; b < outlinesegments; b++)
				{
					if(closed && a == 0 && b == outlinesegments - 1) continue;
					if(SegmentsTouch(outline[a], outline[(a + 1) % outline.Count], outline[b], outline[(b + 1) % outline.Count]))
						return new ExtrudeProblem(ExtrudeProblemKind.SelfIntersection);
				}
			}

			// Against the source chain. An open chain's side walls start on its end vertices, which don't count.
			for(int a = 0; a < outlinesegments; a++)
			{
				Vector2D a1 = outline[a];
				Vector2D a2 = outline[(a + 1) % outline.Count];
				bool skipfirst = (!closed && a == 0);
				bool skiplast = (!closed && a == outlinesegments - 1);
				for(int s = 0; s < segments; s++)
				{
					if(SegmentsCross(a1, a2, points[s], points[(s + 1) % count], skipfirst, skiplast))
						return new ExtrudeProblem(ExtrudeProblemKind.CrossesSource);
				}
			}

			return null;
		}

		// Closed-interval segment intersection, touching endpoints included
		private static bool SegmentsTouch(Vector2D a1, Vector2D a2, Vector2D b1, Vector2D b2)
		{
			return SegmentsCross(a1, a2, b1, b2, false, false);
		}

		// Segment intersection. skipa1 ignores contact at a's first point, skipa2 at its last.
		private static bool SegmentsCross(Vector2D a1, Vector2D a2, Vector2D b1, Vector2D b2, bool skipa1, bool skipa2)
		{
			Vector2D r = a2 - a1;
			Vector2D s = b2 - b1;
			double div = r.x * s.y - r.y * s.x;
			Vector2D q = b1 - a1;
			if(Math.Abs(div) < EPSILON)
			{
				// Parallel: only collinear overlap counts
				if(Math.Abs(q.x * r.y - q.y * r.x) > EPSILON * Math.Max(1.0, r.GetLength())) return false;
				double rr = r.GetLengthSq();
				if(rr < EPSILON) return false;
				double t0 = Vector2D.DotProduct(q, r) / rr;
				double t1 = Vector2D.DotProduct(b2 - a1, r) / rr;
				double lo = Math.Max(Math.Min(t0, t1), skipa1 ? EPSILON : 0.0);
				double hi = Math.Min(Math.Max(t0, t1), skipa2 ? 1.0 - EPSILON : 1.0);
				return lo <= hi;
			}

			double t = (q.x * s.y - q.y * s.x) / div;
			double u = (q.x * r.y - q.y * r.x) / div;
			double tlo = (skipa1 ? EPSILON : -EPSILON);
			double thi = (skipa2 ? 1.0 - EPSILON : 1.0 + EPSILON);
			return t >= tlo && t <= thi && u >= -EPSILON && u <= 1.0 + EPSILON;
		}

		#endregion
	}
}
