using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Map;
using System.IO.Compression;
using System.Text;

namespace Builder.Tests
{
	public class LightmapPreviewDataTests
	{
		#region ================== Lump builders

		// type, typeIndex, controlSector (-1 none), layer, firstTexCoord
		private static byte[] Lump(int size, int layers, int[][] surfaces, float[] texcoords, int probes = 0, int subsectors = 0, int version = 0)
		{
			var body = new MemoryStream();
			var w = new BinaryWriter(body);
			w.Write(version);
			w.Write((ushort)size);
			w.Write((ushort)layers);
			w.Write((uint)surfaces.Length);
			w.Write((uint)(texcoords.Length / 2));
			w.Write((uint)probes);
			w.Write((uint)subsectors);
			for(int i = 0; i < probes * 24; i++) w.Write((byte)0);
			foreach(int[] s in surfaces)
			{
				w.Write((uint)s[0]);
				w.Write((uint)s[1]);
				w.Write(s[2] < 0 ? 0xFFFFFFFFu : (uint)s[2]);
				w.Write((uint)s[3]);
				w.Write((uint)s[4]);
			}
			foreach(float f in texcoords) w.Write(f);
			// Texel i of the whole data holds half(i % 2048) so layers differ
			for(int i = 0; i < layers * size * size * 3; i++) w.Write((ushort)(0x3C00 + (i % 7)));
			return Zlib(body.ToArray());
		}

		private static byte[] Zlib(byte[] raw)
		{
			var output = new MemoryStream();
			output.WriteByte(0x78);
			output.WriteByte(0x9C);
			using(var deflate = new DeflateStream(output, CompressionMode.Compress, true)) deflate.Write(raw, 0, raw.Length);
			output.Write(new byte[4], 0, 4);
			return output.ToArray();
		}

		private static float[] Coords(int count, float start)
		{
			var result = new float[count * 2];
			for(int i = 0; i < result.Length; i++) result[i] = start + i * 0.01f;
			return result;
		}

		private static LightmapData ReadOk(byte[] lump)
		{
			LightmapData data = LightmapData.Read(lump, out string error);
			Assert.True(data != null, error);
			return data;
		}

		private static string ReadFails(byte[] lump)
		{
			LightmapData data = LightmapData.Read(lump, out string error);
			Assert.Null(data);
			Assert.NotNull(error);
			return error;
		}

		#endregion

		#region ================== Lump

		[Fact]
		public void LumpRoundTripsSurfacesCoordinatesAndLayers()
		{
			int[][] surfaces = {
				new[] { 1, 0, -1, 0, 0 },  // middle wall
				new[] { 2, 1, -1, 1, 4 },  // upper wall, layer 1
				new[] { 5, 0, -1, 0, 8 },  // floor, 3 segs
				new[] { 4, 1, -1, 1, 11 }, // ceiling, 4 segs
				new[] { 1, 2, 3, 0, 15 },  // 3D floor wall, control sector 3
			};
			LightmapData data = ReadOk(Lump(4, 2, surfaces, Coords(19, 0.5f), probes: 2, subsectors: 2));

			Assert.Equal(4, data.TextureSize);
			Assert.Equal(2, data.TextureCount);
			Assert.Equal(5, data.Surfaces.Length);
			int[] counts = { 4, 4, 3, 4, 4 };
			for(int i = 0; i < 5; i++)
			{
				Assert.Equal((LightmapSurfaceType)surfaces[i][0], data.Surfaces[i].Type);
				Assert.Equal(surfaces[i][1], data.Surfaces[i].TypeIndex);
				Assert.Equal(surfaces[i][2], data.Surfaces[i].ControlSector);
				Assert.Equal(surfaces[i][3], data.Surfaces[i].Layer);
				Assert.Equal(counts[i], data.Surfaces[i].TexCoordCount);
			}
			Assert.True(Math.Abs(data.GetTexCoord(18).y - (0.5f + 37 * 0.01f)) < 1e-6, "last texture coordinate");
			Assert.Equal(2 * 4 * 4 * 3, data.Texels.Length);

			// Half 0x3C00 is 1.0; layer 1 starts 48 halves in
			float[] layer1 = data.GetLayerRgba(1);
			Assert.Equal(4 * 4 * 4, layer1.Length);
			Assert.Equal(LightmapData.HalfToFloat((ushort)(0x3C00 + (48 % 7))), layer1[0]);
			Assert.Equal(1f, layer1[3]);
			Assert.Equal(1f, LightmapData.HalfToFloat(0x3C00));
			Assert.Equal(-2f, LightmapData.HalfToFloat(0xC000));
			Assert.Equal(0.5f, LightmapData.HalfToFloat(0x3800));
		}

