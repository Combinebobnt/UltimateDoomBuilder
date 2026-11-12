using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CodeImp.DoomBuilder.ZDoom
{
	internal struct AnimatedEntry
	{
		public bool IsTexture;
		public string First;
		public string Last;
		public uint Tics;
	}

	internal static class AnimatedLump
	{
		internal static List<AnimatedEntry> Parse(byte[] data, Action<string> warning = null)
		{
			var result = new List<AnimatedEntry>();
			for(int offset = 0; offset < data.Length; offset += 23)
			{
				if(data[offset] == 255) break;
				if(data.Length - offset < 23)
				{
					if(warning != null) warning("Truncated ANIMATED entry");
					break;
				}
				result.Add(new AnimatedEntry {
					IsTexture = (data[offset] & 1) != 0,
					Last = Name(data, offset + 1), First = Name(data, offset + 10),
					Tics = (uint)(data[offset + 19] | data[offset + 20] << 8 | data[offset + 21] << 16 | data[offset + 22] << 24)
				});
			}
			return result;
		}

		private static string Name(byte[] data, int offset)
		{
			int length = 0;
			while(length < 9 && data[offset + length] != 0) length++;
			return Encoding.ASCII.GetString(data, offset, length).ToUpperInvariant();
		}

		internal static List<AnimatedEntry> LoadDefaults(string table)
		{
			var result = new List<AnimatedEntry>();
			using(Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodeImp.DoomBuilder.Resources.DefaultAnimated.cfg"))
			using(var reader = new StreamReader(stream))
			{
				string line;
				while((line = reader.ReadLine()) != null)
				{
					string[] fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
					if(fields.Length != 5 || fields[0] != table) continue;
					result.Add(new AnimatedEntry { IsTexture = fields[1] == "texture", First = fields[2], Last = fields[3], Tics = uint.Parse(fields[4], CultureInfo.InvariantCulture) });
				}
			}
			return result;
		}
	}
}
