#region ================== Namespaces

using System;
using System.IO;
using System.IO.Compression;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// Header of a baked LIGHTMAP lump (zlib, version 0), the layout UZDoom's MapLoader::LoadLightmap reads
	internal sealed class LightmapLump
	{
		#region ================== Constants

		public const string LUMP_NAME = "LIGHTMAP";
		public const int SUPPORTED_VERSION = 0;
		public const int HEADER_SIZE = 24;

		// UZDoom sizes its texture buffer with a uint32, so anything larger cannot load
		private const long MAX_INFLATED_SIZE = uint.MaxValue;

		#endregion

		#region ================== Properties

		public int Version { get; private set; }
		public int TextureSize { get; private set; }
		public int TextureCount { get; private set; }
		public uint SurfaceCount { get; private set; }
		public uint TexCoordCount { get; private set; }
		public uint LightProbeCount { get; private set; }
		public uint SubsectorCount { get; private set; }

		// Inflated size the header implies: header, probes, surfaces, texture coordinates, RGB16F textures
		public long ExpectedLength
		{
			get
			{
				return HEADER_SIZE + LightProbeCount * 24L + SurfaceCount * 20L + TexCoordCount * 8L
					+ (long)TextureCount * TextureSize * TextureSize * 6L;
			}
		}

		// UZDoom ignores a lump with no surfaces, texture coordinates or texels
		public bool IsEmpty { get { return SurfaceCount == 0 || TexCoordCount == 0 || TextureCount == 0 || TextureSize == 0; } }

		#endregion

		#region ================== Constructor

		private LightmapLump() { }

		#endregion

		#region ================== Methods

		// Reads the header from the compressed lump. With checklength, also inflates the rest
		// (without keeping it) and requires its length to match the header. Returns null on failure.
		public static LightmapLump Read(Stream compressed, bool checklength, out string error)
		{
			error = null;
			try
			{
				// Read, not ReadByte: a lump's ClippedStream throws on ReadByte past its end
				byte[] zlibheader = new byte[2];
				// Deflate, valid check bits, no preset dictionary (FDICT)
				if(ReadFully(compressed, zlibheader, 2) < 2 || (zlibheader[0] & 0x0F) != 8
					|| ((zlibheader[0] << 8) | zlibheader[1]) % 31 != 0 || (zlibheader[1] & 0x20) != 0)
				{
					error = "not zlib data";
					return null;
				}

				using(DeflateStream inflater = new DeflateStream(compressed, CompressionMode.Decompress, true))
				{
					byte[] header = new byte[HEADER_SIZE];
					if(ReadFully(inflater, header, HEADER_SIZE) < HEADER_SIZE)
					{
						error = "truncated header";
						return null;
					}

					LightmapLump result = new LightmapLump
					{
						Version = BitConverter.ToInt32(header, 0),
						TextureSize = BitConverter.ToUInt16(header, 4),
						TextureCount = BitConverter.ToUInt16(header, 6),
						SurfaceCount = BitConverter.ToUInt32(header, 8),
						TexCoordCount = BitConverter.ToUInt32(header, 12),
						LightProbeCount = BitConverter.ToUInt32(header, 16),
						SubsectorCount = BitConverter.ToUInt32(header, 20),
					};

					if(result.Version != SUPPORTED_VERSION)
					{
						error = "unsupported version " + result.Version;
						return null;
					}

					long expected = result.ExpectedLength;
					if(expected > MAX_INFLATED_SIZE)
					{
						error = "header claims " + expected + " bytes";
						return null;
					}

					if(checklength)
					{
						// Stops one buffer past the expected end, so a hostile stream can't run on
						long total = HEADER_SIZE;
						byte[] buffer = new byte[65536];
						int count;
						while(total <= expected && (count = inflater.Read(buffer, 0, buffer.Length)) > 0) total += count;
						if(total != expected)
						{
							error = (total < expected ? "truncated: " : "trailing data: ") + total + " of " + expected + " bytes";
							return null;
						}
					}

					return result;
				}
			}
			// .NET throws InvalidDataException on a corrupt deflate stream, Mono an IOException
			catch(IOException e)
			{
				error = "corrupt data: " + e.Message;
				return null;
			}
			catch(InvalidDataException e)
			{
				error = "corrupt data: " + e.Message;
				return null;
			}
		}

		private static int ReadFully(Stream stream, byte[] buffer, int length)
		{
			int total = 0, count;
			while(total < length && (count = stream.Read(buffer, total, length - total)) > 0) total += count;
			return total;
		}

		public override string ToString()
		{
			return "version " + Version + ", " + TextureCount + " texture(s) of " + TextureSize + "x" + TextureSize + ", "
				+ SurfaceCount + " surfaces, " + LightProbeCount + " light probes, " + SubsectorCount + " subsectors";
		}

		#endregion
	}
}
