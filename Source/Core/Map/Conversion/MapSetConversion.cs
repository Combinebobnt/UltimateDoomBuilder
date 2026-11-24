#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Geometry;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// A format conversion planned on a MapSet: the records are read and converted up front, and Apply writes them back after the config switch
	public sealed class MapSetConversion
	{
		#region ================== Constants

		private const int LOG_CAP = 50; // Errors and Warnings entries per issue kind and per element kind

		#endregion

		#region ================== Variables

		private readonly MapRecords result;
		private readonly List<string> foldedparts; // per sidedef, the part whose UDMF offsets fold into its own, read while the source format is current

		#endregion

		#region ================== Properties

		public FormatProfile Source { get; private set; }
		public FormatProfile Target { get; private set; }
		public ConversionReport Report { get; private set; }

		#endregion

		#region ================== Constructor

		private MapSetConversion(FormatProfile source, FormatProfile target, MapRecords result, List<string> foldedparts, ConversionReport report)
		{
			Source = source;
			Target = target;
			this.result = result;
			this.foldedparts = foldedparts;
			Report = report;
		}

		internal static MapSetConversion Plan(MapSet map, GameConfiguration sourceconfig, ConfigurationInfo source, ConfigurationInfo target, bool hasscripts, out string unavailable)
		{
			// Null when either config has no profile (a namespace or Doom-format config the converter doesn't cover); unavailable then holds a load error, if any
			unavailable = null;
			FormatProfile from = Profile(source, ref unavailable), to = Profile(target, ref unavailable);
			if(from == null || to == null) return null;

			List<string> folded;
			MapRecords records = Read(map, from.Format, sourceconfig.LinedefActivationsFilter, out folded);
			records.HasScripts = hasscripts;
			ConversionReport report = new ConversionReport();
			return new MapSetConversion(from, to, MapFormatConverter.Convert(records, from, to, report), folded, report);
		}

		private static FormatProfile Profile(ConfigurationInfo info, ref string unavailable)
		{
			try
			{
				return FormatProfile.FromConfiguration(info.Name, info.Configuration, Path.Combine(General.ConfigsPath, "Xlat"));
			}
			catch(Exception e)
			{
				// Map Options clears the Errors panel when it applies, so the caller shows this instead
				if(unavailable == null) unavailable = "\"" + info.Name + "\": " + e.Message;
				General.WriteLogLine("Map conversion is unavailable for \"" + info.Name + "\": " + e.Message);
				return null;
			}
		}

		#endregion

		#region ================== Read

		private static MapRecords Read(MapSet map, MapFormat format, int activationfilter, out List<string> folded)
		{
			MapRecords m = new MapRecords { VertexCount = map.Vertices.Count, SidedefCount = map.Sidedefs.Count };
			HashSet<string> longnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool udmf = format == MapFormat.Udmf;

			foreach(Vertex v in map.Vertices)
			{
				Vector2D p = v.Position;
				if(p.x != Math.Floor(p.x) || p.y != Math.Floor(p.y)) m.FractionalVertices++;
				if(OutOfRange(Math.Round(p.x)) || OutOfRange(Math.Round(p.y))) m.OutOfRangeVertices++; // the binary writers round vertices
				if(!udmf) continue;
				if(!double.IsNaN(v.ZFloor)) Count(m.VertexFields, "zfloor");
				if(!double.IsNaN(v.ZCeiling)) Count(m.VertexFields, "zceiling");
				foreach(string f in v.Fields.Keys) Count(m.VertexFields, f);
			}

			// TranslateFromUDMF's choice depends on the current format, so it is made now and replayed in Apply
			folded = new List<string>(map.Sidedefs.Count);
			Dictionary<Sidedef, string> parts = new Dictionary<Sidedef, string>();
			foreach(Sidedef sd in map.Sidedefs)
			{
				string part = udmf ? sd.FoldedOffsetPart() : null;
				folded.Add(part);
				parts[sd] = part;
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
						r.Bits = HexenBits(l.RawFlags, l.Flags, activationfilter);
						r.Activate = l.Activate & activationfilter;
						l.Args.CopyTo(r.Args, 0);
						break;
					default:
						r.Flags = new Dictionary<string, bool>(l.Flags);
						r.Tags = new List<int>(l.Tags);
						l.Args.CopyTo(r.Args, 0);
						r.Fields.AddRange(l.Fields.Keys);
						if(l.Front != null) AddSidedefFields(l.Front, "front.", parts[l.Front], r.Fields);
						if(l.Back != null) AddSidedefFields(l.Back, "back.", parts[l.Back], r.Fields);
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
				if(udmf)
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
				Vector3D p = t.Position;
				if(p.x != Math.Floor(p.x) || p.y != Math.Floor(p.y)) m.FractionalThings++;
				if(OutOfRange(Math.Truncate(p.x)) || OutOfRange(Math.Truncate(p.y))) m.OutOfRangeThings++; // and cut things toward zero
				if(udmf)
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

		private static bool OutOfRange(double v)
		{
			return v < short.MinValue || v > short.MaxValue;
		}

		private static void Count(SortedDictionary<string, int> counts, string name)
		{
			int n;
			counts.TryGetValue(name, out n);
			counts[name] = n + 1;
		}

		internal static int RawBits(ushort raw, Dictionary<string, bool> flags)
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

		internal static int HexenBits(ushort raw, Dictionary<string, bool> flags, int activationfilter)
		{
			// The SPAC bits travel in LineRecord.Activate, not with the flags
			return RawBits(raw, flags) & ~activationfilter;
		}

		private static void AddSidedefFields(Sidedef sd, string side, string folded, List<string> fields)
		{
			// TranslateFromUDMF folds one part's offsets into the sidedef's own, so those two aren't lost
			foreach(string f in sd.Fields.Keys)
				if(folded == null || (f != "offsetx_" + folded && f != "offsety_" + folded)) fields.Add(side + f);
			foreach(KeyValuePair<string, bool> f in sd.Flags)
				if(f.Value) fields.Add(side + f.Key);
		}

		private static void AddLongName(string name, HashSet<string> seen, MapRecords m)
		{
			if(name != null && name.Length > 8 && seen.Add(name)) m.LongTextureNames.Add(name);
		}

		#endregion

		#region ================== Apply

		internal bool Fits(MapSet map)
		{
			// The records are written back by index, so the map must not have changed since Plan
			return map.Linedefs.Count == result.Lines.Count && map.Sectors.Count == result.Sectors.Count && map.Things.Count == result.Things.Count
			       && map.Sidedefs.Count == foldedparts.Count && map.Vertices.Count == result.VertexCount;
		}

		internal void Apply(MapSet map, GameConfiguration config)
		{
			// Runs after the target configuration and its map format interface are in place, so element flags use the new config's keys
			if(!Fits(map)) throw new InvalidOperationException("The map changed after its format conversion was planned");
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
			{
				index = 0;
				foreach(Sidedef sd in map.Sidedefs) sd.TranslateFromUDMF(foldedparts[index++]);

				foreach(Vertex v in map.Vertices)
				{
					v.ZFloor = double.NaN;
					v.ZCeiling = double.NaN;
					v.Fields.Clear();
				}
			}

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

		internal static Dictionary<string, bool> BitsToKeys(int bits, IDictionary<string, string> configflags)
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

		internal void LogReport(MapSet map)
		{
			// The full report, with every element that lost each property, goes to the log file once; the Errors and Warnings panel
			// gets at most LOG_CAP entries per issue kind and per element kind, each element entry selecting it on click
			General.WriteLogLine("Map conversion report:" + Environment.NewLine + FullReport());

			Dictionary<string, int> perkind = new Dictionary<string, int>();
			foreach(ConversionIssue i in Report.Issues)
			{
				string kind = i.Severity.ToString().ToLowerInvariant() + " " + i.Element.ToString().ToLowerInvariant();
				if(Counted(perkind, kind) <= LOG_CAP) LogElement(map, i.Element, i.Index, "Map conversion (" + i.Severity.ToString().ToLowerInvariant() + "): " + i);
			}
			foreach(KeyValuePair<string, int> k in perkind)
				if(k.Value > LOG_CAP) General.ErrorLogger.Add(ErrorType.Warning, "Map conversion: " + (k.Value - LOG_CAP) + " more " + k.Key + " issues are listed in the log file");

			Dictionary<KeyValuePair<ConversionElement, int>, List<string>> perelement = new Dictionary<KeyValuePair<ConversionElement, int>, List<string>>();
			List<KeyValuePair<ConversionElement, int>> order = new List<KeyValuePair<ConversionElement, int>>();
			foreach(DroppedProperty p in Report.Dropped.Values)
			{
				foreach(int index in p.Indices)
				{
					KeyValuePair<ConversionElement, int> key = new KeyValuePair<ConversionElement, int>(p.Element, index);
					List<string> names;
					if(!perelement.TryGetValue(key, out names))
					{
						perelement[key] = names = new List<string>();
						order.Add(key);
					}
					if(!names.Contains(p.Name)) names.Add(p.Name);
				}
			}
			Dictionary<string, int> perelementkind = new Dictionary<string, int>();
			foreach(KeyValuePair<ConversionElement, int> key in order)
				if(Counted(perelementkind, key.Key.ToString().ToLowerInvariant()) <= LOG_CAP)
					LogElement(map, key.Key, key.Value, "Map conversion dropped from " + key.Key + " " + key.Value + ": " + string.Join(", ", perelement[key]));
			foreach(KeyValuePair<string, int> k in perelementkind)
				if(k.Value > LOG_CAP) General.ErrorLogger.Add(ErrorType.Warning, "Map conversion dropped properties from " + (k.Value - LOG_CAP) + " more " + k.Key + "s, listed in the log file");
		}

		private static int Counted(Dictionary<string, int> counts, string key)
		{
			int n;
			counts.TryGetValue(key, out n);
			counts[key] = ++n;
			return n;
		}

		private static void LogElement(MapSet map, ConversionElement element, int index, string message)
		{
			MapElement e = Element(map, element, index);
			if(e != null) General.ErrorLogger.Add(new MapElementErrorItem(ErrorType.Warning, e, message));
			else General.ErrorLogger.Add(ErrorType.Warning, message);
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

		public string FullReport()
		{
			// The report plus every element that loses each dropped property
			StringBuilder sb = new StringBuilder(Report.ToString());
			foreach(DroppedProperty p in Report.Dropped.Values)
				sb.AppendLine("Dropped " + p.Element.ToString().ToLowerInvariant() + " " + p.Name + " from: " + string.Join(", ", p.Indices));
			return sb.ToString();
		}

		public string Describe(int maxlines)
		{
			// The summary, then the first issues and dropped groups, for a message box
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
