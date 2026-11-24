using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CodeImp.DoomBuilder.ZDoom;

namespace CodeImp.DoomBuilder.Data
{
	// How the table reaches images; DataManager supplies its own, tests supply plain objects
	internal sealed class EmissiveResolver
	{
		public Func<string, EmissiveSurface, object> Resolve; // null when the name is unknown in that namespace
		public Func<object, IEnumerable<object>> Frames;     // the image's ANIMATED/ANIMDEFS sequence, or null
		public Func<object, int?> AutoColor;                 // 0xRRGGBB for "color auto", null when it has no colour
		public Func<object, string> Describe;                // the image's name in the digest
	}

	// One image and surface kind that emits
	internal sealed class EmissiveItem
	{
		public object Image;
		public EmissiveSurface Kind;
		public int Color;
		public double Intensity;
		public double Distance;
		public EmitdefsEntry Entry; // the EMITDEFS line it came from
		public bool FromAnimation;  // a frame of the entry's image's sequence
	}

	// The merged EMITDEFS of every loaded resource, resolved to images, with "auto" computed and
	// animation sequences expanded. Built once per resource load.
	internal sealed class EmissiveTable
	{
		public const int ZDRAY_NAME_LENGTH = 63; // ZDRay keeps texture names in 64-byte buffers

		public static readonly EmissiveTable Empty = new EmissiveTable(new List<EmissiveItem>(), null);

		private readonly Dictionary<object, EmissiveItem>[] bykind = { new Dictionary<object, EmissiveItem>(), new Dictionary<object, EmissiveItem>() };
		private readonly List<EmissiveItem> items;
		private readonly EmissiveResolver resolver;

		public IReadOnlyList<EmissiveItem> Items { get { return items; } }
		public int EntryCount { get; private set; } // EMITDEFS entries after the merge, frames not counted
		public string Digest { get; private set; }  // null when the table is empty

		private EmissiveTable(List<EmissiveItem> items, EmissiveResolver resolver)
		{
			this.items = items;
			this.resolver = resolver;
			foreach(EmissiveItem item in items)
			{
				bykind[(int)item.Kind][item.Image] = item;
				if(!item.FromAnimation) EntryCount++;
			}
			Digest = ComputeDigest();
		}

		// entries: every container's EMITDEFS in load order. A later entry wins per (image, kind).
		public static EmissiveTable Build(IEnumerable<EmitdefsEntry> entries, EmissiveResolver resolver, Action<string> warning)
		{
			Action<string> warn = message => { if(warning != null) warning(message); };

			// Explicit entries, removals included, so a frame's own entry (even a removal) beats expansion
			var explicitorder = new List<KeyValuePair<object, EmitdefsEntry>>[] { new List<KeyValuePair<object, EmitdefsEntry>>(), new List<KeyValuePair<object, EmitdefsEntry>>() };
			var explicitbyimage = new[] { new Dictionary<object, int>(), new Dictionary<object, int>() };
			foreach(EmitdefsEntry e in entries)
			{
				object image = resolver.Resolve(e.Name, e.Kind);
				if(image == null)
				{
					warn(Where(e) + ": unknown " + (e.Kind == EmissiveSurface.Walls ? "texture" : "flat") + " \"" + e.Name + "\"");
					continue;
				}
				int k = (int)e.Kind;
				int index;
				var pair = new KeyValuePair<object, EmitdefsEntry>(image, e);
				if(explicitbyimage[k].TryGetValue(image, out index)) explicitorder[k][index] = pair;
				else
				{
					explicitbyimage[k][image] = explicitorder[k].Count;
					explicitorder[k].Add(pair);
				}
			}

			var result = new List<EmissiveItem>();
			var taken = new[] { new HashSet<object>(), new HashSet<object>() };
			var autocolors = new Dictionary<object, int?>();
			foreach(EmissiveSurface kind in new[] { EmissiveSurface.Walls, EmissiveSurface.Flats })
			{
				int k = (int)kind;
				var named = new List<EmissiveItem>();
				foreach(KeyValuePair<object, EmitdefsEntry> pair in explicitorder[k])
				{
					EmitdefsEntry e = pair.Value;
					if(e.IsRemoval) continue;
					int color = e.Color;
					if(color == EmitdefsEntry.AUTO_COLOR)
					{
						int? auto;
						if(!autocolors.TryGetValue(pair.Key, out auto)) autocolors[pair.Key] = auto = resolver.AutoColor(pair.Key);
						if(!auto.HasValue)
						{
							warn(Where(e) + ": \"" + e.Name + "\" has no visible colour for color auto, entry skipped");
							continue;
						}
						color = auto.Value;
					}
					var item = new EmissiveItem { Image = pair.Key, Kind = kind, Color = color, Intensity = e.Intensity, Distance = e.Distance, Entry = e };
					named.Add(item);
					taken[k].Add(pair.Key);
				}
				result.AddRange(named);

				// Every frame of a named image's sequence emits alike, unless the frame has its own entry
				foreach(EmissiveItem item in named)
				{
					IEnumerable<object> frames = resolver.Frames != null ? resolver.Frames(item.Image) : null;
					if(frames == null) continue;
					foreach(object frame in frames)
					{
						if(frame == null || explicitbyimage[k].ContainsKey(frame) || taken[k].Contains(frame)) continue;
						taken[k].Add(frame);
						result.Add(new EmissiveItem { Image = frame, Kind = kind, Color = item.Color, Intensity = item.Intensity, Distance = item.Distance, Entry = item.Entry, FromAnimation = true });
					}
				}
			}

			return result.Count == 0 ? Empty : new EmissiveTable(result, resolver);
		}

