#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// Converts a map's records between formats through the canonical line: source -> canonical -> target
	public static class MapFormatConverter
	{
		#region ================== Constants

		private const int HEXEN_REPEAT = 0x200;
		private const int HEXEN_MONSTERS = 0x2000;
		private const int SPAC_SHIFT = 10;
		internal const int MAX_LISTED_NAMES = 10;

		// Engine line flags (defines.i ML_*) by UDMF name; ML2 values carry the high bit
		private static readonly KeyValuePair<string, int>[] engineflags =
		{
			new KeyValuePair<string, int>("blocking", 0x1),
			new KeyValuePair<string, int>("blockmonsters", 0x2),
			new KeyValuePair<string, int>("twosided", 0x4),
			new KeyValuePair<string, int>("dontpegtop", 0x8),
			new KeyValuePair<string, int>("dontpegbottom", 0x10),
			new KeyValuePair<string, int>("secret", 0x20),
			new KeyValuePair<string, int>("blocksound", 0x40),
			new KeyValuePair<string, int>("dontdraw", 0x80),
			new KeyValuePair<string, int>("mapped", 0x100),
			new KeyValuePair<string, int>("blockplayers", 0x4000),
			new KeyValuePair<string, int>("blockeverything", 0x8000),
			new KeyValuePair<string, int>("zoneboundary", 0x10000),
			new KeyValuePair<string, int>("jumpover", 0x20000),
			new KeyValuePair<string, int>("blockfloaters", 0x40000),
			new KeyValuePair<string, int>("clipmidtex", 0x80000),
			new KeyValuePair<string, int>("wrapmidtex", 0x100000),
			new KeyValuePair<string, int>("midtex3d", 0x200000),
			new KeyValuePair<string, int>("checkswitchrange", 0x400000),
			new KeyValuePair<string, int>("blockprojectiles", 0x1000000),
			new KeyValuePair<string, int>("blockuse", 0x2000000),
			new KeyValuePair<string, int>("blocklandmonsters", unchecked((int)0x80000001)),
			new KeyValuePair<string, int>("translucent", -2), // alpha 0.75, as the UDMF flag gives it
			new KeyValuePair<string, int>("transparent", -3), // alpha 0.25
		};
		private const int ML2_RESERVEDLINEFLAG = 0x2;

		private static readonly string[] doomthingdefaults = { "single", "coop", "dm", "class1", "class2", "class3" };

		// Thing types whose arg0 is a line ID (maploader/slopes.cpp, SlopeLineToPoint)
		private static readonly HashSet<int> lineidthings = new HashSet<int> { 9500, 9501 };

		#endregion

		#region ================== Canonical elements

		private sealed class LineState
		{
			public CanonicalLine Line;
			public Dictionary<string, bool> Flags = new Dictionary<string, bool>(); // UDMF names, set flags only, no activation fields
			public List<int> MoreIds = new List<int>();
			public List<string> Fields;
		}

		private sealed class ThingState
		{
			public ThingRecord Record;
			public Dictionary<string, bool> Flags; // UDMF names; a missing name is false
		}

		#endregion

		#region ================== Convert

		public static MapRecords Convert(MapRecords map, FormatProfile source, FormatProfile target, ConversionReport report)
		{
			// Returns the converted copy; the source records are not changed
			if(source.Format == target.Format) return map.Clone();

			CheckMap(map, source, target, report);

			List<LineState> lines = new List<LineState>(map.Lines.Count);
			for(int i = 0; i < map.Lines.Count; i++) lines.Add(ReadLine(map.Lines[i], i, source, report));

			List<ThingState> things = new List<ThingState>(map.Things.Count);
			for(int i = 0; i < map.Things.Count; i++) things.Add(ReadThing(map.Things[i], i, source, report));

			HashSet<int> referenced = ReferencedLineIds(lines, things, source.Format != MapFormat.Doom);

			MapRecords result = map.Clone();
			for(int i = 0; i < lines.Count; i++) result.Lines[i] = WriteLine(lines[i], i, target, referenced, report);
			for(int i = 0; i < map.Sectors.Count; i++) result.Sectors[i] = ConvertSector(map.Sectors[i], i, source, target, report);
			for(int i = 0; i < things.Count; i++) result.Things[i] = WriteThing(things[i], i, target, report);
			return result;
		}

		private static void CheckMap(MapRecords map, FormatProfile source, FormatProfile target, ConversionReport report)
		{
			if(!target.IsBinary) return;

			Block(report, map.Lines.Count, target.MaxElements, "linedefs");
			Block(report, map.Sectors.Count, target.MaxElements, "sectors");
			Block(report, map.SidedefCount, target.MaxElements, "sidedefs");
			Block(report, map.VertexCount, target.MaxElements, "vertices");
			if(map.OutOfRangeVertices > 0)
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Blocking, map.OutOfRangeVertices + " vertices lie outside -32768..32767, which the format can't store");
			if(map.FractionalVertices > 0)
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Lossy, map.FractionalVertices + " vertices with fractional coordinates are rounded");
			if(map.VertexFields.Count > 0)
			{
				List<string> fields = new List<string>();
				foreach(KeyValuePair<string, int> f in map.VertexFields) fields.Add(f.Key + " on " + f.Value);
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Lossy, "vertex properties the format can't store are dropped: " + string.Join(", ", fields));
			}
			if(map.OutOfRangeThings > 0)
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Blocking, map.OutOfRangeThings + " things lie outside -32768..32767, which the format can't store");
			if(map.FractionalThings > 0)
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Lossy, map.FractionalThings + " things with fractional coordinates are rounded");
			if(map.LongTextureNames.Count > 0)
			{
				// The names list is capped so the message box and the panel stay readable
				int shown = Math.Min(map.LongTextureNames.Count, MAX_LISTED_NAMES);
				string names = string.Join(", ", map.LongTextureNames.GetRange(0, shown));
				if(shown < map.LongTextureNames.Count) names += " (and " + (map.LongTextureNames.Count - shown) + " more)";
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Lossy, "texture names over 8 characters can't be stored: " + names);
			}
			if(map.HasScripts && target.Format == MapFormat.Doom)
				report.Add(ConversionElement.Map, -1, ConversionSeverity.Lossy, "the Doom format has no SCRIPTS or BEHAVIOR lump; the map's scripts are dropped");
		}

		private static void Block(ConversionReport report, int count, int max, string what)
		{
			if(count > max) report.Add(ConversionElement.Map, -1, ConversionSeverity.Blocking, count + " " + what + ", over the format's " + max);
		}

		#endregion

		#region ================== Lines

		private static LineState ReadLine(LineRecord r, int index, FormatProfile p, ConversionReport report)
		{
			LineState s = new LineState { Fields = r.Fields };
			switch(p.Format)
			{
				case MapFormat.Doom:
				{
					s.Line = p.Lines.Translate(r.Action, r.Bits, r.Tags[0]);
					HexenLineCodec.ClearIdArg(s.Line);
					if(r.Action != 0 && s.Line.Special == 0)
						report.Add(ConversionElement.Linedef, index, ConversionSeverity.Lossy, "type " + r.Action + " isn't in the engine's table, which reads it as no special");

					int unknown = 0;
					for(int i = 0; i < 16; i++)
						if((r.Bits & (1 << i)) != 0 && !p.Lines.Table.LineFlags[i].Defined) unknown |= 1 << i;
					if(unknown != 0) report.Drop(ConversionElement.Linedef, index, "flag bits 0x" + unknown.ToString("x") + " (the engine ignores them)");
					if((s.Line.EngineFlags2 & ML2_RESERVEDLINEFLAG) != 0) report.Drop(ConversionElement.Linedef, index, "flag MBF21 reserved bit");

					foreach(KeyValuePair<string, int> f in engineflags)
					{
						bool set;
						if(f.Value == -2) set = s.Line.Alpha == 0.75;
						else if(f.Value == -3) set = s.Line.Alpha == 0.25;
						else if(f.Value < 0) set = (s.Line.EngineFlags2 & f.Value & 0x7fffffff) != 0;
						else set = (s.Line.EngineFlags & f.Value) != 0;
						if(set) s.Flags[f.Key] = true;
					}
					break;
				}

				case MapFormat.Hexen:
				{
					HexenLine h = new HexenLine { Action = r.Action, Spac = (r.Activate >> SPAC_SHIFT) & 7, Repeat = (r.Bits & HEXEN_REPEAT) != 0, Monsters = (r.Bits & HEXEN_MONSTERS) != 0 };
					for(int i = 0; i < 5; i++) h.Args[i] = r.Args[i];
					s.Line = HexenLineCodec.Read(h);
					int left;
					s.Flags = BitsToNames(r.Bits & ~(HEXEN_REPEAT | HEXEN_MONSTERS), p.LineFlagTable, false, out left);
					if(left != 0) report.Drop(ConversionElement.Linedef, index, "flag bits 0x" + left.ToString("x") + " (no UDMF name in the config)");
					break;
				}

				default:
				{
					s.Line = UdmfLineCodec.Read(r.Action, r.Args, r.Flags, r.Tags[0]);
					s.Line.Passthrough = IsSet(r.Flags, "passuse");
					foreach(KeyValuePair<string, bool> f in r.Flags) if(f.Value) s.Flags[f.Key] = true;
					for(int i = 1; i < r.Tags.Count; i++) s.MoreIds.Add(r.Tags[i]);
					break;
				}
			}

			foreach(string name in UdmfLineCodec.FieldNames) s.Flags.Remove(name);

			// Line_SetIdentification's flags: the Hexen reader fills the canonical list, the others the names; each writer reads one
			foreach(string f in s.Line.IdFlags) s.Flags[f] = true;
			s.Line.IdFlags.Clear();
			foreach(string f in Linedef.IdentificationArgFlags)
				if(f != "firstsideonly" && s.Flags.ContainsKey(f)) s.Line.IdFlags.Add(f);

			if(s.Line.Special == 0) s.Line.Args = new int[5]; // the engine's arg0 = tag on Doom lines with no special means nothing
			return s;
		}

		private static LineRecord WriteLine(LineState s, int index, FormatProfile p, HashSet<int> referenced, ConversionReport report)
		{
			CanonicalLine c = s.Line;
			LineRecord r = new LineRecord();
			bool needid = c.LineId != 0 && referenced.Contains(c.LineId);

			if(p.Format == MapFormat.Udmf)
			{
				r.Action = c.Special;
				for(int i = 0; i < 5; i++) r.Args[i] = c.Args[i];
				r.Flags = new Dictionary<string, bool>(s.Flags);
				UdmfLineCodec.Write(c, r.Flags);
				if(c.Passthrough) r.Flags["passuse"] = true;
				r.Tags = new List<int> { c.LineId };
				r.Tags.AddRange(s.MoreIds);
				r.Fields = new List<string>(s.Fields);
				if(c.Special != 0) report.LinesTranslated++;
				return r;
			}

			foreach(int id in s.MoreIds) report.Drop(ConversionElement.Linedef, index, "extra line ID " + id);
			foreach(string f in s.Fields) report.Drop(ConversionElement.Linedef, index, "field " + f);

			if(p.Format == MapFormat.Hexen)
			{
				List<string> issues = new List<string>();
				HexenLine h = HexenLineCodec.Write(c, needid, issues);
				foreach(string issue in issues) report.Add(ConversionElement.Linedef, index, ConversionSeverity.Lossy, issue);

				r.Action = h.Action;
				for(int i = 0; i < 5; i++) r.Args[i] = ToByte(h.Args[i], index, "arg" + i, report);
				r.Activate = h.Spac << SPAC_SHIFT;
				r.Bits = NamesToBits(s.Flags, p.LineFlagTable, IsActivationEntry);
				if(h.Repeat) r.Bits |= HEXEN_REPEAT;
				if(h.Monsters) r.Bits |= HEXEN_MONSTERS;
				Dictionary<string, bool> bitnames = new Dictionary<string, bool>(s.Flags);
				foreach(string f in c.IdFlags) bitnames.Remove(f); // the codec stores these in args and reports what it can't
				ReportLostNames(bitnames, BitsToNames(r.Bits & ~(HEXEN_REPEAT | HEXEN_MONSTERS), p.LineFlagTable, false), ConversionElement.Linedef, index, report);

				if(c.LineId != 0 && !needid && HexenLineCodec.Read(h).LineId != c.LineId)
					report.Drop(ConversionElement.Linedef, index, "line ID no special refers to (scripts may)");
				if(c.Special != 0 && h.Action == c.Special) report.LinesTranslated++;
				return r;
			}

			// Doom
			bool secret = IsSet(s.Flags, "secret");
			DoomLine? d = p.Lines.Find(c, secret, p.OffersLineType);
			int tag = c.LineId;
			if(c.Special != 0 && d == null)
			{
				report.Add(ConversionElement.Linedef, index, ConversionSeverity.Lossy, "special " + c + " has no Doom type the target offers; the line gets none");
			}
			else if(d != null)
			{
				r.Action = d.Value.Type;
				tag = d.Value.Tag;
				r.Bits |= d.Value.Flags & p.DoomLineFlagBits;
				if(d.Value.LostPassthrough || (d.Value.Flags & ~p.DoomLineFlagBits) != 0) report.Add(ConversionElement.Linedef, index, ConversionSeverity.Lossy, "use-through activation written as plain use: the target has no passthrough flag");
				if(c.LineId != 0 && tag != c.LineId)
				{
					if(needid) report.Add(ConversionElement.Linedef, index, ConversionSeverity.Lossy, "line ID " + c.LineId + " lost: the tag holds " + tag);
					else report.Drop(ConversionElement.Linedef, index, "line ID no special refers to (scripts may)");
				}
			}

			// Every Doom line's ID is its tag, so a sector tag can make it the target of a line special
			if(tag != 0 && tag != c.LineId && referenced.Contains(tag))
				report.Add(ConversionElement.Linedef, index, ConversionSeverity.Lossy, "tag " + tag + " also makes it line ID " + tag + ", which a special targets");
			if(r.Action != 0) report.LinesTranslated++;
			r.Tags = new List<int> { ToShort(tag, ConversionElement.Linedef, index, "tag", report) };

			XlatLineFlag[] rules = p.Lines.Table.LineFlags;
			foreach(KeyValuePair<string, int> f in engineflags)
			{
				if(!IsSet(s.Flags, f.Key)) continue;
				int bit = DoomBit(rules, f.Value) & p.DoomLineFlagBits;
				if(bit != 0) r.Bits |= bit;
				else report.Drop(ConversionElement.Linedef, index, "flag " + f.Key);
			}
			if(c.Passthrough && c.Activation != LineActivation.UseThrough)
			{
				int bit = DoomBit(rules, -1) & p.DoomLineFlagBits;
				if(bit != 0) r.Bits |= bit;
			}
			foreach(KeyValuePair<string, bool> f in s.Flags)
				if(f.Value && !IsEngineFlag(f.Key)) report.Drop(ConversionElement.Linedef, index, "flag " + f.Key);
			return r;
		}

		private static int DoomBit(XlatLineFlag[] rules, int engineflag)
		{
			for(int i = 0; i < 16; i++)
				if(rules[i].Defined && !rules[i].IsMask && rules[i].NewValue == engineflag) return 1 << i;
			return 0;
		}

		private static bool IsEngineFlag(string name)
		{
			foreach(KeyValuePair<string, int> f in engineflags) if(f.Key == name) return true;
			return false;
		}

		private static bool IsActivationEntry(FlagTranslation ft)
		{
			foreach(string f in ft.Fields)
				foreach(string a in UdmfLineCodec.FieldNames)
					if(f == a) return true;
			return false;
		}

		private static HashSet<int> ReferencedLineIds(List<LineState> lines, List<ThingState> things, bool thingargs)
		{
			// Line IDs the engine looks up for a special's arg (GetLineIdIterator callers in p_lnspec.cpp, maploader and playsim)
			HashSet<int> ids = new HashSet<int>();
			foreach(LineState s in lines) AddReferences(s.Line.Special, s.Line.Args, ids);

			// Thing specials run the same line specials; slope things name a line in arg0
			if(thingargs)
			{
				foreach(ThingState t in things)
				{
					AddReferences(t.Record.Action, t.Record.Args, ids);
					if(lineidthings.Contains(t.Record.Type)) ids.Add(t.Record.Args[0]);
				}
			}

			// TranslucentLine and Scroll_Texture_Model with arg0 0 mean the line itself
			ids.Remove(0);
			return ids;
		}

		private static void AddReferences(int special, int[] a, HashSet<int> ids)
		{
			switch(special)
			{
				case 16: // Transfer_WallLight
				case 48: // Sector_Attach3dMidtex
				case 52: // Scroll_Wall
				case 53: // Line_SetTextureOffset
				case 55: // Line_SetBlocking
				case 56: // Line_SetTextureScale
				case 150: // Line_SetHealth
				case 156: // Line_SetPortal: the partner line
				case 183: // Line_AlignCeiling
				case 184: // Line_AlignFloor
				case 221: // Scroll_Texture_Both: a negative ID picks the back side
					ids.Add(System.Math.Abs(a[0]));
					break;
				case 208: // TranslucentLine
				case 222: // Scroll_Texture_Model: the scrolling lines carry the model's ID
				case 281: // Line_SetAutomapFlags
				case 282: // Line_SetAutomapStyle
					ids.Add(a[0]);
					break;
				case 57: // Sector_SetPortal, type 5
					if(a[1] == 5) ids.Add(a[0]);
					break;
				case 107: // Line_SetPortalTarget
					ids.Add(a[0]);
					ids.Add(a[1]);
					break;
				case 215: // Teleport_Line
				case 225: // Scroll_Texture_Offsets
					ids.Add(a[1]);
					break;
				case Specials.Static_Init:
					if(a[1] == Specials.Init_SectorLink) ids.Add(a[0]);
					break;
			}
		}

		#endregion

		#region ================== Sectors

		private static SectorRecord ConvertSector(SectorRecord r, int index, FormatProfile source, FormatProfile target, ConversionReport report)
		{
			int special;
			List<int> tags = new List<int>(r.Tags);
			switch(source.Format)
			{
				case MapFormat.Doom: special = source.Sectors.Translate(r.Special); tags[0] = (short)tags[0]; break;
				case MapFormat.Hexen: special = (short)r.Special; tags[0] = (short)tags[0]; break; // the engine reads both as int16
				default: special = r.Special; break;
			}

			SectorRecord s = new SectorRecord();
			if(target.Format == MapFormat.Udmf)
			{
				s.Special = special;
				s.Tags = tags;
				s.Flags = new Dictionary<string, bool>(r.Flags);
				s.Fields = new List<string>(r.Fields);
				if(special != 0) report.SectorsTranslated++;
				return s;
			}

			for(int i = 1; i < tags.Count; i++) report.Drop(ConversionElement.Sector, index, "extra tag " + tags[i]);
			foreach(KeyValuePair<string, bool> f in r.Flags) if(f.Value) report.Drop(ConversionElement.Sector, index, "flag " + f.Key);
			foreach(string f in r.Fields) report.Drop(ConversionElement.Sector, index, "field " + f);
			s.Tags = new List<int> { ToShort(tags[0], ConversionElement.Sector, index, "tag", report) };

			if(target.Format == MapFormat.Hexen)
			{
				s.Special = ToShort(special, ConversionElement.Sector, index, "special", report);
			}
			else if(special != 0)
			{
				int? t = target.Sectors.Find(special, target.GeneralizedSectors || target.ReadsWholeTable);
				if(t != null && target.OffersSectorType(t.Value)) s.Special = t.Value;
				else report.Add(ConversionElement.Sector, index, ConversionSeverity.Lossy, "sector special " + special + " has no Doom type the target offers; the sector gets none");
			}
			if(s.Special != 0) report.SectorsTranslated++;
			return s;
		}

		#endregion

		#region ================== Things

		private static ThingState ReadThing(ThingRecord r, int index, FormatProfile p, ConversionReport report)
		{
			ThingState s = new ThingState { Record = r.Clone() };
			if(p.IsBinary)
			{
				int left;
				s.Flags = ThingNames(r.Bits, p, out left);
				if(left != 0) report.Drop(ConversionElement.Thing, index, "flag bits 0x" + left.ToString("x") + " (no UDMF name in the config)");
				if(p.Format == MapFormat.Hexen) s.Record.Tag = (short)r.Tag; // mapthinghexen_t's tid is int16
			}
			else
			{
				s.Flags = new Dictionary<string, bool>(r.Flags);
			}
			return s;
		}

		private static ThingRecord WriteThing(ThingState s, int index, FormatProfile p, ConversionReport report)
		{
			ThingRecord r = s.Record.Clone();
			if(p.Format == MapFormat.Udmf)
			{
				r.Flags = new Dictionary<string, bool>(s.Flags);
				r.Bits = 0;
				return r;
			}

			r.Flags = new Dictionary<string, bool>();
			foreach(string f in r.Fields) report.Drop(ConversionElement.Thing, index, "field " + f);
			r.Fields = new List<string>();
			r.Bits = NamesToBits(s.Flags, p.ThingFlagTable, null);
			int unused;
			ReportLostNames(s.Flags, ThingNames(r.Bits, p, out unused), ConversionElement.Thing, index, report);

			if(p.Format == MapFormat.Hexen)
			{
				r.Tag = ToShort(r.Tag, ConversionElement.Thing, index, "TID", report);
				if(r.Action < 0 || r.Action > 255)
				{
					report.Add(ConversionElement.Thing, index, ConversionSeverity.Lossy, "special " + r.Action + " doesn't fit Hexen's one-byte action; dropped");
					r.Action = 0;
				}
				for(int i = 0; i < 5; i++) r.Args[i] = ToByte(r.Args[i], index, "arg" + i, report, ConversionElement.Thing);
				if(r.Z < short.MinValue || r.Z > short.MaxValue || r.Z != System.Math.Floor(r.Z))
					report.Add(ConversionElement.Thing, index, ConversionSeverity.Lossy, "height " + r.Z + " doesn't fit Hexen's whole-number int16");
				return r;
			}

			// Doom things have no TID, height, special or args
			List<string> lost = new List<string>();
			if(r.Tag != 0) lost.Add("TID " + r.Tag);
			if(r.Z != 0) lost.Add("height " + r.Z);
			if(r.Action != 0) lost.Add("special " + r.Action + "(" + string.Join(", ", r.Args) + ")");
			if(lost.Count > 0) report.Add(ConversionElement.Thing, index, ConversionSeverity.Lossy, string.Join(", ", lost) + " dropped");
			r.Tag = 0;
			r.Z = 0;
			r.Action = 0;
			r.Args = new int[5];
			return r;
		}

		#endregion

		#region ================== Flag tables

		private static Dictionary<string, bool> BitsToNames(int bits, List<FlagTranslation> table, bool withfalse, out int leftover)
		{
			// As Thing/Linedef.TranslateToUDMF: an entry matches when all its bits are set; withfalse adds the unmatched entries inverted
			Dictionary<string, bool> names = new Dictionary<string, bool>();
			foreach(FlagTranslation f in table)
			{
				bool match = (bits & f.Flag) == f.Flag;
				if(match) bits &= ~f.Flag;
				for(int i = 0; i < f.Fields.Count; i++)
				{
					bool value = match ? f.FieldValues[i] : !f.FieldValues[i];
					if(match || (withfalse && !names.ContainsKey(f.Fields[i]))) names[f.Fields[i]] = value;
				}
			}
			if(!withfalse)
			{
				List<string> unset = new List<string>();
				foreach(KeyValuePair<string, bool> n in names) if(!n.Value) unset.Add(n.Key);
				foreach(string n in unset) names.Remove(n);
			}
			leftover = bits;
			return names;
		}

		private static Dictionary<string, bool> ThingNames(int bits, FormatProfile p, out int leftover)
		{
			// Doom-format things start in every class and game mode (maploader.cpp LoadThings' 0x7e0); table bits only clear them
			Dictionary<string, bool> names = BitsToNames(bits, p.ThingFlagTable, true, out leftover);
			if(p.Format == MapFormat.Doom)
				foreach(string n in doomthingdefaults) if(!names.ContainsKey(n)) names[n] = true;
			return names;
		}

		private static Dictionary<string, bool> BitsToNames(int bits, List<FlagTranslation> table, bool withfalse)
		{
			int left;
			return BitsToNames(bits, table, withfalse, out left);
		}

		private static int NamesToBits(Dictionary<string, bool> names, List<FlagTranslation> table, System.Predicate<FlagTranslation> skip)
		{
			// As TranslateFromUDMF, except a missing name counts as false, which is what UDMF means by it
			int bits = 0;
			foreach(FlagTranslation f in table)
			{
				if(skip != null && skip(f)) continue;
				bool match = true;
				for(int i = 0; i < f.Fields.Count && match; i++) match = IsSet(names, f.Fields[i]) == f.FieldValues[i];
				if(match) bits |= f.Flag;
			}
			return bits;
		}

		private static void ReportLostNames(Dictionary<string, bool> wanted, Dictionary<string, bool> got, ConversionElement element, int index, ConversionReport report)
		{
			HashSet<string> all = new HashSet<string>(wanted.Keys);
			all.UnionWith(got.Keys);
			foreach(string n in all)
			{
				bool w = IsSet(wanted, n), g = IsSet(got, n);
				if(w && !g) report.Drop(element, index, "flag " + n);
				else if(!w && g) report.Drop(element, index, "flag " + n + " (set by the format)");
			}
		}

		#endregion

		#region ================== Helpers

		private static bool IsSet(IDictionary<string, bool> flags, string name)
		{
			bool v;
			return flags.TryGetValue(name, out v) && v;
		}

		private static int ToShort(int value, ConversionElement element, int index, string what, ConversionReport report)
		{
			// Doom and Hexen store tags, TIDs and sector specials in 16 bits, which the engine reads signed
			if(value < short.MinValue || value > ushort.MaxValue)
				report.Add(element, index, ConversionSeverity.Lossy, what + " " + value + " doesn't fit 16 bits; written as " + (value & 0xffff));
			else if(value > short.MaxValue)
				report.Add(element, index, ConversionSeverity.Lossy, what + " " + value + " is read back by the engine as " + (short)value);
			return value & 0xffff;
		}

		private static int ToByte(int value, int index, string what, ConversionReport report, ConversionElement element = ConversionElement.Linedef)
		{
			if(value < 0 || value > 255)
				report.Add(element, index, ConversionSeverity.Lossy, what + " " + value + " doesn't fit Hexen's one byte; written as " + (value & 255));
			return value & 255;
		}

		#endregion
	}
}
