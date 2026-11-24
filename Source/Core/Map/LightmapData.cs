#region ================== Namespaces

using System;
using System.IO;
using System.IO.Compression;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// Values as UZDoom's SurfaceType enum stores them in the lump
	internal enum LightmapSurfaceType
	{
		Null = 0,
		Middle = 1,
		Upper = 2,
		Lower = 3,
		Ceiling = 4,
		Floor = 5
	}

	internal struct LightmapSurface
	{
		public LightmapSurfaceType Type;
		public int TypeIndex; // Baker sidedef for walls, GL subsector for flats
		public int ControlSector; // Baker sector of a 3D floor, -1 for none
		public int Layer;
		public int FirstTexCoord;
		public int TexCoordCount;
	}

	// The whole LIGHTMAP lump: surfaces, texture coordinates and the RGB half-float layers.
	// Light probes are skipped.
	internal sealed class LightmapData
	{
		#region ================== Constants

		// Layers stack into one texture this tall at most
		public const int MAX_ATLAS_HEIGHT = 16384;

		// Half-float RGB texels kept in memory at most (96 MiB)
		public const long MAX_TEXELS = 1L << 24;

		public const int MAX_SURFACES = 1 << 21;
		public const int MAX_TEXCOORDS = 1 << 23;

		private const uint NO_CONTROL_SECTOR = 0xFFFFFFFF;

		#endregion

		#region ================== Properties

		public int TextureSize { get; private set; }
		public int TextureCount { get; private set; }
		public int SubsectorCount { get; private set; }
		public LightmapSurface[] Surfaces { get; private set; }
		public float[] TexCoords { get; private set; } // u, v pairs
		public ushort[] Texels { get; private set; } // RGB half floats, layer after layer, row after row

		#endregion

		#region ================== Constructor

		private LightmapData() { }

		#endregion

		#region ================== Methods

		public Vector2D GetTexCoord(int index)
		{
			return new Vector2D(TexCoords[index * 2], TexCoords[index * 2 + 1]);
		}

		// Reads the compressed lump. Returns null with a reason on anything malformed.
		public static LightmapData Read(byte[] compressed, out string error)
		{
			// The header's limits first, then a pass that inflates without keeping anything, so the
			// allocations below are backed by real data
			LightmapLump header = LightmapLump.Read(new MemoryStream(compressed, false), false, out error);
			if(header == null) return null;
			if(header.IsEmpty)
			{
				error = "the lightmap is empty";
				return null;
			}
			if((long)header.TextureCount * header.TextureSize > MAX_ATLAS_HEIGHT)
			{
				error = header.TextureCount + " layers of " + header.TextureSize + " are taller than " + MAX_ATLAS_HEIGHT + " texels";
				return null;
			}
			if((long)header.TextureCount * header.TextureSize * header.TextureSize > MAX_TEXELS)
			{
				error = "the lightmap has more than " + MAX_TEXELS + " texels";
				return null;
			}
			if(header.SurfaceCount > MAX_SURFACES || header.TexCoordCount > MAX_TEXCOORDS)
			{
				error = "too many surfaces (" + header.SurfaceCount + ") or texture coordinates (" + header.TexCoordCount + ")";
				return null;
			}
			if(LightmapLump.Read(new MemoryStream(compressed, false), true, out error) == null) return null;

			LightmapData data = new LightmapData
			{
				TextureSize = header.TextureSize,
				TextureCount = header.TextureCount,
				SubsectorCount = (int)Math.Min(header.SubsectorCount, int.MaxValue),
			};

			try
			{
				MemoryStream stream = new MemoryStream(compressed, false);
				stream.Position = 2; // zlib header, checked by the first pass
				using(DeflateStream inflater = new DeflateStream(stream, CompressionMode.Decompress))
				using(BinaryReader reader = new BinaryReader(inflater))
				{
					Skip(reader, LightmapLump.HEADER_SIZE + header.LightProbeCount * 24L);

					int count = (int)header.SurfaceCount;
					data.Surfaces = new LightmapSurface[count];
					for(int i = 0; i < count; i++)
					{
						uint type = reader.ReadUInt32();
						uint typeindex = reader.ReadUInt32();
						uint control = reader.ReadUInt32();
						uint layer = reader.ReadUInt32();
						uint first = reader.ReadUInt32();
						if(type > (uint)LightmapSurfaceType.Floor) return Fail("surface " + i + " has unknown type " + type, out error);
						if(layer >= header.TextureCount) return Fail("surface " + i + " uses layer " + layer + " of " + header.TextureCount, out error);
						if(first >= header.TexCoordCount) return Fail("surface " + i + " starts past the texture coordinates", out error);
						if(typeindex > int.MaxValue || (control != NO_CONTROL_SECTOR && control > int.MaxValue))
							return Fail("surface " + i + " has an index out of range", out error);
						data.Surfaces[i] = new LightmapSurface
						{
							Type = (LightmapSurfaceType)type,
							TypeIndex = (int)typeindex,
							ControlSector = control == NO_CONTROL_SECTOR ? -1 : (int)control,
							Layer = (int)layer,
							FirstTexCoord = (int)first,
						};
					}

					// A surface's coordinates run up to the next surface's first one
					for(int i = 0; i < count; i++)
					{
						int end = i + 1 < count ? data.Surfaces[i + 1].FirstTexCoord : (int)header.TexCoordCount;
						int n = end - data.Surfaces[i].FirstTexCoord;
						if(n <= 0) return Fail("surface " + (i + 1) + " does not start after surface " + i, out error);
						LightmapSurfaceType t = data.Surfaces[i].Type;
						bool wall = t == LightmapSurfaceType.Middle || t == LightmapSurfaceType.Upper || t == LightmapSurfaceType.Lower;
						if(wall && n != 4) return Fail("wall surface " + i + " has " + n + " texture coordinates", out error);
						if(!wall && t != LightmapSurfaceType.Null && n < 3) return Fail("flat surface " + i + " has " + n + " texture coordinates", out error);
						data.Surfaces[i].TexCoordCount = n;
					}

					data.TexCoords = new float[header.TexCoordCount * 2];
					for(int i = 0; i < data.TexCoords.Length; i++)
					{
						float f = reader.ReadSingle();
						if(float.IsNaN(f) || float.IsInfinity(f)) return Fail("texture coordinate " + (i / 2) + " is not finite", out error);
						data.TexCoords[i] = f;
					}

					data.Texels = new ushort[(long)header.TextureCount * header.TextureSize * header.TextureSize * 3];
					byte[] buffer = new byte[65536];
					int pos = 0;
					while(pos < data.Texels.Length)
					{
						int want = Math.Min(buffer.Length, (data.Texels.Length - pos) * 2);
						int got = ReadFully(reader, buffer, want);
						if(got < want) return Fail("truncated texture data", out error);
						Buffer.BlockCopy(buffer, 0, data.Texels, pos * 2, got);
						pos += got / 2;
					}
				}
			}
			catch(EndOfStreamException)
			{
				return Fail("truncated data", out error);
			}
			catch(IOException e)
			{
				return Fail("corrupt data: " + e.Message, out error);
			}
			catch(InvalidDataException e)
			{
				return Fail("corrupt data: " + e.Message, out error);
			}

			return data;
		}

		// Checks the surface indices against the map the lump belongs to
		public bool Validate(int sidedefcount, int subsectorcount, int sectorcount, out string error)
		{
			for(int i = 0; i < Surfaces.Length; i++)
			{
				LightmapSurface s = Surfaces[i];
				if(s.Type == LightmapSurfaceType.Null) continue;
				bool flat = s.Type == LightmapSurfaceType.Floor || s.Type == LightmapSurfaceType.Ceiling;
				int limit = flat ? subsectorcount : sidedefcount;
				if(s.TypeIndex >= limit)
				{
					error = "surface " + i + " refers to " + (flat ? "subsector " : "sidedef ") + s.TypeIndex + " of " + limit;
					return false;
				}
				if(s.ControlSector >= sectorcount)
				{
					error = "surface " + i + " refers to sector " + s.ControlSector + " of " + sectorcount;
					return false;
				}
			}
			error = null;
			return true;
		}

		// One layer as float RGBA rows, the format the renderer uploads
		public float[] GetLayerRgba(int layer)
		{
			int texels = TextureSize * TextureSize;
			float[] result = new float[texels * 4];
			int src = layer * texels * 3;
			float[] table = HalfTable;
			for(int i = 0; i < texels; i++)
			{
				result[i * 4] = table[Texels[src++]];
				result[i * 4 + 1] = table[Texels[src++]];
				result[i * 4 + 2] = table[Texels[src++]];
				result[i * 4 + 3] = 1f;
			}
			return result;
		}

		private static float[] halftable;
		private static float[] HalfTable
		{
			get
			{
				if(halftable == null)
				{
					float[] t = new float[65536];
					for(int i = 0; i < t.Length; i++) t[i] = HalfToFloat((ushort)i);
					halftable = t;
				}
				return halftable;
			}
		}

		public static float HalfToFloat(ushort h)
		{
			int sign = (h >> 15) & 1;
			int exponent = (h >> 10) & 0x1F;
			int mantissa = h & 0x3FF;
			float value;
			if(exponent == 0) value = mantissa * (1f / (1 << 24));
			else if(exponent == 31) value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
			else value = (1f + mantissa / 1024f) * (float)Math.Pow(2, exponent - 15);
			return sign == 1 ? -value : value;
		}

		private static LightmapData Fail(string reason, out string error)
		{
			error = reason;
			return null;
		}

		private static void Skip(BinaryReader reader, long count)
		{
			byte[] buffer = new byte[65536];
			while(count > 0)
			{
				int n = reader.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
				if(n <= 0) throw new EndOfStreamException();
				count -= n;
			}
		}

		// Reads through the BinaryReader so its buffering stays consistent
		private static int ReadFully(BinaryReader reader, byte[] buffer, int length)
		{
			int total = 0, n;
			while(total < length && (n = reader.Read(buffer, total, length - total)) > 0) total += n;
			return total;
		}

		#endregion
	}
}