		public EmissiveItem Find(object image, EmissiveSurface kind)
		{
			EmissiveItem item;
			return image != null && bykind[(int)kind].TryGetValue(image, out item) ? item : null;
		}

		// The baker's --emissive file, keyed by the map's own texture strings, or null when no string
		// emits. usedentries counts the EMITDEFS entries the map uses.
		public string BuildBakeFile(IEnumerable<KeyValuePair<string, EmissiveSurface>> mapnames, Action<string> warning, out int usedentries)
		{
			usedentries = 0;
			if(items.Count == 0) return null;

			var lines = new SortedDictionary<string, string>(StringComparer.Ordinal); // key: kind + lowercased name
			var used = new HashSet<EmitdefsEntry>();
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach(KeyValuePair<string, EmissiveSurface> pair in mapnames)
			{
				string name = pair.Key;
				if(string.IsNullOrEmpty(name) || name == "-" || name.IndexOf('"') >= 0) continue;
				if(!seen.Add((int)pair.Value + name)) continue;
				EmissiveItem item = Find(resolver.Resolve(name, pair.Value), pair.Value);
				if(item == null) continue;

				string cut = name.Length > ZDRAY_NAME_LENGTH ? name.Substring(0, ZDRAY_NAME_LENGTH) : name;
				string key = (int)pair.Value + cut.ToLowerInvariant();
				string line = FileLine(cut, item);
				string existing;
				if(lines.TryGetValue(key, out existing))
				{
					// Two map strings ZDRay can't tell apart: the first one kept wins
					if(ValuePart(existing) != ValuePart(line) && warning != null)
						warning("Emissive textures: \"" + name + "\" matches \"" + cut + "\" in the baker, which already has a different entry; skipped");
					continue;
				}
				lines[key] = line;
				used.Add(item.Entry);
			}

			usedentries = used.Count;
			if(lines.Count == 0) return null;
			return "// Emissive textures for this bake, from EMITDEFS\n" + string.Join("\n", lines.Values) + "\n";
		}

		private static string FileLine(string name, EmissiveItem item)
		{
			return "\"" + name + "\" " + Values(item) + " on " + (item.Kind == EmissiveSurface.Walls ? "walls" : "flats");
		}

		private static string Values(EmissiveItem item)
		{
			return "color " + item.Color.ToString("x6", CultureInfo.InvariantCulture) +
				" intensity " + item.Intensity.ToString("R", CultureInfo.InvariantCulture) +
				" distance " + item.Distance.ToString("R", CultureInfo.InvariantCulture);
		}

		private static string ValuePart(string line) { return line.Substring(line.LastIndexOf('"') + 1); }

		private static string Where(EmitdefsEntry e) { return e.Source + ", line " + e.Line; }

		private string ComputeDigest()
		{
			if(items.Count == 0) return null;
			List<string> lines = items.Select(i => (i.Kind == EmissiveSurface.Walls ? "walls " : "flats ") + resolver.Describe(i.Image) + "\t" + Values(i)).ToList();
			lines.Sort(StringComparer.Ordinal);
			using(SHA256 sha = SHA256.Create())
				return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).Replace("-", "");
		}

		// Brightest channel scaled to 255, keeping the hue; null for black
		public static int? ScaleToFull(int r, int g, int b)
		{
			int max = Math.Max(r, Math.Max(g, b));
			if(max <= 0) return null;
			r = Math.Min(255, (int)Math.Round(r * 255.0 / max));
			g = Math.Min(255, (int)Math.Round(g * 255.0 / max));
			b = Math.Min(255, (int)Math.Round(b * 255.0 / max));
			return (r << 16) | (g << 8) | b;
		}
	}
}
