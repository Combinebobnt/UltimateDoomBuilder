using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CodeImp.DoomBuilder.ZDoom
{
	internal enum EmissiveSurface { Walls, Flats }

	// One EMITDEFS line for one surface kind; a "both" line gives one entry per kind
	internal sealed class EmitdefsEntry
	{
		public const int AUTO_COLOR = -1;

		public string Name;
		public EmissiveSurface Kind;
		public int Color = 0xFFFFFF; // 0xRRGGBB, or AUTO_COLOR
		public double Intensity = 1.0;
		public double Distance;
		public string Source;
		public int Line;

		// An entry that removes an inherited one rather than emitting
		public bool IsRemoval { get { return Intensity <= 0 || Color == 0; } }
	}

	// EMITDEFS: a texture name, then keyword/value pairs (color, intensity, distance, on). The format
	// is the baker's --emissive file plus "color auto"; problems warn and never stop the load.
	internal static class EmitdefsParser
	{
		public const double MAX_INTENSITY = 16.0;

		internal static List<EmitdefsEntry> Parse(string text, string source, Action<string> warning)
		{
			var result = new List<EmitdefsEntry>();
			using(StringReader reader = new StringReader(text ?? ""))
			{
				string line;
				int number = 0;
				while((line = reader.ReadLine()) != null)
				{
					number++;
					int n = number;
					ParseLine(line, source, n, result, message => { if(warning != null) warning(source + ", line " + n + ": " + message); });
				}
			}
			return result;
		}

		private static void ParseLine(string line, string source, int number, List<EmitdefsEntry> result, Action<string> warn)
		{
			List<string> tokens = Tokenize(line);
			if(tokens == null) { warn("unterminated quote, line skipped"); return; }
			if(tokens.Count == 0) return;
			if(tokens[0].Length == 0) { warn("empty texture name, line skipped"); return; }

			var entry = new EmitdefsEntry { Name = tokens[0], Source = source, Line = number };
			bool walls = true, flats = true, distance = false; // a repeated "on" keeps the last, as in the baker
			for(int i = 1; i < tokens.Count; i += 2)
			{
				string key = tokens[i];
				if(i + 1 >= tokens.Count) { warn("no value for \"" + key + "\", line skipped"); return; }
				string value = tokens[i + 1];
				switch(key)
				{
					case "color":
						if(value == "auto") entry.Color = EmitdefsEntry.AUTO_COLOR;
						else if(!TryHexColor(value, out entry.Color)) { warn("color \"" + value + "\" is not 6 hex digits or auto, line skipped"); return; }
						break;
					case "intensity":
						if(!TryNumber(value, out entry.Intensity) || entry.Intensity < 0) { warn("intensity \"" + value + "\" is not a number of 0 or more, line skipped"); return; }
						entry.Intensity = Math.Min(entry.Intensity, MAX_INTENSITY);
						break;
					case "distance":
						if(!TryNumber(value, out entry.Distance) || entry.Distance <= 0) { warn("distance \"" + value + "\" is not a number above 0, line skipped"); return; }
						distance = true;
						break;
					case "on":
						if(value == "walls") { walls = true; flats = false; }
						else if(value == "flats") { walls = false; flats = true; }
						else if(value == "both") walls = flats = true;
						else { warn("on \"" + value + "\" is not walls, flats or both, line skipped"); return; }
						break;
					default:
						warn("unknown key \"" + key + "\" ignored");
						break;
				}
			}
			// A removal (intensity 0 or color 000000) needs no distance
			if(!distance && !entry.IsRemoval) { warn("no distance, line skipped"); return; }

			if(walls) result.Add(Copy(entry, EmissiveSurface.Walls));
			if(flats) result.Add(Copy(entry, EmissiveSurface.Flats));
		}

		private static EmitdefsEntry Copy(EmitdefsEntry e, EmissiveSurface kind)
		{
			return new EmitdefsEntry { Name = e.Name, Kind = kind, Color = e.Color, Intensity = e.Intensity, Distance = e.Distance, Source = e.Source, Line = e.Line };
		}

		// Null on an unterminated quote; // outside quotes starts a comment
		private static List<string> Tokenize(string line)
		{
			var tokens = new List<string>();
			int i = 0;
			while(i < line.Length)
			{
				char c = line[i];
				if(c == ' ' || c == '\t' || c == '\r') i++;
				else if(c == '/' && i + 1 < line.Length && line[i + 1] == '/') break;
				else if(c == '"')
				{
					int end = line.IndexOf('"', i + 1);
					if(end < 0) return null;
					tokens.Add(line.Substring(i + 1, end - i - 1));
					i = end + 1;
				}
				else
				{
					int end = line.IndexOfAny(new[] { ' ', '\t', '\r', '"' }, i);
					if(end < 0) end = line.Length;
					tokens.Add(line.Substring(i, end - i));
					i = end;
				}
			}
			return tokens;
		}

		private static bool TryNumber(string s, out double value)
		{
			return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
		}

		private static bool TryHexColor(string s, out int color)
		{
			color = 0;
			if(s.Length != 6) return false;
			foreach(char c in s)
				if(!Uri.IsHexDigit(c)) return false;
			color = int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			return true;
		}
	}
}
