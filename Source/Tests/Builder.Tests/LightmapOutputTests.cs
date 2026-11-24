using CodeImp.DoomBuilder.Map;
using System.Drawing;

namespace Builder.Tests
{
	public class LightmapOutputTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "lightmap-output-" + Guid.NewGuid().ToString("N"));

		public LightmapOutputTests() { Directory.CreateDirectory(directory); }

		public void Dispose() { Directory.Delete(directory, true); }

		// A wall rectangle 16x16 texels and a flat triangle spanning 8x4, on a 64 page
		private static readonly int[][] SURFACES = { new[] { 1, 0, -1, 0, 0 }, new[] { 5, 0, -1, 1, 4 } };
		private static readonly float[] COORDS =
		{
			0.125f, 0.25f, 0.375f, 0.25f, 0.375f, 0.5f, 0.125f, 0.5f,
			0.5f, 0.5f, 0.625f, 0.5f, 0.5f, 0.5625f,
		};

		// Header, surfaces and coordinates without the texels the header claims
		private static byte[] LayoutOnly(int size, int layers, uint surfaces, uint texcoords, byte[] body)
		{
			var raw = new MemoryStream();
			var w = new BinaryWriter(raw);
			w.Write(0);
			w.Write((ushort)size);
			w.Write((ushort)layers);
			w.Write(surfaces);
			w.Write(texcoords);
			w.Write(0u);
			w.Write(0u);
			w.Write(body);
			return LightmapPreviewDataTests.Zlib(raw.ToArray());
		}

		private static byte[] SurfaceBody()
		{
			var body = new MemoryStream();
			var w = new BinaryWriter(body);
			foreach(int[] s in SURFACES)
			{
				w.Write((uint)s[0]);
				w.Write((uint)s[1]);
				w.Write(0xFFFFFFFFu);
				w.Write((uint)s[3]);
				w.Write((uint)s[4]);
			}
			foreach(float f in COORDS) w.Write(f);
			return body.ToArray();
		}

		[Fact]
		public void StatsCountPagesSurfacesProbesAndUsedTexels()
		{
			byte[] lump = LightmapPreviewDataTests.Lump(64, 2, SURFACES, COORDS, probes: 3);
			LightmapStats stats = LightmapStats.Read(lump, out string error);
			Assert.True(stats != null, error);

			Assert.Equal(64, stats.TextureSize);
			Assert.Equal(2, stats.TextureCount);
			Assert.Equal(2, stats.SurfaceCount);
			Assert.Equal(3L, stats.LightProbeCount);
			Assert.Equal((long)lump.Length, stats.CompressedSize);
			Assert.Equal(16L * 16 + 8 * 4, stats.UsedTexels);
			Assert.Equal((16 * 16 + 8 * 4) / (2.0 * 64 * 64), stats.Fill);
			Assert.Null(stats.PreviewRefusal);
		}

		[Fact]
		public void LargePagesReadForStatsWhileThePreviewRefusesBeforeAllocating()
		{
			// Two 4096 pages would be 200 MB of texels; the lump carries none of them
			byte[] lump = LayoutOnly(4096, 2, 2, 7, SurfaceBody());

			Assert.Null(LightmapData.Read(lump, out string error));
			Assert.Contains("more than", error);

			LightmapStats stats = LightmapStats.Read(lump, out error);
			Assert.True(stats != null, error);
			Assert.Equal(2, stats.TextureCount);
			Assert.Equal(4096, stats.TextureSize);
			Assert.Contains("more than", stats.PreviewRefusal);
			Assert.Null(LightmapData.ReadLayout(lump, out error).Texels);
		}

		[Fact]
		public void LayoutReadRefusesCountsTheDataDoesNotBackUp()
		{
			// A header claiming a million surfaces over two surfaces' worth of data
			Assert.Null(LightmapStats.Read(LayoutOnly(128, 1, 1000000, 7, SurfaceBody()), out string error));
			Assert.Contains("truncated", error);
		}

		[Fact]
		public void AtlasExportWritesOnePngPerPageClampedToFullBrightness()
		{
			// Page 0 alternates half and no light per texel, page 1 is twice full brightness
			int page = 128 * 128 * 3;
			byte[] lump = LightmapPreviewDataTests.Lump(128, 2, SURFACES, COORDS,
				texel: i => i >= page ? (ushort)0x4000 : ((i / 3) % 2 == 0 ? (ushort)0x3800 : (ushort)0));
			string path = Path.Combine(directory, "atlas.png");

			List<string> files = LightmapAtlasExport.Export(lump, path, out string error);
			Assert.True(error == null, error);
			Assert.Equal(new[] { Path.Combine(directory, "atlas_0.png"), Path.Combine(directory, "atlas_1.png") }, files.ToArray());

			using(Bitmap first = new Bitmap(files[0]))
			using(Bitmap second = new Bitmap(files[1]))
			{
				Assert.Equal(128, first.Width);
				Assert.Equal(128, first.Height);
				Assert.Equal(Color.FromArgb(128, 128, 128).ToArgb(), first.GetPixel(0, 0).ToArgb());
				Assert.Equal(Color.Black.ToArgb(), first.GetPixel(1, 0).ToArgb());
				Assert.Equal(Color.White.ToArgb(), second.GetPixel(77, 99).ToArgb());
			}
		}

		[Fact]
		public void AtlasExportOfOnePageUsesTheChosenNameAndATruncatedPageFails()
		{
			string path = Path.Combine(directory, "one.png");
			Assert.Equal(path, LightmapAtlasExport.PagePath(path, 0, 1));

			List<string> files = LightmapAtlasExport.Export(LightmapPreviewDataTests.Lump(64, 1, new[] { SURFACES[0] }, new float[8]), path, out string error);
			Assert.True(error == null, error);
			Assert.Equal(new[] { path }, files.ToArray());

			// Texels for one and a half pages of two
			byte[] body = SurfaceBody();
			byte[] texels = new byte[64 * 64 * 6 * 3 / 2];
			byte[] withtexels = new byte[body.Length + texels.Length];
			body.CopyTo(withtexels, 0);
			files = LightmapAtlasExport.Export(LayoutOnly(64, 2, 2, 7, withtexels), Path.Combine(directory, "cut.png"), out error);
			Assert.Contains("truncated", error);
			Assert.Equal(1, files.Count);
		}
	}
}
