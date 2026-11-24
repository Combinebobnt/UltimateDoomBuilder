#region ================== Namespaces

using System.Collections.Generic;
using System.Text;

#endregion

namespace CodeImp.DoomBuilder.Map.Conversion
{
	public enum ConversionElement
	{
		Map,
		Linedef,
		Sector,
		Thing,
	}

	public enum ConversionSeverity
	{
		Note, // changed but plays the same, or something the engine still handles
		Lossy, // something the source map had is gone or behaves differently
		Blocking, // the target format can't hold the map at all
	}

	public sealed class ConversionIssue
	{
		public ConversionElement Element;
		public int Index; // -1 for the whole map
		public ConversionSeverity Severity;
		public string Message;

		public override string ToString()
		{
			return (Element == ConversionElement.Map ? "Map" : Element + " " + Index) + ": " + Message;
		}
	}

	// A property many elements may lose at once (a UDMF field, a flag), listed once with the elements
	public sealed class DroppedProperty
	{
		public ConversionElement Element;
		public string Name;
		public List<int> Indices = new List<int>();

		public override string ToString()
		{
			return Element.ToString().ToLowerInvariant() + " " + Name + " on " + Indices.Count + (Indices.Count == 1 ? " element" : " elements");
		}
	}

	public sealed class ConversionReport
	{
		#region ================== Properties

		public List<ConversionIssue> Issues { get; } = new List<ConversionIssue>();

		// Keyed "<element> <name>", e.g. "Sector field lightfloor"
		public SortedDictionary<string, DroppedProperty> Dropped { get; } = new SortedDictionary<string, DroppedProperty>();

		public int LinesTranslated; // lines with a special in the target
		public int SectorsTranslated; // sectors with a special in the target

		public bool Blocked
		{
			get
			{
				foreach(ConversionIssue i in Issues) if(i.Severity == ConversionSeverity.Blocking) return true;
				return false;
			}
		}

		public int LossyCount
		{
			get
			{
				int n = 0;
				foreach(ConversionIssue i in Issues) if(i.Severity == ConversionSeverity.Lossy) n++;
				return n;
			}
		}

		#endregion

		#region ================== Methods

		public void Add(ConversionElement element, int index, ConversionSeverity severity, string message)
		{
			Issues.Add(new ConversionIssue { Element = element, Index = index, Severity = severity, Message = message });
		}

		public void Drop(ConversionElement element, int index, string name)
		{
			string key = element + " " + name;
			DroppedProperty p;
			if(!Dropped.TryGetValue(key, out p)) Dropped[key] = p = new DroppedProperty { Element = element, Name = name };
			p.Indices.Add(index);
		}

		public bool HasDropped(ConversionElement element, string name)
		{
			return Dropped.ContainsKey(element + " " + name);
		}

		public string Summary()
		{
			string s = LinesTranslated + " linedefs and " + SectorsTranslated + " sectors with specials, " + LossyCount + " lossy";
			if(Dropped.Count > 0) s += ", " + Dropped.Count + " properties dropped";
			if(Blocked) s += "; the target format can't hold this map";
			return s;
		}

		public override string ToString()
		{
			StringBuilder sb = new StringBuilder(Summary()).AppendLine();
			foreach(ConversionIssue i in Issues) sb.AppendLine(i.Severity + ": " + i);
			foreach(DroppedProperty p in Dropped.Values) sb.AppendLine("Dropped: " + p);
			return sb.ToString();
		}

		#endregion
	}
}
