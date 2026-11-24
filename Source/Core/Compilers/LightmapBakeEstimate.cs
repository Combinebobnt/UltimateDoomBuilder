#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Map;

#endregion

namespace CodeImp.DoomBuilder.Compilers
{
	// One side of a line as the estimate sees it: X1,Y1 to X2,Y2 runs with its sector on the right
	internal struct LightmapEstimateSide
	{
		public double X1, Y1, X2, Y2;
		public int Sector;
		public int Other;           // the sector across the line, -1 for a one-sided line
		public bool Upper, Lower;   // the part has a texture, so the baker lights it
		public int Distance;        // lm_sampledist, 0 when unset
		public int Top, Mid, Bottom; // lm_sampledist_top/_mid/_bot, 0 when unset
	}

	internal struct LightmapEstimateSector
	{
		public double Floor, Ceiling;
		public int FloorDistance, CeilingDistance; // lm_sampledist_floor/_ceiling, 0 when unset
	}

	// How long a bake will take: ZDRay's traced texel count estimated from the map, times a per texel
	// cost learned from this machine's earlier bakes
	internal static class LightmapBakeEstimate
	{
		public const double CONFIRM_SECONDS = 60;
		public const int DEFAULT_MAP_DISTANCE = 8;

		// The ZDRayInfo lm_sampledistance as the baker reads it
		public static int MapDistance(int value)
		{
			return RoundPowerOfTwo(Math.Max(8, Math.Min(value, 128)));
		}

		// A surface's texel spacing: its own distance or the map's, scaled, then rounded up to a power of two
		public static int SampleDimension(int distance, int mapdistance, float scale)
		{
			int d = (distance != 0 ? distance : mapdistance);
			if(scale != 1.0f) d = (int)Math.Round(d * scale, MidpointRounding.AwayFromZero);
			return RoundPowerOfTwo(Math.Max(1, d));
		}

		private static int RoundPowerOfTwo(int x)
		{
			int mask = 1;
			while(mask < x && mask < 0x40000000) mask <<= 1;
			return mask;
		}

		// A surface's block: its extent snapped outward to the texel grid plus a texel of border on each
		// side, so on average three texels more than extent / spacing along each axis. For a w x h
		// rectangle that is (w/d + 3)(h/d + 3) = area/d^2 + 1.5 perimeter/d + 9, which also serves a
		// sector's flats. The baker clamps a surface to a page less its border.
		private static double Block(double area, double perimeter, int d, int texturesize)
		{
			double texels = area / ((double)d * d) + 1.5 * perimeter / d + 9;
			double page = (double)(texturesize - 2) * (texturesize - 2);
			return Math.Min(texels, page);
		}

		private static double Wall(double length, double height, int d, int texturesize)
		{
			if(height <= 0) return 0;
			return Block(length * height, 2 * (length + height), d, texturesize);
		}

		// The surfaces ZDRay lights on the map: one-sided middles, textured uppers and lowers, and every
		// sector's floor and ceiling. 3D floors and slopes are left out.
		public static long Texels(IList<LightmapEstimateSide> sides, IList<LightmapEstimateSector> sectors, int mapdistance, float scale, int texturesize)
		{
			double total = 0;
			double[] area = new double[sectors.Count];
			double[] perimeter = new double[sectors.Count];
			bool[] used = new bool[sectors.Count];

			foreach(LightmapEstimateSide s in sides)
			{
				if(s.Sector < 0 || s.Sector >= sectors.Count) continue;
				double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
				double length = Math.Sqrt(dx * dx + dy * dy);
				if(length <= 0) continue; // the baker removes zero length lines

				used[s.Sector] = true;
				area[s.Sector] += s.X1 * s.Y2 - s.X2 * s.Y1;
				perimeter[s.Sector] += length;

				// A wall's block spans its extent on the axis it is mapped along
				double extent = Math.Max(Math.Abs(dx), Math.Abs(dy));
				LightmapEstimateSector own = sectors[s.Sector];
				if(s.Other < 0 || s.Other >= sectors.Count)
				{
					total += Wall(extent, own.Ceiling - own.Floor, SampleDimension(Part(s.Mid, s.Distance), mapdistance, scale), texturesize);
					continue;
				}
				LightmapEstimateSector other = sectors[s.Other];
				if(s.Lower && own.Floor < other.Floor)
					total += Wall(extent, other.Floor - own.Floor, SampleDimension(Part(s.Bottom, s.Distance), mapdistance, scale), texturesize);
				if(s.Upper && own.Ceiling > other.Ceiling)
					total += Wall(extent, own.Ceiling - other.Ceiling, SampleDimension(Part(s.Top, s.Distance), mapdistance, scale), texturesize);
			}

			for(int i = 0; i < sectors.Count; i++)
			{
				if(!used[i]) continue;
				double a = Math.Abs(area[i]) / 2;
				total += Block(a, perimeter[i], SampleDimension(sectors[i].FloorDistance, mapdistance, scale), texturesize);
				total += Block(a, perimeter[i], SampleDimension(sectors[i].CeilingDistance, mapdistance, scale), texturesize);
			}
			return (long)Math.Round(total);
		}

