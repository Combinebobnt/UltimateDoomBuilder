using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Rendering;
using System.IO.Compression;

namespace Builder.Tests
{
	public class LightmapProbeTests
	{
		private static readonly int[][] SURFACES = { new[] { 1, 0, -1, 0, 0 }, new[] { 5, 0, -1, 0, 4 } };

		private static float[] Coords()
		{
			return new float[14];
		}

		private static LightmapData Read(float[] probes)
		{
			LightmapData data = LightmapData.Read(LightmapPreviewDataTests.Lump(4, 1, SURFACES, Coords(), probeValues: probes), out string error);
			Assert.True(data != null, error);
			return data;
		}

		// r carries the probe's id so a lookup names the probe it found
		private static float[] Probes(params float[][] xyz)
		{
			float[] result = new float[xyz.Length * 6];
			for(int i = 0; i < xyz.Length; i++)
			{
				result[i * 6] = xyz[i][0];
				result[i * 6 + 1] = xyz[i][1];
				result[i * 6 + 2] = xyz[i][2];
				result[i * 6 + 3] = i + 1;
				result[i * 6 + 4] = 0.25f;
				result[i * 6 + 5] = 0.5f;
			}
			return result;
		}

		private static int Find(LightmapProbeGrid grid, float x, float y, float z)
		{
			Vector3f c;
			return grid.TryFind(x, y, z, out c) ? (int)c.X : 0;
		}

		[Fact]
		public void ProbesRoundTripAndKeepTheSurfaces()
		{
			float[] probes = Probes(new[] { 16f, -8f, 40f }, new[] { 100f, 200f, 0.5f });
			LightmapData data = Read(probes);
			Assert.Null(data.ProbeError);
			Assert.Equal(probes, data.Probes);
			Assert.Equal(2, data.Surfaces.Length);
			Assert.Null(LightmapData.ReadLayout(LightmapPreviewDataTests.Lump(4, 1, SURFACES, Coords(), probeValues: probes), out string error).Probes);
		}

		[Fact]
		public void BadProbesAreDroppedWhileTheSurfacesLoad()
		{
			float[] nan = Probes(new[] { 0f, 0f, 0f }, new[] { 1f, 1f, 1f });
			nan[10] = float.PositiveInfinity;
			float[] far = Probes(new[] { 0f, 0f, 0f }, new[] { 1f, -LightmapData.MAX_PROBE_COORD * 2, 1f });
			foreach(float[] bad in new[] { nan, far })
			{
				LightmapData data = Read(bad);
				Assert.NotNull(data.ProbeError);
				Assert.Contains("probe 1", data.ProbeError);
				Assert.Empty(data.Probes);
				Assert.Equal(2, data.Surfaces.Length);
			}
		}

		[Fact]
		public void TooManyProbesAreDroppedWhileTheSurfacesLoad()
		{
			// Streamed zeros, so the over-cap count is backed by real data without holding it in memory
			long count = LightmapData.MAX_PROBES + 1L;
			var output = new MemoryStream();
			output.WriteByte(0x78);
			output.WriteByte(0x9C);
			using(var deflate = new DeflateStream(output, CompressionMode.Compress, true))
			{
				var w = new BinaryWriter(deflate);
				w.Write(0); w.Write((ushort)4); w.Write((ushort)1);
				w.Write((uint)SURFACES.Length); w.Write((uint)7); w.Write((uint)count); w.Write((uint)0);
				byte[] zeros = new byte[1 << 20];
				for(long left = count * 24; left > 0; left -= zeros.Length) w.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
				foreach(int[] s in SURFACES) { w.Write((uint)s[0]); w.Write((uint)s[1]); w.Write(0xFFFFFFFFu); w.Write((uint)s[3]); w.Write((uint)s[4]); }
				for(int i = 0; i < 14; i++) w.Write(0f);
				for(int i = 0; i < 4 * 4 * 3; i++) w.Write((ushort)0x3C00);
				w.Flush();
			}
			output.Write(new byte[4], 0, 4);

			LightmapData data = LightmapData.Read(output.ToArray(), out string error);
			Assert.True(data != null, error);
			Assert.Contains("more than", data.ProbeError);
			Assert.Empty(data.Probes);
			Assert.Equal(2, data.Surfaces.Length);
		}

		[Fact]
		public void LookupFindsTheNearestProbeInTheThreeByThreeCells()
		{
			var grid = new LightmapProbeGrid(Probes(
				new[] { 10f, 10f, 0f },   // 1: own cell (0, 0)
				new[] { 20f, 10f, 0f },   // 2: own cell, nearer to x 18
				new[] { 33f, 10f, 0f },   // 3: cell (1, 0), nearest to x 30
				new[] { 100f, 10f, 0f })); // 4: cell (3, 0), two cells from x 30
			Assert.Equal(4, grid.Count);
			Assert.Equal(2, Find(grid, 18, 10, 0));
			Assert.Equal(1, Find(grid, 11, 10, 0));
			Assert.Equal(3, Find(grid, 30, 10, 0)); // the neighbour cell beats the own cell
			Assert.Equal(4, Find(grid, 95, 10, 0));
			Assert.Equal(0, Find(grid, 200, 10, 0)); // no probe within the 3x3 cells
			Assert.Equal(0, Find(grid, 18, 200, 0));
			Vector3f c;
			Assert.True(grid.TryFind(18, 10, 0, out c));
			Assert.Equal(0.25f, c.Y);
			Assert.Equal(0.5f, c.Z);
		}

		[Fact]
		public void ProbesTwoCellsAwayAreOutOfReach()
		{
			// 1 is in cell 2, 2 in cell -1: from x 31.9 (cell 0) the nearer 1 is out of reach
			var grid = new LightmapProbeGrid(Probes(new[] { 64f, 0f, 0f }, new[] { -20f, 0f, 0f }));
			Assert.Equal(2, Find(grid, 31.9f, 0, 0));
			Assert.Equal(1, Find(grid, 32f, 0, 0));
		}

		[Fact]
		public void NegativeCoordinatesFloorOntoTheirCells()
		{
			// Probe 1 sits in cell (-1, -1), probe 2 in (-2, 0): floored, not truncated toward zero
			var grid = new LightmapProbeGrid(Probes(new[] { -32f, -0.5f, 0f }, new[] { -32.01f, 0f, 0f }));
			Assert.Equal(1, Find(grid, 0f, 0f, 0f));
			Assert.Equal(0, Find(grid, 0f, 32f, 0f));
			Assert.Equal(0, Find(grid, 32f, 0f, 0f));
			Assert.Equal(2, Find(grid, -64f, 0f, 0f)); // both in reach, 2 nearer
		}

		[Fact]
		public void EmptyGridFindsNothing()
		{
			var grid = new LightmapProbeGrid(new float[0]);
			Assert.Equal(0, grid.Count);
			Assert.Equal(0, Find(grid, 0, 0, 0));
		}

		[Fact]
		public void CrossCellTiesGoToTheFirstCellScanned()
		{
			// Equal distances from (32, 32): the scan runs cell Y outer, X inner, ascending, strict <
			var grid = new LightmapProbeGrid(Probes(
				new[] { 40f, 40f, 0f },  // 1: cell (1, 1)
				new[] { 24f, 40f, 0f },  // 2: cell (0, 1)
				new[] { 40f, 24f, 0f })); // 3: cell (1, 0), scanned first
			Assert.Equal(3, Find(grid, 32, 32, 0));
		}
	}
}
