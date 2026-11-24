#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Types;

#endregion

namespace CodeImp.DoomBuilder.Compilers
{
	// The map's bake lighting as the dialog edits it: the ZDRayInfo thing's keys, angle and pitch, or the
	// baker's defaults when the map has no such thing
	internal sealed class LightmapLighting
	{
		#region ================== Constants

		public const int INFO_THING_TYPE = 9890;
		public const int DEFAULT_ANGLE = 214;    // the nearest whole degrees to the baker's default sun vector
		public const int DEFAULT_ELEVATION = 59;

		private const string SUN = "sun";
		private const string SUN_INTENSITY = "sunintensity";
		private const string SUN_KEY = "lm_sunintensity";

		#endregion

		#region ================== Variables

		private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

		#endregion

		#region ================== Properties

		// Every option the ZDRayInfo thing carries, whatever tab shows it
		public static IEnumerable<LightmapBakeOption> Options
		{
			get
			{
				return LightmapBakeOptions.All.Where(o => o.Kind == LightmapOptionKind.MapKey || o.Kind == LightmapOptionKind.ThingAngle || o.Kind == LightmapOptionKind.ThingPitch);
			}
		}

		public bool HasThing { get; private set; }
		public object this[string id] { get { return values[id]; } set { values[id] = value; } }
		public int Angle { get { return (int)values["sunangle"]; } }
		public int Elevation { get { return (int)values["sunelevation"]; } }

		#endregion

		#region ================== Constructor

		private LightmapLighting() { }

		// fields null: no ZDRayInfo thing, so every value is the baker's default
		public static LightmapLighting Read(UniFields fields, int angle, int pitch)
		{
			LightmapLighting l = new LightmapLighting { HasThing = (fields != null) };
			foreach(LightmapBakeOption o in Options) l.values[o.Id] = o.Default;
			if(fields == null) return l;

			foreach(LightmapBakeOption o in Options)
			{
				if(o.Kind != LightmapOptionKind.MapKey || o.Id == SUN || !fields.ContainsKey(o.Key)) continue;
				object raw = fields[o.Key].Value;
				object v = Convert(o, raw);
				if(v != null) l.values[o.Id] = v;
			}

			// Sun off is an intensity of 0; the intensity row then shows the default to switch back on with
			double intensity = (double)l.values[SUN_INTENSITY];
			l.values[SUN] = (intensity > 0);
			if(intensity <= 0) l.values[SUN_INTENSITY] = GetDefault(SUN_INTENSITY);

			l.values["sunangle"] = ((angle % 360) + 360) % 360;
			l.values["sunelevation"] = PitchToElevation(pitch);
			return l;
		}

		public LightmapLighting Clone()
		{
			LightmapLighting l = new LightmapLighting { HasThing = HasThing };
			foreach(KeyValuePair<string, object> p in values) l.values[p.Key] = p.Value;
			return l;
		}

		#endregion

		#region ================== Methods

		// The ZDRay baker reads the last ZDRayInfo thing
		public static T FindInfoThing<T>(IEnumerable<T> things, Func<T, int> type, out int count) where T : class
		{
			T last = null;
			count = 0;
			foreach(T t in things)
			{
				if(type(t) != INFO_THING_TYPE) continue;
				last = t;
				count++;
			}
			return last;
		}

		// Pitch is stored 0..359 and positive raises the sun (ZDRay's sun vector z is sin(pitch))
		public static int PitchToElevation(int pitch)
		{
			int p = ((pitch % 360) + 360) % 360;
			return (p > 180 ? p - 360 : p);
		}

		public IEnumerable<string> ChangedFrom(LightmapLighting original)
		{
			return values.Keys.Where(id => !Equals(values[id], original.values[id]));
		}

		public bool DiffersFrom(LightmapLighting original)
		{
			return ChangedFrom(original).Any();
		}

		// Writes the keys whose values changed since original; a value equal to the key's default removes it
		public void WriteFields(UniFields fields, LightmapLighting original)
		{
			HashSet<string> changed = new HashSet<string>(ChangedFrom(original), StringComparer.Ordinal);
			if(changed.Contains(SUN) || changed.Contains(SUN_INTENSITY))
			{
				double intensity = ((bool)values[SUN] ? (double)values[SUN_INTENSITY] : 0.0);
				UniFields.SetFloat(fields, SUN_KEY, intensity, (double)GetDefault(SUN_INTENSITY));
			}

			foreach(LightmapBakeOption o in Options)
			{
				if(o.Kind != LightmapOptionKind.MapKey || o.Id == SUN || o.Id == SUN_INTENSITY || !changed.Contains(o.Id)) continue;
				object v = values[o.Id];
				switch(o.Type)
				{
					case LightmapOptionType.Int: UniFields.SetInteger(fields, o.Key, (int)v, (int)o.Default); break;
					case LightmapOptionType.Float: UniFields.SetFloat(fields, o.Key, (double)v, (double)o.Default); break;
					case LightmapOptionType.Colour: UniFields.SetString(fields, o.Key, (string)v, (string)o.Default); break;
					case LightmapOptionType.Bool:
						if((bool)v == (bool)o.Default) fields.Remove(o.Key);
						else fields[o.Key] = new UniValue(UniversalType.Boolean, v);
						break;
				}
			}
		}

		// Lowercase six-digit hex, or null when the text isn't a colour
		public static string NormalizeColour(string text)
		{
			if(text == null) return null;
			string s = text.Trim().Trim('"');
			if(s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
			if(s.Length != 6) return null;
			int dummy;
			if(!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out dummy)) return null;
			return s.ToLowerInvariant();
		}

		private static object GetDefault(string id)
		{
			return LightmapBakeOptions.Get(id).Default;
		}

		// A key's value as the option's type, null when it can't be read as one
		private static object Convert(LightmapBakeOption o, object raw)
		{
			try
			{
				switch(o.Type)
				{
					case LightmapOptionType.Int: return System.Convert.ToInt32(raw, CultureInfo.InvariantCulture);
					case LightmapOptionType.Float: return System.Convert.ToDouble(raw, CultureInfo.InvariantCulture);
					case LightmapOptionType.Bool: return System.Convert.ToBoolean(raw, CultureInfo.InvariantCulture);
					case LightmapOptionType.Colour: return NormalizeColour(raw as string);
					default: return null;
				}
			}
			catch(FormatException) { return null; }
			catch(InvalidCastException) { return null; }
			catch(OverflowException) { return null; }
		}

		#endregion
	}
}