		[Fact]
		public void MalformedLumpsAreRejectedWithAReason()
		{
			int[][] good = { new[] { 1, 0, -1, 0, 0 }, new[] { 5, 0, -1, 0, 4 } };
			float[] coords = Coords(7, 0);
			ReadOk(Lump(4, 1, good, coords));

			// Body shorter than the header claims
			byte[] full = Lump(4, 1, good, coords);
			var raw = new MemoryStream();
			using(var inflate = new DeflateStream(new MemoryStream(full, 2, full.Length - 2), CompressionMode.Decompress)) inflate.CopyTo(raw);
			byte[] cut = raw.ToArray();
			Array.Resize(ref cut, 24 + 30);
			Assert.Contains("truncated", ReadFails(Zlib(cut)));

			// First coordinate past the end, not increasing, layer out of range, unknown type
			Assert.Contains("past", ReadFails(Lump(4, 1, new[] { new[] { 1, 0, -1, 0, 0 }, new[] { 5, 0, -1, 0, 7 } }, coords)));
			Assert.Contains("does not start after", ReadFails(Lump(4, 1, new[] { new[] { 1, 0, -1, 0, 4 }, new[] { 5, 0, -1, 0, 0 } }, coords)));
			Assert.Contains("layer", ReadFails(Lump(4, 1, new[] { new[] { 1, 0, -1, 1, 0 }, new[] { 5, 0, -1, 0, 4 } }, coords)));
			Assert.Contains("unknown type", ReadFails(Lump(4, 1, new[] { new[] { 9, 0, -1, 0, 0 }, new[] { 5, 0, -1, 0, 4 } }, coords)));

			// A wall needs exactly 4 coordinates, a flat 3 or more
			Assert.Contains("wall surface 0", ReadFails(Lump(4, 1, new[] { new[] { 1, 0, -1, 0, 0 }, new[] { 5, 0, -1, 0, 3 } }, Coords(6, 0))));
			Assert.Contains("flat surface 1", ReadFails(Lump(4, 1, new[] { new[] { 1, 0, -1, 0, 0 }, new[] { 5, 0, -1, 0, 4 } }, Coords(6, 0))));

			// Non-zlib, wrong version, non-finite coordinates
			ReadFails(new byte[] { 1, 2, 3, 4 });
			Assert.Contains("version", ReadFails(Lump(4, 1, good, coords, version: 1)));
			float[] nan = Coords(7, 0);
			nan[3] = float.NaN;
			Assert.Contains("not finite", ReadFails(Lump(4, 1, good, nan)));
		}

		[Fact]
		public void HostileHeadersAreRefusedBeforeAnyBodyIsRead()
		{
			// Header only: every case must fail on the header's own numbers, not allocate from them
			Assert.Contains("taller", ReadFails(Zlib(LightmapRetentionTests.Header(0, 1024, 17, 1, 4, 0, 0))));
			Assert.Contains("texels", ReadFails(Zlib(LightmapRetentionTests.Header(0, 8192, 1, 1, 4, 0, 0))));
			Assert.Contains("too many", ReadFails(Zlib(LightmapRetentionTests.Header(0, 4, 1, (uint)LightmapData.MAX_SURFACES + 1, 4, 0, 0))));
			Assert.Contains("bytes", ReadFails(Zlib(LightmapRetentionTests.Header(0, 4, 1, 1, 4, 0xFFFFFFFF, 0))));
			Assert.Contains("empty", ReadFails(Zlib(LightmapRetentionTests.Header(0, 4, 1, 0, 4, 0, 0))));
		}