		private static int Part(int part, int side) { return (part != 0 ? part : side); }

		// The estimate's view of a map
		public static void Read(MapSet map, out List<LightmapEstimateSide> sides, out List<LightmapEstimateSector> sectors)
		{
			sectors = new List<LightmapEstimateSector>(map.Sectors.Count);
			Dictionary<Sector, int> index = new Dictionary<Sector, int>(map.Sectors.Count);
			foreach(Sector s in map.Sectors)
			{
				index[s] = sectors.Count;
				sectors.Add(new LightmapEstimateSector
				{
					Floor = s.FloorHeight,
					Ceiling = s.CeilHeight,
					FloorDistance = UniFields.GetInteger(s.Fields, "lm_sampledist_floor"),
					CeilingDistance = UniFields.GetInteger(s.Fields, "lm_sampledist_ceiling"),
				});
			}

			sides = new List<LightmapEstimateSide>(map.Sidedefs.Count);
			foreach(Sidedef sd in map.Sidedefs)
			{
				int sector, other = -1;
				if(sd.Line == null || sd.Sector == null || !index.TryGetValue(sd.Sector, out sector)) continue;
				Sidedef back = sd.Other;
				if(back != null && back.Sector != null && !index.TryGetValue(back.Sector, out other)) other = -1;
				bool front = sd.IsFront;
				Vertex a = (front ? sd.Line.Start : sd.Line.End), b = (front ? sd.Line.End : sd.Line.Start);
				sides.Add(new LightmapEstimateSide
				{
					X1 = a.Position.x, Y1 = a.Position.y, X2 = b.Position.x, Y2 = b.Position.y,
					Sector = sector,
					Other = other,
					Upper = HasTexture(sd.HighTexture),
					Lower = HasTexture(sd.LowTexture),
					Distance = UniFields.GetInteger(sd.Fields, "lm_sampledist"),
					Top = UniFields.GetInteger(sd.Fields, "lm_sampledist_top"),
					Mid = UniFields.GetInteger(sd.Fields, "lm_sampledist_mid"),
					Bottom = UniFields.GetInteger(sd.Fields, "lm_sampledist_bot"),
				});
			}
		}

		private static bool HasTexture(string name) { return !string.IsNullOrEmpty(name) && name[0] != '-'; }

		// Seconds for texels at a cost in thread seconds per texel; negative when there is no cost yet
		public static double Seconds(long texels, double cost, int threads)
		{
			if(cost <= 0 || texels <= 0) return -1;
			return texels * cost / Math.Max(1, threads);
		}

		// The cost after a CPU trace of traced texels (light probes included) that took seconds on threads.
		// The first measurement sets it, later ones move it halfway, so one odd bake can't dominate.
		public static double Calibrate(double cost, long traced, double seconds, int threads)
		{
			if(traced <= 0 || seconds <= 0 || threads <= 0) return cost;
			double measured = seconds * threads / traced;
			return (cost > 0 ? (cost + measured) / 2 : measured);
		}

		public static string Describe(double seconds)
		{
			if(seconds < 0) return null;
			if(seconds < 1) return "under a second";
			if(seconds < 90) return "about " + Math.Round(seconds) + " s";
			if(seconds < 5400) return "about " + Math.Round(seconds / 60) + " min";
			return "about " + (seconds / 3600).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " h";
		}
	}
}
