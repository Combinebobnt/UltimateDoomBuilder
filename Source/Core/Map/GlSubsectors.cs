#region ================== Namespaces

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// The GL subsectors of a ZNODES lump in an extended GL format (XGLN, XGL2, XGL3 and their
	// compressed Z forms). Nodes are not read. Seg vertex indices below OriginalVertexCount refer
	// to the map's own vertices as the node builder saw them, the rest to NewVertices.
	internal sealed class GlSubsectors
	{
		#region ================== Constants

		public const string LUMP_NAME = "ZNODES";

		// A compressed lump inflating past this is refused
		private const int MAX_INFLATED_SIZE = 256 << 20;

		#endregion

		#region ================== Properties

		public string Format { get; private set; }
		public int OriginalVertexCount { get; private set; }
		public Vector2D[] NewVertices { get; private set; }
		public int[] FirstSeg { get; private set; }
		public int[] SegCount { get; private set; }
		public int[] SegVertex { get; private set; }
		public int[] SegLine { get; private set; } // -1 for a miniseg
		public bool[] SegBackSide { get; private set; }
		public int Count { get { return FirstSeg.Length; } }

		#endregion

		#region ================== Constructor

		private GlSubsectors() { }

		#endregion

		#region ================== Methods

		// Returns null with a reason on an unsupported or malformed lump
		public static GlSubsectors Read(byte[] lump, out string error)
		{
			error = null;
			if(lump == null || lump.Length < 4) return Fail("the nodes lump is empty", out error);
			string format = Encoding.ASCII.GetString(lump, 0, 4);
			bool compressed;
			switch(format)
			{
				case "XGLN": case "XGL2": case "XGL3": compressed = false; break;
				case "ZGLN": case "ZGL2": case "ZGL3": compressed = true; break;
				default: return Fail("unsupported nodes format \"" + format + "\" (GL nodes needed)", out error);
			}
			bool wideline = format[3] != 'N';

			byte[] body;
			if(compressed)
			{
				body = Inflate(lump, out error);
				if(body == null) return null;
			}
			else
			{
				body = new byte[lump.Length - 4];
				Buffer.BlockCopy(lump, 4, body, 0, body.Length);
			}

			try
			{
				using(BinaryReader reader = new BinaryReader(new MemoryStream(body, false)))
				{
					GlSubsectors result = new GlSubsectors { Format = format };
					uint original = reader.ReadUInt32();
					uint added = reader.ReadUInt32();
					if(!Fits(reader, added, 8)) return Fail("truncated vertices", out error);
					if(original > int.MaxValue - added) return Fail("too many vertices", out error);
					result.OriginalVertexCount = (int)original;
					result.NewVertices = new Vector2D[added];
					for(int i = 0; i < added; i++)
						result.NewVertices[i] = new Vector2D(reader.ReadInt32() / 65536.0, reader.ReadInt32() / 65536.0);
					long vertexcount = (long)original + added;

					uint subsectors = reader.ReadUInt32();
					if(!Fits(reader, subsectors, 4)) return Fail("truncated subsectors", out error);
					result.FirstSeg = new int[subsectors];
					result.SegCount = new int[subsectors];
					long total = 0;
					for(int i = 0; i < subsectors; i++)
					{
						uint n = reader.ReadUInt32();
						result.FirstSeg[i] = (int)Math.Min(total, int.MaxValue);
						result.SegCount[i] = (int)Math.Min(n, int.MaxValue);
						total += n;
					}

					uint segs = reader.ReadUInt32();
					if(segs != total) return Fail("subsectors use " + total + " segs, the lump has " + segs, out error);
					int segsize = wideline ? 13 : 11;
					if(!Fits(reader, segs, segsize)) return Fail("truncated segs", out error);
					result.SegVertex = new int[segs];
					result.SegLine = new int[segs];
					result.SegBackSide = new bool[segs];
					for(int i = 0; i < segs; i++)
					{
						uint v = reader.ReadUInt32();
						reader.ReadUInt32(); // Partner seg
						uint line = wideline ? reader.ReadUInt32() : reader.ReadUInt16();
						byte side = reader.ReadByte();
						if(v >= vertexcount) return Fail("seg " + i + " uses vertex " + v + " of " + vertexcount, out error);
						bool miniseg = wideline ? line == 0xFFFFFFFF : line == 0xFFFF;
						if(!miniseg && line > int.MaxValue) return Fail("seg " + i + " has line index " + line, out error);
						result.SegVertex[i] = (int)v;
						result.SegLine[i] = miniseg ? -1 : (int)line;
						result.SegBackSide[i] = side != 0;
					}

					return result;
				}
			}
			catch(EndOfStreamException)
			{
				return Fail("truncated nodes lump", out error);
			}
		}

		// Position of a seg vertex, given the vertices the node builder saw
		public Vector2D GetVertex(int index, Vector2D[] originalvertices)
		{
			return index < OriginalVertexCount ? originalvertices[index] : NewVertices[index - OriginalVertexCount];
		}

		private static bool Fits(BinaryReader reader, uint count, int size)
		{
			Stream s = reader.BaseStream;
			return (long)count * size <= s.Length - s.Position;
		}

		private static GlSubsectors Fail(string reason, out string error)
		{
			error = reason;
			return null;
		}

		// The data after the signature is a zlib stream
		private static byte[] Inflate(byte[] lump, out string error)
		{
			error = null;
			if(lump.Length < 6 || (lump[4] & 0x0F) != 8 || ((lump[4] << 8) | lump[5]) % 31 != 0 || (lump[5] & 0x20) != 0)
			{
				error = "compressed nodes are not zlib data";
				return null;
			}
			try
			{
				using(DeflateStream inflater = new DeflateStream(new MemoryStream(lump, 6, lump.Length - 6, false), CompressionMode.Decompress))
				using(MemoryStream output = new MemoryStream())
				{
					byte[] buffer = new byte[65536];
					int n;
					while((n = inflater.Read(buffer, 0, buffer.Length)) > 0)
					{
						output.Write(buffer, 0, n);
						if(output.Length > MAX_INFLATED_SIZE)
						{
							error = "compressed nodes inflate past " + MAX_INFLATED_SIZE + " bytes";
							return null;
						}
					}
					return output.ToArray();
				}
			}
			catch(IOException e)
			{
				error = "corrupt compressed nodes: " + e.Message;
				return null;
			}
			catch(InvalidDataException e)
			{
				error = "corrupt compressed nodes: " + e.Message;
				return null;
			}
		}

		#endregion
	}
}
