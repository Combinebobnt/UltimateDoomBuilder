#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Map.Statistics
{
	public sealed class OverviewRow
	{
		public string Label;
		public string Value;
		public bool OverLimit;

		public OverviewRow(string label, string value, bool overlimit = false)
		{
			Label = label;
			Value = value;
			OverLimit = overlimit;
		}
	}

	// One texture or flat name and where the map uses it
	public sealed class TextureUse
	{
		public string Name;
		public bool IsFlat;
		public bool Missing;
		public int Uses;
		public HashSet<Linedef> Linedefs = new HashSet<Linedef>();
		public HashSet<Sector> Sectors = new HashSet<Sector>();
	}

	// The Overview and Textures tabs' passes over the open map
	public static class MapOverviewStats
	{
		// Doom: effect 9 or generalized bit 128; other formats: bit 1024, or UDMF's secret flag
		public static bool SectorIsSecret(Sector s)
		{
			SectorEffectData data = General.Map.Config.GetSectorEffectData(s.Effect);

			if(General.Map.DOOM)
				return data.Effect == 9 || data.GeneralizedBits.Contains(128);

			if(data.GeneralizedBits.Contains(1024)) return true;
			return General.Map.UDMF && s.IsFlagSet("secret");
		}

		public static List<Sector> SecretSectors()
		{
			return General.Map.Map.Sectors.Where(SectorIsSecret).ToList();
		}

		public static List<OverviewRow> Overview(List<TextureUse> textures)
		{
			MapSet map = General.Map.Map;
			var io = General.Map.FormatInterface;
			List<OverviewRow> rows = new List<OverviewRow>();

			rows.Add(new OverviewRow("Map", General.Map.Options.LevelName));
			rows.Add(new OverviewRow("Format", General.Map.UDMF ? "UDMF" : General.Map.HEXEN ? "Hexen" : "Doom"));
			rows.Add(new OverviewRow("Game configuration", General.Map.ConfigSettings.Name));

			rows.Add(Counted("Vertices", map.Vertices.Count, io.MaxVertices));
			rows.Add(Counted("Linedefs", map.Linedefs.Count, io.MaxLinedefs));
			rows.Add(Counted("Sidedefs", map.Sidedefs.Count, io.MaxSidedefs));
			rows.Add(Counted("Sectors", map.Sectors.Count, io.MaxSectors));
			rows.Add(Counted("Things", map.Things.Count, io.MaxThings));

			if(map.Vertices.Count > 0)
			{
				double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
				foreach(Vertex v in map.Vertices)
				{
					minx = Math.Min(minx, v.Position.x);
					miny = Math.Min(miny, v.Position.y);
					maxx = Math.Max(maxx, v.Position.x);
					maxy = Math.Max(maxy, v.Position.y);
				}

				rows.Add(new OverviewRow("Bounds", string.Format(CultureInfo.InvariantCulture, "({0}, {1}) to ({2}, {3})", minx, miny, maxx, maxy)));
				rows.Add(new OverviewRow("Size", string.Format(CultureInfo.InvariantCulture, "{0} x {1}", maxx - minx, maxy - miny)));
			}

			rows.Add(Number("Linedefs with an action", map.Linedefs.Count(l => l.Action != 0)));
			if(io.HasLinedefTag) rows.Add(Number("Tagged linedefs", map.Linedefs.Count(l => l.Tags.Any(t => t != 0))));
			rows.Add(Number("Tagged sectors", map.Sectors.Count(s => s.Tags.Any(t => t != 0))));
			rows.Add(Number("Sectors with an effect", map.Sectors.Count(s => s.Effect != 0)));
			rows.Add(Number("Secret sectors", map.Sectors.Count(SectorIsSecret)));
			rows.Add(Number("Things of unknown type", map.Things.Count(t => General.Map.Data.GetThingInfoEx(t.Type) == null)));

			int[] starts = new int[5];
			int dmstarts = 0;
			foreach(Thing t in map.Things)
			{
				int player = PlayerStart(t.Type);
				if(player > 0) starts[player]++;
				else if(player == 0) dmstarts++;
			}
			rows.Add(new OverviewRow("Player 1-4 starts", string.Join(" / ", starts.Skip(1))));
			rows.Add(Number("Deathmatch starts", dmstarts));

			int numbered = (General.Map.NumberedScripts != null) ? General.Map.NumberedScripts.Count : 0;
			int named = (General.Map.NamedScripts != null) ? General.Map.NamedScripts.Count : 0;
			if(numbered + named > 0) rows.Add(new OverviewRow("Scripts", numbered + " numbered, " + named + " named"));

			int walls = textures.Count(t => !t.IsFlat), flats = textures.Count(t => t.IsFlat);
			rows.Add(new OverviewRow("Wall textures used", walls + Missing(textures.Count(t => !t.IsFlat && t.Missing)), textures.Any(t => !t.IsFlat && t.Missing)));
			rows.Add(new OverviewRow("Flats used", flats + Missing(textures.Count(t => t.IsFlat && t.Missing)), textures.Any(t => t.IsFlat && t.Missing)));

			return rows;
		}

		private static string Missing(int count)
		{
			return (count > 0) ? " (" + count + " missing)" : string.Empty;
		}

		private static OverviewRow Counted(string label, int count, int limit)
		{
			return new OverviewRow(label, count + " / " + limit, count > limit);
		}

		private static OverviewRow Number(string label, int count)
		{
			return new OverviewRow(label, count.ToString(CultureInfo.InvariantCulture));
		}

		// 1-4 for a player start, 0 for a deathmatch start, -1 otherwise
		public static int PlayerStart(int type)
		{
			ThingTypeInfo ti = General.Map.Data.GetThingInfoEx(type);
			string cls = (ti != null) ? ti.ClassName : null;

			if(!string.IsNullOrEmpty(cls))
			{
				if(string.Equals(cls, "$DeathmatchStart", StringComparison.OrdinalIgnoreCase)) return 0;
				for(int i = 1; i <= 4; i++)
					if(string.Equals(cls, "$Player" + i + "Start", StringComparison.OrdinalIgnoreCase)) return i;
				return -1;
			}

			if(type >= 1 && type <= 4) return type;
			return (type == 11) ? 0 : -1;
		}

		public static List<TextureUse> Textures()
		{
			Dictionary<string, TextureUse> uses = new Dictionary<string, TextureUse>(StringComparer.OrdinalIgnoreCase);

			foreach(Sidedef sd in General.Map.Map.Sidedefs)
			{
				AddTexture(uses, sd.HighTexture, sd.Line);
				AddTexture(uses, sd.MiddleTexture, sd.Line);
				AddTexture(uses, sd.LowTexture, sd.Line);
			}

			foreach(Sector s in General.Map.Map.Sectors)
			{
				AddFlat(uses, s.FloorTexture, s);
				AddFlat(uses, s.CeilTexture, s);
			}

			return uses.Values.OrderBy(u => u.IsFlat).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
		}

		private static bool Skip(string name)
		{
			return string.IsNullOrEmpty(name) || name == "-";
		}

		private static void AddTexture(Dictionary<string, TextureUse> uses, string name, Linedef line)
		{
			if(Skip(name)) return;
			TextureUse u = Get(uses, name, false);
			u.Uses++;
			u.Linedefs.Add(line);
		}

		private static void AddFlat(Dictionary<string, TextureUse> uses, string name, Sector sector)
		{
			if(Skip(name)) return;
			TextureUse u = Get(uses, name, true);
			u.Uses++;
			u.Sectors.Add(sector);
		}

		// In a mixed textures/flats config, kind follows the slot a name is used in
		private static TextureUse Get(Dictionary<string, TextureUse> uses, string name, bool flat)
		{
			string key = (flat ? "F:" : "T:") + name;
			if(!uses.TryGetValue(key, out TextureUse u))
			{
				u = new TextureUse
				{
					Name = name,
					IsFlat = flat,
					Missing = flat ? !General.Map.Data.GetFlatExists(name) : !General.Map.Data.GetTextureExists(name)
				};
				uses[key] = u;
			}
			return u;
		}

		// Tab-separated text for the clipboard
		public static string ToText(IEnumerable<OverviewRow> rows)
		{
			StringBuilder sb = new StringBuilder();
			foreach(OverviewRow r in rows) sb.Append(r.Label).Append('\t').Append(r.Value).Append(Environment.NewLine);
			return sb.ToString();
		}
	}
}