		[Fact]
		public void ValidateChecksIndicesAgainstTheMap()
		{
			LightmapData data = ReadOk(Lump(4, 1, new[] { new[] { 1, 5, -1, 0, 0 }, new[] { 5, 2, 3, 0, 4 } }, Coords(7, 0)));
			Assert.True(data.Validate(6, 3, 4, out string error), error);
			Assert.False(data.Validate(5, 3, 4, out error));
			Assert.Contains("sidedef 5", error);
			Assert.False(data.Validate(6, 2, 4, out error));
			Assert.Contains("subsector 2", error);
			Assert.False(data.Validate(6, 3, 3, out error));
			Assert.Contains("sector 3", error);
		}

		#endregion

		#region ================== ZNODES

		// segs per subsector: (vertex, line, backside); line -1 writes the format's miniseg value
		private static byte[] GlNodes(string format, int original, Vector2D[] added, int[][][] subsectors)
		{
			bool wide = format[3] != 'N';
			var body = new MemoryStream();
			var w = new BinaryWriter(body);
			w.Write((uint)original);
			w.Write((uint)added.Length);
			foreach(Vector2D v in added)
			{
				w.Write((int)(v.x * 65536));
				w.Write((int)(v.y * 65536));
			}
			w.Write((uint)subsectors.Length);
			int total = 0;
			foreach(int[][] sub in subsectors)
			{
				w.Write((uint)sub.Length);
				total += sub.Length;
			}
			w.Write((uint)total);
			foreach(int[][] sub in subsectors)
			{
				foreach(int[] seg in sub)
				{
					w.Write((uint)seg[0]);
					w.Write(0xFFFFFFFFu);
					if(wide) w.Write(seg[1] < 0 ? 0xFFFFFFFFu : (uint)seg[1]);
					else w.Write(seg[1] < 0 ? (ushort)0xFFFF : (ushort)seg[1]);
					w.Write((byte)seg[2]);
				}
			}
			w.Write(0u); // No nodes
			byte[] raw = body.ToArray();
			var lump = new MemoryStream();
			lump.Write(Encoding.ASCII.GetBytes(format), 0, 4);
			byte[] payload = format[0] == 'Z' ? Zlib(raw) : raw;
			lump.Write(payload, 0, payload.Length);
			return lump.ToArray();
		}

		// A 64x64 square (vertices 0-3) split by the diagonal 0-2 into two subsectors
		private static readonly int[][][] SPLIT_SQUARE = {
			new[] { new[] { 0, 0, 0 }, new[] { 1, 1, 0 }, new[] { 2, -1, 0 } },
			new[] { new[] { 2, 2, 0 }, new[] { 3, 3, 0 }, new[] { 0, -1, 0 } },
		};

		[Fact]
		public void CompressedAndPlainGlNodesParseTheSame()
		{
			foreach(string pair in new[] { "XGLN,ZGLN", "XGL2,ZGL2", "XGL3,ZGL3" })
			{
				string[] f = pair.Split(',');
				GlSubsectors plain = GlSubsectors.Read(GlNodes(f[0], 4, new[] { new Vector2D(32.5, 16) }, SPLIT_SQUARE), out string error);
				Assert.True(plain != null, f[0] + ": " + error);
				GlSubsectors packed = GlSubsectors.Read(GlNodes(f[1], 4, new[] { new Vector2D(32.5, 16) }, SPLIT_SQUARE), out error);
				Assert.True(packed != null, f[1] + ": " + error);
				foreach(GlSubsectors g in new[] { plain, packed })
				{
					Assert.Equal(4, g.OriginalVertexCount);
					Assert.Equal(new Vector2D(32.5, 16), g.NewVertices[0]);
					Assert.Equal(2, g.Count);
					Assert.Equal(3, g.FirstSeg[1]);
					Assert.Equal(3, g.SegCount[1]);
					Assert.Equal(-1, g.SegLine[2]);
					Assert.Equal(3, g.SegLine[4]);
					Assert.Equal(2, g.SegVertex[3]);
				}
			}
		}

