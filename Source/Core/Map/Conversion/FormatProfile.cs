#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	public enum MapFormat
	{
		Doom,
		Hexen,
		Udmf,
	}

	// What one side of a conversion can hold, read from a raw game configuration so tests need no General.Map
	public sealed class FormatProfile
	{
		#region ================== Constants

		// Engines that read every type and line flag bit of their xlat table, whatever the config lists
		private static readonly HashSet<string> xlatengines = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zdoom", "gzdoom", "zandronum" };

		#endregion

		#region ================== Properties

		public string Name { get; private set; }
		public MapFormat Format { get; private set; }
		public string Engine { get; private set; }

		// Doom format only
		public LineXlat Lines { get; private set; }
		public SectorXlat Sectors { get; private set; }

		// Doom and Hexen: bits to UDMF names, as the config's copy/paste tables give them
		public List<FlagTranslation> LineFlagTable { get; private set; }
		public List<FlagTranslation> ThingFlagTable { get; private set; }

		// Doom-format line flag bits the target engine reads, and the line and sector types its config offers
		public int DoomLineFlagBits { get; private set; }
		public bool ReadsWholeTable { get; private set; }
		public HashSet<int> LineTypes { get; private set; }
		public List<KeyValuePair<int, int>> GeneralizedLineRanges { get; private set; } // offset, length
		public HashSet<int> SectorTypes { get; private set; }
		public bool GeneralizedSectors { get; private set; }

		public bool IsBinary { get { return Format != MapFormat.Udmf; } }
		public int MaxElements { get { return IsBinary ? ushort.MaxValue : int.MaxValue; } }

		#endregion

		#region ================== Constructor

		private FormatProfile()
		{
			LineFlagTable = new List<FlagTranslation>();
			ThingFlagTable = new List<FlagTranslation>();
			LineTypes = new HashSet<int>();
			GeneralizedLineRanges = new List<KeyValuePair<int, int>>();
			SectorTypes = new HashSet<int>();
		}

		public static FormatProfile FromConfiguration(string name, Configuration cfg, string xlatfolder)
		{
			// xlatfolder holds the bundled tables (Configurations/Xlat); a config Covers rejects has no profile
			if(!Covers(cfg)) return null;
			FormatProfile p = new FormatProfile();
			p.Name = name;
			p.Engine = cfg.ReadSetting("engine", "");
			p.Format = FormatOf(cfg).Value;

			ReadFlagTable(cfg, "linedefflagstranslation", p.LineFlagTable);
			ReadFlagTable(cfg, "thingflagstranslation", p.ThingFlagTable);

			if(p.Format == MapFormat.Doom)
			{
				string table = cfg.ReadSetting("xlattranslator", "");
				XlatTable xlat = XlatTable.LoadFromFolder(xlatfolder, table);
				p.Lines = new LineXlat(xlat);
				p.Sectors = new SectorXlat(xlat);

				p.ReadsWholeTable = xlatengines.Contains(p.Engine);
				foreach(DictionaryEntry de in cfg.ReadSetting("linedefflags", new Hashtable()))
				{
					int bit;
					if(TryInt(de.Key, out bit)) p.DoomLineFlagBits |= bit;
				}
				if(p.ReadsWholeTable)
				{
					for(int i = 0; i < 16; i++)
						if(xlat.LineFlags[i].Defined && !xlat.LineFlags[i].IsMask) p.DoomLineFlagBits |= 1 << i;
				}

				foreach(DictionaryEntry cat in cfg.ReadSetting("linedeftypes", new Hashtable()))
				{
					if(!(cat.Value is IDictionary)) continue;
					foreach(DictionaryEntry de in (IDictionary)cat.Value)
					{
						int type;
						if(TryInt(de.Key, out type) && de.Value is IDictionary) p.LineTypes.Add(type);
					}
				}
				if(cfg.ReadSetting("generalizedlinedefs", false))
				{
					foreach(DictionaryEntry cat in cfg.ReadSetting("gen_linedeftypes", new Hashtable()))
					{
						if(!(cat.Value is IDictionary)) continue;
						int offset = cfg.ReadSetting("gen_linedeftypes." + cat.Key + ".offset", 0);
						int length = cfg.ReadSetting("gen_linedeftypes." + cat.Key + ".length", 0);
						if(length > 0) p.GeneralizedLineRanges.Add(new KeyValuePair<int, int>(offset, length));
					}
				}

				foreach(DictionaryEntry de in cfg.ReadSetting("sectortypes", new Hashtable()))
				{
					int type;
					if(TryInt(de.Key, out type)) p.SectorTypes.Add(type);
				}
				p.GeneralizedSectors = cfg.ReadSetting("generalizedsectors", false);
			}

			return p;
		}

		public static MapFormat? FormatOf(Configuration cfg)
		{
			switch(cfg.ReadSetting("formatinterface", "").ToLowerInvariant())
			{
				case "doommapsetio": return MapFormat.Doom;
				case "hexenmapsetio": return MapFormat.Hexen;
				case "universalmapsetio": return MapFormat.Udmf;
				default: return null;
			}
		}

		public static bool Covers(Configuration cfg)
		{
			// Without loading a table: a Doom-format config needs xlattranslator, and a UDMF config's engine is its namespace,
			// where Eternity, DSDA and other namespaces have other special sets
			switch(FormatOf(cfg))
			{
				case MapFormat.Doom: return cfg.ReadSetting("xlattranslator", "") != "";
				case MapFormat.Hexen: return true;
				case MapFormat.Udmf: return xlatengines.Contains(cfg.ReadSetting("engine", ""));
				default: return false;
			}
		}

		public static FormatProfile FromConfigurationFile(string path, string xlatfolder)
		{
			Configuration cfg = new Configuration(path, true);
			if(cfg.ErrorResult) throw new InvalidDataException(path + ": " + cfg.ErrorDescription);
			return FromConfiguration(Path.GetFileNameWithoutExtension(path), cfg, xlatfolder);
		}

		private static void ReadFlagTable(Configuration cfg, string key, List<FlagTranslation> list)
		{
			foreach(DictionaryEntry de in cfg.ReadSetting(key, new Hashtable()))
			{
				int bit;
				if(TryInt(de.Key, out bit)) list.Add(new FlagTranslation(de));
			}
			list.Sort();
		}

		private static bool TryInt(object key, out int value)
		{
			return int.TryParse(key.ToString(), NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out value);
		}

		#endregion

		#region ================== Methods

		public bool OffersLineType(int type)
		{
			if(type == 0 || ReadsWholeTable || LineTypes.Contains(type)) return true;
			foreach(KeyValuePair<int, int> r in GeneralizedLineRanges)
				if(type >= r.Key && type < r.Key + r.Value) return true;
			return false;
		}

		public bool OffersSectorType(int type)
		{
			if(type == 0 || ReadsWholeTable) return true;
			int plain = type & ~Sectors.MaskBits;
			if(plain != type && !GeneralizedSectors) return false;
			return plain == 0 || SectorTypes.Contains(plain);
		}

		#endregion
	}
}
