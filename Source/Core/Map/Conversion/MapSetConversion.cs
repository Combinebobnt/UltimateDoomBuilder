#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// A format conversion planned on a MapSet: the records are read and converted up front, and Apply writes them back after the config switch
	public sealed class MapSetConversion
	{
		#region ================== Variables

		private readonly MapRecords result;

		#endregion

		#region ================== Properties

		public FormatProfile Source { get; private set; }
		public FormatProfile Target { get; private set; }
		public ConversionReport Report { get; private set; }

		#endregion

		#region ================== Constructor

		private MapSetConversion(FormatProfile source, FormatProfile target, MapRecords result, ConversionReport report)
		{
			Source = source;
			Target = target;
			this.result = result;
			Report = report;
		}

		// Null when either config has no profile (a namespace or Doom-format config the converter doesn't cover)
		internal static MapSetConversion Plan(MapSet map, GameConfiguration sourceconfig, ConfigurationInfo source, ConfigurationInfo target, bool hasscripts)
		{
			FormatProfile from = Profile(source), to = Profile(target);
			if(from == null || to == null) return null;

			MapRecords records = Read(map, from.Format, sourceconfig.LinedefActivationsFilter);
			records.HasScripts = hasscripts;
			ConversionReport report = new ConversionReport();
			return new MapSetConversion(from, to, MapFormatConverter.Convert(records, from, to, report), report);
		}

		private static FormatProfile Profile(ConfigurationInfo info)
		{
			try
			{
				return FormatProfile.FromConfiguration(info.Name, info.Configuration, Path.Combine(General.ConfigsPath, "Xlat"));
			}
			catch(Exception e)
			{
				General.ErrorLogger.Add(ErrorType.Warning, "Map conversion is unavailable for \"" + info.Name + "\": " + e.Message);
				return null;
			}
		}

		#endregion

		#region ================== Read

		private static MapRecords Read(MapSet map, MapFormat format, int activationfilter)
		{
			MapRecords m = new MapRecords { VertexCount = map.Vertices.Count, SidedefCount = map.Sidedefs.Count };
			HashSet<string> longnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach(Vertex v in map.Vertices)
			{
				Vector2D p = v.Position;
				if(p.x != Math.Floor(p.x) || p.y != Math.Floor(p.y)) m.FractionalVertices++;
				if(p.x < short.MinValue || p.x > short.MaxValue || p.y < short.MinValue || p.y > short.MaxValue) m.OutOfRangeVertices++;
			}

			foreach(Linedef l in map.Linedefs)
			{
				LineRecord r = new LineRecord { Action = l.Action };
				switch(format)
				{
					case MapFormat.Doom:
						r.Bits = RawBits(l.RawFlags, l.Flags);
						r.Tags = new List<int> { l.Tags[0] };
						break;
					case MapFormat.Hexen:
						r.Bits = RawBits(l.RawFlags, l.Flags) & ~activationfilter;
						r.Activate = l.Activate & activationfilter;
						l.Args.CopyTo(r.Args, 0);
						break;
					default:
						r.Flags = new Dictionary<string, bool>(l.Flags);
						r.Tags = new List<int>(l.Tags);
						l.Args.CopyTo(r.Args, 0);
						r.Fields.AddRange(l.Fields.Keys);
						if(l.Front != null) AddSidedefFields(l.Front, r.Fields);
						if(l.Back != null) AddSidedefFields(l.Back, r.Fields);
						break;
				}
				m.Lines.Add(r);
			}

			foreach(Sidedef sd in map.Sidedefs)
			{
				AddLongName(sd.HighTexture, longnames, m);
				AddLongName(sd.MiddleTexture, longnames, m);
				AddLongName(sd.LowTexture, longnames, m);
			}

			foreach(Sector s in map.Sectors)
			{
				SectorRecord r = new SectorRecord { Special = s.Effect, Tags = new List<int>(s.Tags) };
				if(format == MapFormat.Udmf)
				{
					r.Flags = new Dictionary<string, bool>(s.Flags);
					foreach(string f in s.Fields.Keys)
						if(f != MapSet.VirtualSectorField) r.Fields.Add(f);
					if(s.FloorSlope.GetLengthSq() > 0) r.Fields.Add("floorplane");
					if(s.CeilSlope.GetLengthSq() > 0) r.Fields.Add("ceilingplane");
				}
				m.Sectors.Add(r);
				AddLongName(s.FloorTexture, longnames, m);
				AddLongName(s.CeilTexture, longnames, m);
			}

			foreach(Thing t in map.Things)
			{
				ThingRecord r = new ThingRecord { Type = t.Type, Tag = t.Tag, Z = t.Position.z, Action = t.Action };
				t.Args.CopyTo(r.Args, 0);
				if(format == MapFormat.Udmf)
				{
					r.Flags = new Dictionary<string, bool>(t.Flags);
					r.Fields.AddRange(t.Fields.Keys);
					if(t.Pitch != 0) r.Fields.Add("pitch");
					if(t.Roll != 0) r.Fields.Add("roll");
					if(t.ScaleX != 1.0 || t.ScaleY != 1.0) r.Fields.Add("scale");
				}
				else
				{
					r.Bits = RawBits(t.RawFlags, t.Flags);
				}
				m.Things.Add(r);
			}

			return m;
		}

		private static int RawBits(ushort raw, Dictionary<string, bool> flags)
		{
			// As UpdateRawFlagsFromFlags: the dictionary wins for the bits the config names, the raw word keeps the rest
			int bits = raw;
			foreach(KeyValuePair<string, bool> f in flags)
			{
				ushort bit;
				if(!ushort.TryParse(f.Key, out bit)) continue;
				bits &= ~bit;
				if(f.Value) bits |= bit;
			}
			return bits;
		}

		private static void AddSidedefFields(Sidedef sd, List<string> fields)
		{
			// TranslateFromUDMF folds one part's offsets into the sidedef's own, so those two aren't lost
			string folded = sd.FoldedOffsetPart();
			foreach(string f in sd.Fields.Keys)
				if(folded == null || (f != "offsetx_" + folded && f != "offsety_" + folded)) fields.Add("sidedef." + f);
			foreach(KeyValuePair<string, bool> f in sd.Flags)
				if(f.Value) fields.Add("sidedef." + f.Key);
		}

		private static void AddLongName(string name, HashSet<string> seen, MapRecords m)
		{
			if(name != null && name.Length > 8 && seen.Add(name)) m.LongTextureNames.Add(name);
		}

		#endregion

		#region ================== Apply

		// Runs after the target configuration and its map format interface are in place, so element flags use the new config's keys
		internal void Apply(MapSet map, GameConfiguration config)
		{
			bool binary = Target.IsBinary;
			bool fromudmf = Source.Format == MapFormat.Udmf;

			int index = 0;
			foreach(Linedef l in map.Linedefs)
			{
				LineRecord r = result.Lines[index++];
				if(binary)
				{
					if(fromudmf)
					{
						l.Fields.Clear();
						l.ExtraFloorFlag = false;
					}
					int raw = r.Bits | r.Activate;
					l.Update(BitsToKeys(raw, config.LinedefFlags), (ushort)raw, r.Activate, r.Tags, r.Action, r.Args);
				}
				else
				{
					l.Update(r.Flags, 0, 0, r.Tags, r.Action, r.Args);
				}
			}

			if(binary && fromudmf)
				foreach(Sidedef sd in map.Sidedefs) sd.TranslateFromUDMF();

			index = 0;
			foreach(Sector s in map.Sectors)
			{
				SectorRecord r = result.Sectors[index++];
				if(binary && fromudmf) s.TranslateFromUDMF();
				s.Effect = r.Special;
				s.Tags = new List<int>(r.Tags);
				if(!binary)
				{
					s.Flags.Clear();
					foreach(KeyValuePair<string, bool> f in r.Flags) s.Flags[f.Key] = f.Value;
				}
				s.UpdateNeeded = true;
			}

			index = 0;
			foreach(Thing t in map.Things)
			{
				ThingRecord r = result.Things[index++];
				if(binary && fromudmf) t.ClearUDMFProperties();
				Dictionary<string, bool> flags = binary ? BitsToKeys(r.Bits, config.ThingFlags) : r.Flags;
				t.SetConvertedProperties(flags, (ushort)(binary ? r.Bits : 0), r.Tag, r.Z, r.Action, r.Args);
			}
		}

		private static Dictionary<string, bool> BitsToKeys(int bits, IDictionary<string, string> configflags)
		{
			// As UpdateFlagsFromRawFlags, for the new config's numeric flag keys
			Dictionary<string, bool> flags = new Dictionary<string, bool>(StringComparer.Ordinal);
			foreach(string key in configflags.Keys)
			{
				ushort bit;
				if(ushort.TryParse(key, out bit)) flags[key] = (bits & bit) == bit;
			}
			return flags;
		}

		// Each issue and each element's dropped properties as one Errors and Warnings entry; an element entry selects it on click
		internal void LogReport(MapSet map)
		{
			foreach(ConversionIssue i in Report.Issues)
			{
				string message = "Map conversion (" + i.Severity.ToString().ToLowerInvariant() + "): " + i;
				MapElement e = Element(map, i.Element, i.Index);
				if(e != null) General.ErrorLogger.Add(new MapElementErrorItem(ErrorType.Warning, e, message));
				else General.ErrorLogger.Add(ErrorType.Warning, message);
			}

			Dictionary<KeyValuePair<ConversionElement, int>, List<string>> perelement = new Dictionary<KeyValuePair<ConversionElement, int>, List<string>>();
			foreach(DroppedProperty p in Report.Dropped.Values)
			{
				foreach(int index in p.Indices)
				{
					KeyValuePair<ConversionElement, int> key = new KeyValuePair<ConversionElement, int>(p.Element, index);
					List<string> names;
					if(!perelement.TryGetValue(key, out names)) perelement[key] = names = new List<string>();
					names.Add(p.Name);
				}
			}
			foreach(KeyValuePair<KeyValuePair<ConversionElement, int>, List<string>> d in perelement)
			{
				string message = "Map conversion dropped from " + d.Key.Key + " " + d.Key.Value + ": " + string.Join(", ", d.Value);
				MapElement e = Element(map, d.Key.Key, d.Key.Value);
				if(e != null) General.ErrorLogger.Add(new MapElementErrorItem(ErrorType.Warning, e, message));
				else General.ErrorLogger.Add(ErrorType.Warning, message);
			}
		}

		private static MapElement Element(MapSet map, ConversionElement element, int index)
		{
			switch(element)
			{
				case ConversionElement.Linedef: return index >= 0 && index < map.Linedefs.Count ? map.GetLinedefByIndex(index) : null;
				case ConversionElement.Sector: return index >= 0 && index < map.Sectors.Count ? map.GetSectorByIndex(index) : null;
				case ConversionElement.Thing: return index >= 0 && index < map.Things.Count ? map.GetThingByIndex(index) : null;
				default: return null;
			}
		}

		// The summary, then the first issues and dropped groups, for a message box
		public string Describe(int maxlines)
		{
			List<string> lines = new List<string>();
			foreach(ConversionIssue i in Report.Issues)
				if(i.Severity != ConversionSeverity.Note) lines.Add(i.ToString());
			foreach(DroppedProperty p in Report.Dropped.Values) lines.Add("Dropped: " + p);

			string s = Report.Summary();
			for(int i = 0; i < lines.Count && i < maxlines; i++) s += Environment.NewLine + lines[i];
			if(lines.Count > maxlines) s += Environment.NewLine + "(" + (lines.Count - maxlines) + " more)";
			return s;
		}

		#endregion
	}
}