		[Fact]
		public void BadGlNodesAreRejected()
		{
			Assert.Contains("unsupported", Fails(Encoding.ASCII.GetBytes("XNOD....")));
			Assert.Contains("vertex 9", Fails(GlNodes("XGLN", 4, new Vector2D[0], new[] { new[] { new[] { 9, 0, 0 }, new[] { 1, 1, 0 }, new[] { 2, 2, 0 } } })));
			byte[] lump = GlNodes("XGL2", 4, new Vector2D[0], SPLIT_SQUARE);
			Array.Resize(ref lump, lump.Length - 20);
			Assert.Contains("truncated", Fails(lump));
			// A subsector count far beyond the data
			byte[] hostile = GlNodes("XGLN", 4, new Vector2D[0], SPLIT_SQUARE);
			BitConverter.GetBytes(0x7FFFFFFFu).CopyTo(hostile, 12);
			Assert.Contains("truncated", Fails(hostile));
		}

		private static string Fails(byte[] lump)
		{
			Assert.Null(GlSubsectors.Read(lump, out string error));
			Assert.NotNull(error);
			return error;
		}

		#endregion

		#region ================== Matcher

		// Two 64x64 rooms side by side sharing the two-sided line 1-4
		//   3---4---5
		//   |   |   |
		//   0---1---2
		private static MapTopology Rooms()
		{
			return new MapTopology
			{
				Vertices = new[] { new Vector2D(0, 0), new Vector2D(64, 0), new Vector2D(128, 0), new Vector2D(0, 64), new Vector2D(64, 64), new Vector2D(128, 64) },
				// Clockwise around each room, front sides inside
				LineStart = new[] { 0, 3, 4, 1, 4, 5, 2 },
				LineEnd = new[] { 3, 4, 1, 0, 5, 2, 1 },
				LineFront = new[] { 0, 1, 2, 3, 4, 5, 6 },
				LineBack = new[] { -1, -1, 7, -1, -1, -1, -1 },
				SideSector = new[] { 0, 0, 0, 0, 1, 1, 1, 1 },
				SectorCount = 2,
			};
		}

		private static BakerIndexMap MatchOk(MapTopology baker, MapTopology map)
		{
			Assert.Null(map.Check());
			Assert.Null(baker.Check());
			BakerIndexMap m = BakerIndexMap.Match(baker, map, out string error);
			Assert.True(m != null, error);
			return m;
		}

		[Fact]
		public void IdenticalMapsMatchOneToOne()
		{
			BakerIndexMap m = MatchOk(Rooms(), Rooms());
			for(int i = 0; i < 8; i++) Assert.Equal(i, m.Sidedef[i]);
			Assert.Equal(0, m.Sector[0]);
			Assert.Equal(1, m.Sector[1]);
			for(int i = 0; i < 7; i++) Assert.Equal(i, m.Linedef[i]);
		}

		[Fact]
		public void ShuffledFlippedAndPrunedBakesStillMatch()
		{
			MapTopology map = Rooms();
			// The map also has a zero-length line in room 0 (sidedef 8) and an unused sector 2
			map.Vertices = map.Vertices.Concat(new[] { new Vector2D(20, 20), new Vector2D(20, 20) }).ToArray();
			map.LineStart = map.LineStart.Concat(new[] { 6 }).ToArray();
			map.LineEnd = map.LineEnd.Concat(new[] { 7 }).ToArray();
			map.LineFront = map.LineFront.Concat(new[] { 8 }).ToArray();
			map.LineBack = map.LineBack.Concat(new[] { -1 }).ToArray();
			map.SideSector = map.SideSector.Concat(new[] { 0 }).ToArray();
			map.SectorCount = 3;

			// Baker: sectors swapped, vertices reversed, lines reversed, the shared line flipped
			MapTopology baker = Rooms();
			int[] vperm = { 5, 4, 3, 2, 1, 0 }; // old vertex i is now vperm[i]
			Vector2D[] verts = new Vector2D[6];
			for(int i = 0; i < 6; i++) verts[vperm[i]] = baker.Vertices[i];
			baker.Vertices = verts;
			baker.LineStart = baker.LineStart.Select(v => vperm[v]).Reverse().ToArray();
			baker.LineEnd = baker.LineEnd.Select(v => vperm[v]).Reverse().ToArray();
			baker.LineFront = baker.LineFront.Reverse().ToArray();
			baker.LineBack = baker.LineBack.Reverse().ToArray();
			baker.SideSector = baker.SideSector.Select(s => 1 - s).ToArray();
			// Shared line is now index 4: flip it
			int shared = 4;
			int a = baker.LineStart[shared];
			baker.LineStart[shared] = baker.LineEnd[shared];
			baker.LineEnd[shared] = a;
			int f = baker.LineFront[shared];
			baker.LineFront[shared] = baker.LineBack[shared];
			baker.LineBack[shared] = f;

			BakerIndexMap m = MatchOk(baker, map);
			for(int i = 0; i < 8; i++) Assert.Equal(i, m.Sidedef[i]);
			Assert.Equal(1, m.Sector[0]);
			Assert.Equal(0, m.Sector[1]);
			Assert.Equal(2, m.Linedef[4]);
			Assert.Equal(6, m.Linedef[0]);
		}

