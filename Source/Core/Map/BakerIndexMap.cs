#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// The parts of a map's structure the lightmap index mapping needs, as plain arrays
	internal sealed class MapTopology
	{
		public Vector2D[] Vertices;
		public int[] LineStart, LineEnd, LineFront, LineBack; // Vertex and sidedef indices, -1 for no side
		public int[] SideSector;
		public int SectorCount;

		// Reads a UDMF TEXTMAP. Returns null with a reason when it does not parse.
		public static MapTopology FromTextmap(string[] textmap, out string error)
		{
			UniversalParser parser = new UniversalParser();
			if(!parser.InputConfiguration(textmap))
			{
				error = "the TEXTMAP does not parse: " + parser.ErrorDescription;
				return null;
			}

			List<Vector2D> vertices = new List<Vector2D>();
			List<int> start = new List<int>(), end = new List<int>(), front = new List<int>(), back = new List<int>();
			List<int> sidesector = new List<int>();
			int sectors = 0;
			try
			{
				foreach(UniversalEntry entry in parser.Root)
				{
					UniversalCollection c = entry.Value as UniversalCollection;
					if(c == null) continue;
					switch(entry.Key.ToLowerInvariant())
					{
						case "vertex":
							vertices.Add(new Vector2D(Number(c, "x", double.NaN), Number(c, "y", double.NaN)));
							break;
						case "linedef":
							start.Add((int)Number(c, "v1", -1));
							end.Add((int)Number(c, "v2", -1));
							front.Add((int)Number(c, "sidefront", -1));
							back.Add((int)Number(c, "sideback", -1));
							break;
						case "sidedef":
							sidesector.Add((int)Number(c, "sector", -1));
							break;
						case "sector":
							sectors++;
							break;
					}
				}
			}
			catch(FormatException e)
			{
				error = "the TEXTMAP has a bad value: " + e.Message;
				return null;
			}

			MapTopology result = new MapTopology
			{
				Vertices = vertices.ToArray(),
				LineStart = start.ToArray(), LineEnd = end.ToArray(), LineFront = front.ToArray(), LineBack = back.ToArray(),
				SideSector = sidesector.ToArray(),
				SectorCount = sectors,
			};
			error = result.Check();
			return error == null ? result : null;
		}

		public static MapTopology FromMapSet(MapSet map)
		{
			MapTopology result = new MapTopology
			{
				Vertices = new Vector2D[map.Vertices.Count],
				LineStart = new int[map.Linedefs.Count], LineEnd = new int[map.Linedefs.Count],
				LineFront = new int[map.Linedefs.Count], LineBack = new int[map.Linedefs.Count],
				SideSector = new int[map.Sidedefs.Count],
				SectorCount = map.Sectors.Count,
			};
			foreach(Vertex v in map.Vertices) result.Vertices[v.Index] = v.Position;
			foreach(Linedef l in map.Linedefs)
			{
				result.LineStart[l.Index] = l.Start.Index;
				result.LineEnd[l.Index] = l.End.Index;
				result.LineFront[l.Index] = l.Front != null ? l.Front.Index : -1;
				result.LineBack[l.Index] = l.Back != null ? l.Back.Index : -1;
			}
			foreach(Sidedef s in map.Sidedefs) result.SideSector[s.Index] = s.Sector != null ? s.Sector.Index : -1;
			return result;
		}

		// Same positions, references and counts, element for element
		public bool SameAs(MapTopology other)
		{
			if(other == null || SectorCount != other.SectorCount || Vertices.Length != other.Vertices.Length) return false;
			for(int i = 0; i < Vertices.Length; i++)
				if(Vertices[i].x != other.Vertices[i].x || Vertices[i].y != other.Vertices[i].y) return false;
			return Equal(LineStart, other.LineStart) && Equal(LineEnd, other.LineEnd) && Equal(LineFront, other.LineFront)
				&& Equal(LineBack, other.LineBack) && Equal(SideSector, other.SideSector);
		}

		private static bool Equal(int[] a, int[] b)
		{
			if(a.Length != b.Length) return false;
			for(int i = 0; i < a.Length; i++) if(a[i] != b[i]) return false;
			return true;
		}

		// Null when every index is in range
		public string Check()
		{
			for(int i = 0; i < Vertices.Length; i++)
				if(double.IsNaN(Vertices[i].x) || double.IsNaN(Vertices[i].y)) return "vertex " + i + " has no position";
			for(int i = 0; i < LineStart.Length; i++)
			{
				if(LineStart[i] < 0 || LineStart[i] >= Vertices.Length || LineEnd[i] < 0 || LineEnd[i] >= Vertices.Length)
					return "linedef " + i + " uses a missing vertex";
				if(LineFront[i] < -1 || LineFront[i] >= SideSector.Length || LineBack[i] < -1 || LineBack[i] >= SideSector.Length)
					return "linedef " + i + " uses a missing sidedef";
			}
			for(int i = 0; i < SideSector.Length; i++)
				if(SideSector[i] < 0 || SideSector[i] >= SectorCount) return "sidedef " + i + " uses a missing sector";
			return null;
		}

		private static double Number(UniversalCollection c, string key, double fallback)
		{
			foreach(UniversalEntry e in c)
				if(string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)) return Convert.ToDouble(e.Value, System.Globalization.CultureInfo.InvariantCulture);
			return fallback;
		}
	}

	// Maps the sidedef, linedef and sector indices of a baker's renumbered map to the editor's map.
	// A sidedef matches the one with the same directed edge as the side faces it (so a flipped
	// linedef still matches), ties broken by index order. Sectors follow their sidedefs.
	internal sealed class BakerIndexMap
	{
		#region ================== Constants

		// Positions are compared on this grid, finer than UDMF's written precision
		private const double POSITION_GRID = 1024.0;

		#endregion

		#region ================== Properties

		public int[] Sidedef { get; private set; } // Baker sidedef to editor sidedef, -1 for an unused one
		public int[] Sector { get; private set; } // Baker sector to editor sector, -1 for one with no sidedefs
		public int[] Linedef { get; private set; } // Baker linedef to editor linedef

		#endregion

		#region ================== Methods

		// Returns null with a reason when the maps do not correspond one to one
		public static BakerIndexMap Match(MapTopology baker, MapTopology map, out string error)
		{
			error = null;
			int[] bakerline = SideLines(baker, out error);
			if(bakerline == null) { error = "baked map: " + error; return null; }
			int[] mapline = SideLines(map, out error);
			if(mapline == null) { error = "map: " + error; return null; }

			// Editor sidedefs grouped by directed edge, in index order
			Dictionary<EdgeKey, Queue<int>> edges = new Dictionary<EdgeKey, Queue<int>>();
			for(int s = 0; s < map.SideSector.Length; s++)
			{
				if(mapline[s] < 0) continue;
				EdgeKey key = Edge(map, mapline[s], s);
				Queue<int> q;
				if(!edges.TryGetValue(key, out q)) edges.Add(key, q = new Queue<int>());
				q.Enqueue(s);
			}

			BakerIndexMap result = new BakerIndexMap
			{
				Sidedef = new int[baker.SideSector.Length],
				Sector = new int[baker.SectorCount],
				Linedef = new int[baker.LineStart.Length],
			};
			for(int i = 0; i < result.Sector.Length; i++) result.Sector[i] = -1;
			bool[] used = new bool[map.SideSector.Length];

			for(int s = 0; s < baker.SideSector.Length; s++)
			{
				result.Sidedef[s] = -1;
				if(bakerline[s] < 0) continue;
				Queue<int> q;
				if(!edges.TryGetValue(Edge(baker, bakerline[s], s), out q) || q.Count == 0)
				{
					error = "baked sidedef " + s + " has no matching sidedef in the map";
					return null;
				}
				int m = q.Dequeue();
				used[m] = true;
				result.Sidedef[s] = m;

				int bs = baker.SideSector[s], ms = map.SideSector[m];
				if(result.Sector[bs] == -1) result.Sector[bs] = ms;
				else if(result.Sector[bs] != ms)
				{
					error = "baked sector " + bs + " matches more than one sector in the map";
					return null;
				}
			}

			// Leftovers are only allowed on zero-length lines, which the baker removes
			for(int m = 0; m < used.Length; m++)
			{
				if(used[m] || mapline[m] < 0) continue;
				int l = mapline[m];
				if(map.Vertices[map.LineStart[l]] != map.Vertices[map.LineEnd[l]])
				{
					error = "map sidedef " + m + " has no matching sidedef in the bake";
					return null;
				}
			}

			// Two baked sectors must not land on one map sector
			HashSet<int> targets = new HashSet<int>();
			foreach(int t in result.Sector)
			{
				if(t >= 0 && !targets.Add(t))
				{
					error = "map sector " + t + " matches more than one baked sector";
					return null;
				}
			}

			for(int l = 0; l < baker.LineStart.Length; l++)
			{
				int side = baker.LineFront[l] >= 0 ? baker.LineFront[l] : baker.LineBack[l];
				result.Linedef[l] = side >= 0 ? mapline[result.Sidedef[side]] : -1;
			}

			return result;
		}

		// Linedef of each sidedef, -1 when no line uses it; null when a sidedef is shared
		private static int[] SideLines(MapTopology t, out string error)
		{
			error = null;
			int[] result = new int[t.SideSector.Length];
			for(int i = 0; i < result.Length; i++) result[i] = -1;
			for(int l = 0; l < t.LineStart.Length; l++)
			{
				foreach(int s in new[] { t.LineFront[l], t.LineBack[l] })
				{
					if(s < 0) continue;
					if(result[s] != -1)
					{
						error = "sidedef " + s + " is used by more than one side";
						return null;
					}
					result[s] = l;
				}
			}
			return result;
		}

		private static EdgeKey Edge(MapTopology t, int line, int side)
		{
			Vector2D a = t.Vertices[t.LineStart[line]], b = t.Vertices[t.LineEnd[line]];
			if(t.LineFront[line] != side) { Vector2D swap = a; a = b; b = swap; }
			return new EdgeKey(Grid(a.x), Grid(a.y), Grid(b.x), Grid(b.y));
		}

		private static long Grid(double v) { return (long)Math.Round(v * POSITION_GRID); }

		private struct EdgeKey : IEquatable<EdgeKey>
		{
			private readonly long x1, y1, x2, y2;
			public EdgeKey(long x1, long y1, long x2, long y2) { this.x1 = x1; this.y1 = y1; this.x2 = x2; this.y2 = y2; }
			public bool Equals(EdgeKey o) { return x1 == o.x1 && y1 == o.y1 && x2 == o.x2 && y2 == o.y2; }
			public override bool Equals(object o) { return o is EdgeKey && Equals((EdgeKey)o); }
			public override int GetHashCode() { return (int)(x1 * 73856093 ^ y1 * 19349663 ^ x2 * 83492791 ^ y2 * 50331653); }
		}

		#endregion
	}
}
