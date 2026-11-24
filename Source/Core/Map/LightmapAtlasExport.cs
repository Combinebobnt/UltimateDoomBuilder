#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

#endregion

namespace CodeImp.DoomBuilder.Map
{
	// Writes a LIGHTMAP lump's texture pages as PNG files, one row at a time, so any texture size exports
	internal static class LightmapAtlasExport
	{
		#region ================== Methods

		// The file a page goes to: the chosen path for a single page, numbered from 0 otherwise
		public static string PagePath(string path, int page, int pagecount)
		{
			if(pagecount == 1) return path;
			return Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + "_" + page + Path.GetExtension(path));
		}

		// Returns the files written; on a failure, error says why and the pages written before it stay
		public static List<string> Export(byte[] compressed, string path, out string error)
		{
			List<string> written = new List<string>();
			LightmapLump header = LightmapLump.Read(new MemoryStream(compressed, false), false, out error);
			if(header == null) return written;
			if(header.IsEmpty)
			{
				error = "the lightmap is empty";
				return written;
			}

			int size = header.TextureSize;
			byte[] row = new byte[size * 6];
			byte[] pixels = new byte[size * 3];
			byte[] table = ByteTable;
			try
			{
				using(BinaryReader reader = LightmapData.Inflate(compressed))
				{
					LightmapData.Skip(reader, LightmapData.LayoutLength(header));
					for(int page = 0; page < header.TextureCount; page++)
					{
						using(Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format24bppRgb))
						{
							BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
							try
							{
								for(int y = 0; y < size; y++)
								{
									if(LightmapData.ReadFully(reader, row, row.Length) < row.Length)
									{
										error = "truncated texture data in page " + page;
										return written;
									}
									// RGB half floats to BGR bytes
									for(int x = 0; x < size; x++)
									{
										pixels[x * 3] = table[BitConverter.ToUInt16(row, x * 6 + 4)];
										pixels[x * 3 + 1] = table[BitConverter.ToUInt16(row, x * 6 + 2)];
										pixels[x * 3 + 2] = table[BitConverter.ToUInt16(row, x * 6)];
									}
									Marshal.Copy(pixels, 0, bits.Scan0 + y * bits.Stride, pixels.Length);
								}
							}
							finally { bitmap.UnlockBits(bits); }

							string file = PagePath(path, page, header.TextureCount);
							bitmap.Save(file, ImageFormat.Png);
							written.Add(file);
						}
					}
				}
			}
			catch(EndOfStreamException) { error = "truncated data"; }
			catch(InvalidDataException e) { error = "corrupt data: " + e.Message; }
			catch(IOException e) { error = e.Message; }
			catch(UnauthorizedAccessException e) { error = e.Message; }
			catch(ExternalException e) { error = "the PNG could not be written: " + e.Message; }
			return written;
		}

		// A half float clamped to 0..1 as a byte
		private static byte[] bytetable;
		private static byte[] ByteTable
		{
			get
			{
				if(bytetable == null)
				{
					byte[] t = new byte[65536];
					for(int i = 0; i < t.Length; i++)
					{
						float f = LightmapData.HalfToFloat((ushort)i);
						t[i] = (byte)(float.IsNaN(f) ? 0 : Math.Round(Math.Max(0f, Math.Min(1f, f)) * 255f));
					}
					bytetable = t;
				}
				return bytetable;
			}
		}

		#endregion
	}
}