		[Fact]
		public void OverlappingLinesMatchInIndexOrder()
		{
			MapTopology map = Rooms();
			// A second one-sided line over line 0, with its own sidedef 8 in room 0
			map.LineStart = map.LineStart.Concat(new[] { 0 }).ToArray();
			map.LineEnd = map.LineEnd.Concat(new[] { 3 }).ToArray();
			map.LineFront = map.LineFront.Concat(new[] { 8 }).ToArray();
			map.LineBack = map.LineBack.Concat(new[] { -1 }).ToArray();
			map.SideSector = map.SideSector.Concat(new[] { 0 }).ToArray();
			BakerIndexMap m = MatchOk(map, map);
			Assert.Equal(0, m.Sidedef[0]);
			Assert.Equal(8, m.Sidedef[8]);
		}

		[Fact]
		public void MissingSidedefsAndSplitSectorsRefuse()
		{
			// The map lost the room-1 side of the shared line
			MapTopology map = Rooms();
			map.LineBack[2] = -1;
			Assert.Null(BakerIndexMap.Match(Rooms(), map, out string error));
			Assert.Contains("baked sidedef 7", error);

			// The map gained a line the bake lacks
			Assert.Null(BakerIndexMap.Match(map, Rooms(), out error));
			Assert.Contains("map sidedef 7", error);

			// One baked sector whose sides land in both map sectors
			MapTopology merged = Rooms();
			merged.SideSector = new[] { 0, 0, 0, 0, 0, 0, 0, 0 };
			merged.SectorCount = 1;
			Assert.Null(BakerIndexMap.Match(merged, Rooms(), out error));
			Assert.Contains("more than one sector", error);

			// Two baked sectors landing on one map sector
			Assert.Null(BakerIndexMap.Match(Rooms(), merged, out error));
			Assert.Contains("more than one baked sector", error);
		}

		[Fact]
		public void TextmapTopologyReadsLinesSidesAndSectors()
		{
			string[] textmap = (
				"namespace = \"zdoom\";\n" +
				"vertex { x = 0.0; y = 0.0; }\nvertex { x = 64.0; y = 0.0; }\nvertex { x = 64; y = 64.5; }\n" +
				"linedef { v1 = 0; v2 = 1; sidefront = 0; }\nlinedef { v1 = 1; v2 = 2; sidefront = 1; sideback = 2; }\n" +
				"sidedef { sector = 0; }\nsidedef { sector = 0; }\nsidedef { sector = 1; }\n" +
				"sector { }\nsector { }\n").Split('\n');
			MapTopology t = MapTopology.FromTextmap(textmap, out string error);
			Assert.True(t != null, error);
			Assert.Equal(3, t.Vertices.Length);
			Assert.Equal(new Vector2D(64, 64.5), t.Vertices[2]);
			Assert.Equal(-1, t.LineBack[0]);
			Assert.Equal(2, t.LineBack[1]);
			Assert.Equal(1, t.SideSector[2]);
			Assert.Equal(2, t.SectorCount);

			textmap[textmap.Length - 2] = "";
			Assert.Null(MapTopology.FromTextmap(textmap, out error));
			Assert.Contains("missing sector", error);
		}

