#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	public enum LightmapWallPart
	{
		Upper,
		Middle,
		Lower
	}

	// Lightmap coordinates of one wall part as an affine function of the position along the side
	// (t, 0 at the side's own start) and the height. Exact because the baker projects each surface
	// along a world axis.
	public struct WallLightmap
	{
		public double U0, Ut, Uz, V0, Vt, Vz;
		public int Layer;

		public double GetU(double t, double z) { return U0 + Ut * t + Uz * z; }
		public double GetV(double t, double z) { return V0 + Vt * t + Vz * z; }

		// corners: bottom left, top left, top right, bottom right (the lump's order), heights matching.
		// Returns false when the corners don't span an area.
		public static bool Fit(Vector2D[] corners, double bottomleft, double topleft, double topright, double bottomright, int layer, out WallLightmap result)
		{
			result = new WallLightmap { Layer = layer };
			double[] t = { 0, 0, 1, 1 };
			double[] z = { bottomleft, topleft, topright, bottomright };

			// Least squares over the 4 corners: rows (1, t, z)
			double s1 = 4, st = 0, sz = 0, stt = 0, stz = 0, szz = 0;
			for(int i = 0; i < 4; i++)
			{
				st += t[i]; sz += z[i]; stt += t[i] * t[i]; stz += t[i] * z[i]; szz += z[i] * z[i];
			}
			double det = s1 * (stt * szz - stz * stz) - st * (st * szz - stz * sz) + sz * (st * stz - stt * sz);
			if(Math.Abs(det) < 1e-9) return false;

			double[] coef = new double[3];
			for(int axis = 0; axis < 2; axis++)
			{
				double b0 = 0, bt = 0, bz = 0;
				for(int i = 0; i < 4; i++)
				{
					double w = axis == 0 ? corners[i].x : corners[i].y;
					b0 += w; bt += t[i] * w; bz += z[i] * w;
				}
				// Cramer's rule
				coef[0] = (b0 * (stt * szz - stz * stz) - st * (bt * szz - stz * bz) + sz * (bt * stz - stt * bz)) / det;
				coef[1] = (s1 * (bt * szz - stz * bz) - b0 * (st * szz - stz * sz) + sz * (st * bz - bt * sz)) / det;
				coef[2] = (s1 * (stt * bz - stz * bt) - st * (st * bz - bt * sz) + b0 * (st * stz - stt * sz)) / det;
				if(axis == 0) { result.U0 = coef[0]; result.Ut = coef[1]; result.Uz = coef[2]; }
				else { result.V0 = coef[0]; result.Vt = coef[1]; result.Vz = coef[2]; }
			}
			return true;
		}
	}

	// Triangles of one flat built from GL subsectors, clockwise as Sector.Triangles' are for a floor
	public sealed class LightmapFlat
	{
		public Vector2D[] Positions; // 3 per triangle
		public float[] Lightmap; // u, v, layer + 1 per position; 0, 0, 0 where the subsector has no lightmap
	}

	// A baked lightmap mapped onto the editor's map: wall surfaces by sidedef, flats by sector.
	public sealed class LightmapPreview
	{
		#region ================== Variables

		private readonly LightmapData data;
		private readonly Dictionary<long, int> walls = new Dictionary<long, int>(); // (sidedef, part, control) to surface
		private readonly Dictionary<long, int> flats = new Dictionary<long, int>(); // (subsector, type, control) to surface
		private readonly Dictionary<int, List<int>> sectorsubsectors = new Dictionary<int, List<int>>();
		private readonly HashSet<int> litsectors = new HashSet<int>();
		private Vector2D[][] polygons;

		#endregion

		#region ================== Properties

		public int TextureSize { get { return data.TextureSize; } }
		public int LayerCount { get { return data.TextureCount; } }
		public string Source { get; internal set; }
		public LightmapProbeGrid Probes { get; private set; } // null when the bake has none or they were dropped

		#endregion

		#region ================== Constructor

		private LightmapPreview(LightmapData data)
		{
			this.data = data;
		}

		#endregion

		#region ================== Building

		// Returns null with a reason when the bake does not belong to the map
		internal static LightmapPreview Build(LightmapData data, GlSubsectors gl, MapTopology baker, MapTopology map, out string error)
		{
			if(gl.OriginalVertexCount != baker.Vertices.Length)
			{
				error = "the nodes were built for " + gl.OriginalVertexCount + " vertices, the baked map has " + baker.Vertices.Length;
				return null;
			}
			if(!data.Validate(baker.SideSector.Length, gl.Count, baker.SectorCount, out error)) return null;
			BakerIndexMap index = BakerIndexMap.Match(baker, map, out error);
			if(index == null) return null;

			LightmapPreview preview = new LightmapPreview(data);

			// Subsector polygons and their sectors
			int[] subsectorsector = new int[gl.Count];
			preview.polygons = new Vector2D[gl.Count][];
			for(int i = 0; i < gl.Count; i++)
			{
				subsectorsector[i] = -1;
				Vector2D[] poly = new Vector2D[gl.SegCount[i]];
				for(int j = 0; j < poly.Length; j++)
				{
					int seg = gl.FirstSeg[i] + j;
					poly[j] = gl.GetVertex(gl.SegVertex[seg], baker.Vertices);
					int line = gl.SegLine[seg];
					if(subsectorsector[i] != -1 || line < 0) continue;
					if(line >= baker.LineStart.Length)
					{
						error = "seg " + seg + " uses linedef " + line + " of " + baker.LineStart.Length;
						return null;
					}
					int side = gl.SegBackSide[seg] ? baker.LineBack[line] : baker.LineFront[line];
					if(side < 0) continue;
					int sector = index.Sector[baker.SideSector[side]];
					if(sector < 0)
					{
						error = "subsector " + i + " has no sector in the map";
						return null;
					}
					subsectorsector[i] = sector;
				}
				preview.polygons[i] = poly;
				if(subsectorsector[i] >= 0 && poly.Length >= 3)
				{
					List<int> list;
					if(!preview.sectorsubsectors.TryGetValue(subsectorsector[i], out list))
						preview.sectorsubsectors.Add(subsectorsector[i], list = new List<int>());
					list.Add(i);
				}
			}

			for(int i = 0; i < data.Surfaces.Length; i++)
			{
				LightmapSurface s = data.Surfaces[i];
				if(s.Type == LightmapSurfaceType.Null) continue;
				int control = -1;
				if(s.ControlSector >= 0)
				{
					control = index.Sector[s.ControlSector];
					if(control < 0)
					{
						error = "surface " + i + " uses a 3D floor sector that has no match in the map";
						return null;
					}
				}

				if(s.Type == LightmapSurfaceType.Floor || s.Type == LightmapSurfaceType.Ceiling)
				{
					if(s.TexCoordCount != gl.SegCount[s.TypeIndex])
					{
						error = "flat surface " + i + " has " + s.TexCoordCount + " texture coordinates for " + gl.SegCount[s.TypeIndex] + " segs";
						return null;
					}
					preview.flats[Key(s.TypeIndex, (int)s.Type, control)] = i;
					if(subsectorsector[s.TypeIndex] >= 0) preview.litsectors.Add(subsectorsector[s.TypeIndex]);
				}
				else
				{
					int side = index.Sidedef[s.TypeIndex];
					if(side < 0)
					{
						error = "surface " + i + " uses a sidedef no line uses";
						return null;
					}
					preview.walls[Key(side, (int)s.Type, control)] = i;
				}
			}

			if(data.Probes != null && data.Probes.Length > 0) preview.Probes = new LightmapProbeGrid(data.Probes);
			error = null;
			return preview;
		}

		private static long Key(int element, int type, int control)
		{
			return ((long)element << 32) | ((long)type << 28) | (uint)(control + 1);
		}

		#endregion

		#region ================== Queries

		// The lump's corner coordinates of a wall part (bottom left, top left, top right, bottom right as the
		// keyed side sees them), or null. A 3D floor's wall is keyed on the side facing into the sector that
		// holds the floor, and its corners then run in the drawn (opposite) side's direction.
		public Vector2D[] GetWallCorners(int sidedef, LightmapWallPart part, int controlsector, out int layer)
		{
			layer = -1;
			LightmapSurfaceType type = part == LightmapWallPart.Upper ? LightmapSurfaceType.Upper
				: part == LightmapWallPart.Lower ? LightmapSurfaceType.Lower : LightmapSurfaceType.Middle;
			int surface;
			if(!walls.TryGetValue(Key(sidedef, (int)type, controlsector), out surface)) return null;
			LightmapSurface s = data.Surfaces[surface];
			layer = s.Layer;
			Vector2D[] corners = new Vector2D[4];
			for(int i = 0; i < 4; i++) corners[i] = data.GetTexCoord(s.FirstTexCoord + i);
			return corners;
		}

		// Whether a sector's flats are drawn from subsectors (it has at least one flat surface)
		public bool HasFlats(int sector)
		{
			return litsectors.Contains(sector);
		}

		// Triangles for a Visual mode floor or ceiling of a sector, or null when the sector has no flat
		// surfaces. For a 3D floor (controlsector set) the Visual mode ceiling is the floor's top, which
		// the baker calls its floor, and the other way round.
		public LightmapFlat GetFlat(int sector, bool visualceiling, int controlsector)
		{
			List<int> subsectors;
			if(!litsectors.Contains(sector) || !sectorsubsectors.TryGetValue(sector, out subsectors)) return null;
			LightmapSurfaceType type = (controlsector < 0) == visualceiling ? LightmapSurfaceType.Ceiling : LightmapSurfaceType.Floor;

			List<Vector2D> positions = new List<Vector2D>();
			List<float> lightmap = new List<float>();
			foreach(int sub in subsectors)
			{
				Vector2D[] poly = polygons[sub];
				int surface;
				bool lit = flats.TryGetValue(Key(sub, (int)type, controlsector), out surface);
				LightmapSurface s = lit ? data.Surfaces[surface] : new LightmapSurface();
				for(int k = 2; k < poly.Length; k++)
				{
					foreach(int j in new[] { 0, k - 1, k })
					{
						positions.Add(poly[j]);
						if(lit)
						{
							Vector2D uv = data.GetTexCoord(s.FirstTexCoord + j);
							lightmap.Add((float)uv.x);
							lightmap.Add((float)uv.y);
							lightmap.Add(s.Layer + 1);
						}
						else
						{
							lightmap.Add(0f); lightmap.Add(0f); lightmap.Add(0f);
						}
					}
				}
			}
			return new LightmapFlat { Positions = positions.ToArray(), Lightmap = lightmap.ToArray() };
		}

		// The atlas: layers stacked vertically, float RGBA rows, one layer at a time
		public float[] GetLayerRgba(int layer)
		{
			return data.GetLayerRgba(layer);
		}

		#endregion
	}
}
