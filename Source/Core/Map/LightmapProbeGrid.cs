#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Rendering;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// The baked light probes on the game's grid, so a thing finds the probe the game lights it from:
	// the nearest one in its own cell or the 8 around it, none when those cells are empty.
	public sealed class LightmapProbeGrid
	{
		#region ================== Constants

		public const int CELL_SIZE = 32;

		#endregion

		#region ================== Variables

		private readonly float[] probes; // x, y, z, r, g, b, sorted by cell Y, then X
		private readonly Dictionary<long, int[]> cells = new Dictionary<long, int[]>(); // cell to first probe, count

		#endregion

		#region ================== Properties

		public int Count { get { return probes.Length / 6; } }

		#endregion

		#region ================== Constructor

		public LightmapProbeGrid(float[] source)
		{
			int count = source.Length / 6;
			int[] order = new int[count];
			long[] keys = new long[count];
			for(int i = 0; i < count; i++)
			{
				order[i] = i;
				keys[i] = Key(Cell(source[i * 6]), Cell(source[i * 6 + 1]));
			}
			Array.Sort(order, (a, b) => keys[a] != keys[b] ? keys[a].CompareTo(keys[b]) : a.CompareTo(b));

			probes = new float[count * 6];
			for(int i = 0; i < count; i++)
			{
				Array.Copy(source, order[i] * 6, probes, i * 6, 6);
				long key = keys[order[i]];
				int[] cell;
				if(cells.TryGetValue(key, out cell)) cell[1]++;
				else cells.Add(key, new[] { i, 1 });
			}
		}

		#endregion

		#region ================== Methods

		private static long Cell(double v)
		{
			return (long)Math.Floor(v * (1.0 / CELL_SIZE));
		}

		// Cell Y major, then X, so sorting by key sorts as the game does. Coordinates are bounded by
		// LightmapData.MAX_PROBE_COORD, so 32 bits each never collide.
		private static long Key(long x, long y)
		{
			return (y << 32) + x;
		}

		// The colour of the probe the game lights a thing at (x, y, z) from
		public bool TryFind(float x, float y, float z, out Vector3f color)
		{
			color = new Vector3f();
			long cx = Cell(x), cy = Cell(y);
			int found = -1;
			float lastdist = 0f;
			for(long gy = cy - 1; gy <= cy + 1; gy++)
			{
				for(long gx = cx - 1; gx <= cx + 1; gx++)
				{
					int[] cell;
					if(!cells.TryGetValue(Key(gx, gy), out cell)) continue;
					for(int i = cell[0]; i < cell[0] + cell[1]; i++)
					{
						float dx = probes[i * 6] - x;
						float dy = probes[i * 6 + 1] - y;
						float dz = probes[i * 6 + 2] - z;
						float dist = dx * dx + dy * dy + dz * dz;
						if(found < 0 || dist < lastdist)
						{
							found = i;
							lastdist = dist;
						}
					}
				}
			}
			if(found < 0) return false;
			color = new Vector3f(probes[found * 6 + 3], probes[found * 6 + 4], probes[found * 6 + 5]);
			return true;
		}

		#endregion
	}
}