		#endregion

		#region ================== Walls

		private static Vector2D Uv(double t, double z) { return new Vector2D(0.1 + 0.3 * t + 0.001 * z, 0.2 - 0.05 * t + 0.004 * z); }

		[Fact]
		public void WallFitIsExactForStraightAndSlopedParts()
		{
			// bottom left, top left, top right, bottom right
			foreach(double[] z in new[] { new[] { 0.0, 128, 128, 0 }, new[] { 0.0, 128, 128, 32 }, new[] { -16.0, 100, 140, 8 } })
			{
				Vector2D[] corners = { Uv(0, z[0]), Uv(0, z[1]), Uv(1, z[2]), Uv(1, z[3]) };
				Assert.True(WallLightmap.Fit(corners, z[0], z[1], z[2], z[3], 2, out WallLightmap w));
				Assert.Equal(2, w.Layer);
				// Corners and arbitrary interior points of any split
				foreach(double[] p in new[] { new[] { 0, z[0] }, new[] { 1, z[2] }, new[] { 0.3, 37.5 }, new[] { 0.71, 99.25 } })
				{
					Vector2D want = Uv(p[0], p[1]);
					Assert.True(Math.Abs(w.GetU(p[0], p[1]) - want.x) < 1e-9 && Math.Abs(w.GetV(p[0], p[1]) - want.y) < 1e-9,
						"t " + p[0] + " z " + p[1]);
				}
			}

			Assert.False(WallLightmap.Fit(new[] { Uv(0, 0), Uv(0, 0), Uv(1, 0), Uv(1, 0) }, 0, 0, 0, 0, 0, out _));
		}

		#endregion

		#region ================== Preview

		// One 64x64 square sector (vertices 0-3, lines 0-3 clockwise), split by a miniseg on 0-2,
		// plus a 3D floor control sector 1 off to the side (vertices 4-7, lines 4-7, sides 4-7)
		private static MapTopology SquareWithControl()
		{
			return new MapTopology
			{
				Vertices = new[] {
					new Vector2D(0, 0), new Vector2D(0, 64), new Vector2D(64, 64), new Vector2D(64, 0),
					new Vector2D(200, 0), new Vector2D(200, 16), new Vector2D(216, 16), new Vector2D(216, 0) },
				LineStart = new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
				LineEnd = new[] { 1, 2, 3, 0, 5, 6, 7, 4 },
				LineFront = new[] { 0, 1, 2, 3, 4, 5, 6, 7 },
				LineBack = new[] { -1, -1, -1, -1, -1, -1, -1, -1 },
				SideSector = new[] { 0, 0, 0, 0, 1, 1, 1, 1 },
				SectorCount = 2,
			};
		}

		private static LightmapPreview Preview(int[][] surfaces, float[] coords)
		{
			LightmapData data = ReadOk(Lump(4, 2, surfaces, coords, subsectors: 2));
			int[][][] subs = {
				new[] { new[] { 0, 0, 0 }, new[] { 1, 1, 0 }, new[] { 2, -1, 0 } },
				new[] { new[] { 2, 2, 0 }, new[] { 3, 3, 0 }, new[] { 0, -1, 0 } },
			};
			GlSubsectors gl = GlSubsectors.Read(GlNodes("XGLN", 8, new Vector2D[0], subs), out string error);
			Assert.True(gl != null, error);
			LightmapPreview p = LightmapPreview.Build(data, gl, SquareWithControl(), SquareWithControl(), out error);
			Assert.True(p != null, error);
			return p;
		}

