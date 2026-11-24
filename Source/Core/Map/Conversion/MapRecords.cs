#region ================== Namespaces

using System.Collections.Generic;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	// A linedef as one format stores it. Doom and Hexen use Bits (and Activate), UDMF uses Flags.
	public sealed class LineRecord
	{
		public int Action;
		public int[] Args = new int[5];
		public int Activate; // Hexen: the SPAC bits as UDB keeps them (flags & 0x1c00)
		public int Bits; // Doom and Hexen flag bits, activation excluded
		public Dictionary<string, bool> Flags = new Dictionary<string, bool>();
		public List<int> Tags = new List<int> { 0 }; // Doom tag or UDMF id and moreids; Hexen keeps 0
		public List<string> Fields = new List<string>(); // UDMF properties with no binary equivalent, sidedef ones as "front.<name>" / "back.<name>"

		public LineRecord Clone()
		{
			LineRecord r = (LineRecord)MemberwiseClone();
			r.Args = (int[])Args.Clone();
			r.Flags = new Dictionary<string, bool>(Flags);
			r.Tags = new List<int>(Tags);
			r.Fields = new List<string>(Fields);
			return r;
		}
	}

	public sealed class SectorRecord
	{
		public int Special;
		public List<int> Tags = new List<int> { 0 };
		public Dictionary<string, bool> Flags = new Dictionary<string, bool>(); // UDMF only
		public List<string> Fields = new List<string>();

		public SectorRecord Clone()
		{
			SectorRecord r = (SectorRecord)MemberwiseClone();
			r.Tags = new List<int>(Tags);
			r.Flags = new Dictionary<string, bool>(Flags);
			r.Fields = new List<string>(Fields);
			return r;
		}
	}

	public sealed class ThingRecord
	{
		public int Type;
		public int Bits; // Doom and Hexen
		public Dictionary<string, bool> Flags = new Dictionary<string, bool>(); // UDMF
		public int Tag; // TID
		public double Z;
		public int Action;
		public int[] Args = new int[5];
		public List<string> Fields = new List<string>();

		public ThingRecord Clone()
		{
			ThingRecord r = (ThingRecord)MemberwiseClone();
			r.Args = (int[])Args.Clone();
			r.Flags = new Dictionary<string, bool>(Flags);
			r.Fields = new List<string>(Fields);
			return r;
		}
	}

	// The parts of a map the converter reads and writes, plus the facts its downgrade checks need
	public sealed class MapRecords
	{
		public List<LineRecord> Lines = new List<LineRecord>();
		public List<SectorRecord> Sectors = new List<SectorRecord>();
		public List<ThingRecord> Things = new List<ThingRecord>();

		public int VertexCount;
		public int SidedefCount;
		public int FractionalVertices; // vertices whose coordinates aren't whole numbers
		public int OutOfRangeVertices; // vertices outside -32768..32767
		public SortedDictionary<string, int> VertexFields = new SortedDictionary<string, int>(); // UDMF vertex property (zfloor, zceiling, fields) -> vertices with it
		public int FractionalThings; // things whose x or y isn't a whole number
		public int OutOfRangeThings; // things outside -32768..32767
		public List<string> LongTextureNames = new List<string>(); // over 8 characters, each once
		public bool HasScripts; // a SCRIPTS or BEHAVIOR lump

		public MapRecords Clone()
		{
			MapRecords m = (MapRecords)MemberwiseClone();
			m.Lines = Lines.ConvertAll(r => r.Clone());
			m.Sectors = Sectors.ConvertAll(r => r.Clone());
			m.Things = Things.ConvertAll(r => r.Clone());
			m.VertexFields = new SortedDictionary<string, int>(VertexFields);
			m.LongTextureNames = new List<string>(LongTextureNames);
			return m;
		}
	}
}
