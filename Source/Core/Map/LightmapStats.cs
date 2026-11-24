#region ================== Namespaces

using System;
using System.IO;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// What a LIGHTMAP lump holds, read without its texels: the Bake Lightmaps window's Output tab
	internal sealed class LightmapStats
	{
		#region ================== Properties

		public int TextureSize { get; private set; }
		public int TextureCount { get; private set; }
		public int SurfaceCount { get; private set; }
		public long LightProbeCount { get; private set; }
		public long CompressedSize { get; private set; }
		public long UsedTexels { get; private set; } // the surfaces' texture coordinate rectangles
		public string PreviewRefusal { get; private set; } // why the Visual-mode preview can't hold it, null when it can

		public long AtlasTexels { get { return (long)TextureCount * TextureSize * TextureSize; } }
		public double Fill { get { return AtlasTexels > 0 ? (double)UsedTexels / AtlasTexels : 0; } }

		#endregion

		#region ================== Constructor

		private LightmapStats() { }

		#endregion

		#region ================== Methods

		// Null with a reason on a malformed lump
		public static LightmapStats Read(byte[] compressed, out string error)
		{
			LightmapLump header = LightmapLump.Read(new MemoryStream(compressed, false), false, out error);
			if(header == null) return null;
			LightmapData layout = LightmapData.ReadLayout(compressed, out error);
			if(layout == null) return null;

			LightmapStats stats = new LightmapStats
			{
				TextureSize = layout.TextureSize,
				TextureCount = layout.TextureCount,
				SurfaceCount = layout.Surfaces.Length,
				LightProbeCount = header.LightProbeCount,
				CompressedSize = compressed.Length,
				PreviewRefusal = LightmapData.TooLarge(header),
			};
			for(int i = 0; i < layout.Surfaces.Length; i++) stats.UsedTexels += SurfaceTexels(layout, i);
			return stats;
		}

		// Texels inside a surface's texture coordinate bounding box, which the baker packs as a rectangle
		public static long SurfaceTexels(LightmapData data, int surface)
		{
			LightmapSurface s = data.Surfaces[surface];
			if(s.Type == LightmapSurfaceType.Null) return 0;
			float minu = float.MaxValue, minv = float.MaxValue, maxu = float.MinValue, maxv = float.MinValue;
			for(int i = s.FirstTexCoord; i < s.FirstTexCoord + s.TexCoordCount; i++)
			{
				float u = data.TexCoords[i * 2], v = data.TexCoords[i * 2 + 1];
				minu = Math.Min(minu, u);
				maxu = Math.Max(maxu, u);
				minv = Math.Min(minv, v);
				maxv = Math.Max(maxv, v);
			}
			// Coordinates are fractions of a page, so a span past 1 is a broken lump, not a larger rectangle
			double width = Math.Min(1.0, maxu - minu) * data.TextureSize, height = Math.Min(1.0, maxv - minv) * data.TextureSize;
			return (long)Math.Round(width) * (long)Math.Round(height);
		}

		#endregion
	}
}