		[Fact]
		public void FlatsComeFromSubsectorsWithPerVertexCoordinates()
		{
			// Floor on subsector 0 only; 3D floor top (baker floor + control) and underside on subsector 1
			int[][] surfaces = {
				new[] { 5, 0, -1, 0, 0 },
				new[] { 5, 1, 1, 1, 3 },
				new[] { 4, 1, 1, 0, 6 },
			};
			LightmapPreview p = Preview(surfaces, Coords(9, 0));
			Assert.True(p.HasFlats(0));
			Assert.False(p.HasFlats(1));
			Assert.Null(p.GetFlat(1, false, -1));

			LightmapFlat floor = p.GetFlat(0, false, -1);
			Assert.Equal(6, floor.Positions.Length);
			// Subsector 0's fan in seg order (0, 1, 2): lit with texcoords 0..2 and layer 0 + 1
			Assert.Equal(new Vector2D(0, 0), floor.Positions[0]);
			Assert.Equal(new Vector2D(0, 64), floor.Positions[1]);
			Assert.Equal(new Vector2D(64, 64), floor.Positions[2]);
			Assert.True(Math.Abs(floor.Lightmap[3 * 2] - 0.04f) < 1e-6, "third vertex u is texcoord 2");
			Assert.Equal(1f, floor.Lightmap[2]);
			// Clockwise in map coordinates, as Sector.Triangles' floor triangles are
			Assert.True(Cross(floor.Positions[0], floor.Positions[1], floor.Positions[2]) < 0);
			// Subsector 1 has no floor surface: drawn, but without lightmap
			Assert.Equal(0f, floor.Lightmap[3 * 3 + 2]);

			Assert.Equal(0f, p.GetFlat(0, true, -1).Lightmap[2]);

			// 3D floor: Visual mode ceiling = the top = baker floor surface (layer 1), floor = underside
			LightmapFlat top = p.GetFlat(0, true, 1);
			Assert.Equal(0f, top.Lightmap[2]);
			Assert.Equal(2f, top.Lightmap[3 * 3 + 2]);
			LightmapFlat under = p.GetFlat(0, false, 1);
			Assert.Equal(1f, under.Lightmap[3 * 3 + 2]);
			Assert.True(Math.Abs(under.Lightmap[3 * 3] - 0.12f) < 1e-6, "underside starts at texcoord 6");
		}

		[Fact]
		public void WallsAreFoundBySidedefPartAndControlSector()
		{
			int[][] surfaces = {
				new[] { 1, 0, -1, 0, 0 },
				new[] { 2, 1, -1, 1, 4 },
				new[] { 1, 2, 1, 1, 8 },
			};
			LightmapPreview p = Preview(surfaces, Coords(12, 0));
			Vector2D[] c = p.GetWallCorners(0, LightmapWallPart.Middle, -1, out int layer);
			Assert.NotNull(c);
			Assert.Equal(0, layer);
			Assert.True(Math.Abs(c[3].y - 0.07) < 1e-6, "corner 3 is texcoord 3");
			Assert.NotNull(p.GetWallCorners(1, LightmapWallPart.Upper, -1, out layer));
			Assert.Null(p.GetWallCorners(1, LightmapWallPart.Lower, -1, out layer));
			Assert.Null(p.GetWallCorners(2, LightmapWallPart.Middle, -1, out layer));
			c = p.GetWallCorners(2, LightmapWallPart.Middle, 1, out layer);
			Assert.NotNull(c);
			Assert.Equal(1, layer);
			Assert.True(Math.Abs(c[0].x - 0.16) < 1e-6, "3D floor wall corners in the lump's order");
		}

		[Fact]
		public void MismatchedNodesOrIndicesRefuseThePreview()
		{
			LightmapData data = ReadOk(Lump(4, 1, new[] { new[] { 1, 9, -1, 0, 0 } }, Coords(4, 0), subsectors: 2));
			GlSubsectors gl = GlSubsectors.Read(GlNodes("XGLN", 8, new Vector2D[0], SPLIT_SQUARE), out string error);
			Assert.Null(LightmapPreview.Build(data, gl, SquareWithControl(), SquareWithControl(), out error));
			Assert.Contains("sidedef 9", error);

			GlSubsectors other = GlSubsectors.Read(GlNodes("XGLN", 7, new Vector2D[0], SPLIT_SQUARE), out error);
			Assert.Null(LightmapPreview.Build(data, other, SquareWithControl(), SquareWithControl(), out error));
			Assert.Contains("vertices", error);
		}

		private static double Cross(Vector2D a, Vector2D b, Vector2D c)
		{
			return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
		}

		#endregion
	}
}
