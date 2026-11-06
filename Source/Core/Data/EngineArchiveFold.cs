#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Data
{
	internal enum EngineNameKind { Class, Light }

	// Class and light names the engine archives define (Includes/Engine_names.cfg)
	internal sealed class EngineNames
	{
		private readonly HashSet<string> classes;
		private readonly HashSet<string> lights;

		internal EngineNames(IEnumerable<string> classes, IEnumerable<string> lights)
		{
			this.classes = new HashSet<string>(classes, StringComparer.OrdinalIgnoreCase);
			this.lights = new HashSet<string>(lights, StringComparer.OrdinalIgnoreCase);
		}

		internal bool Contains(EngineNameKind kind, string name)
		{
			if(string.IsNullOrEmpty(name)) return false;
			return (kind == EngineNameKind.Class ? classes : lights).Contains(name);
		}

		// Returns null and sets error when the file can't be read
		internal static EngineNames Load(string path, out string error)
		{
			Configuration cfg = new Configuration(path, true);
			if(cfg.ErrorResult)
			{
				error = "Unable to read \"" + path + "\": " + cfg.ErrorDescription + " (line " + cfg.ErrorLine + ")";
				return null;
			}

			error = null;
			return new EngineNames(Keys(cfg, "engineclasses"), Keys(cfg, "enginelights"));
		}

		private static IEnumerable<string> Keys(Configuration cfg, string setting)
		{
			foreach(DictionaryEntry e in cfg.ReadSetting(setting, new Hashtable()))
				yield return e.Key.ToString();
		}
	}

	// While a config's engine archive is missing, folds the messages about its classes and lights into one count line
	internal sealed class EngineArchiveFold
	{
		private const int TOP_NAMES = 5;

		private readonly Action<ErrorItem> output;
		private readonly List<KeyValuePair<string, ErrorItem>> buffer = new List<KeyValuePair<string, ErrorItem>>();
		private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		private List<RequiredArchive> missing = new List<RequiredArchive>();
		private string configtitle;
		private EngineNames names;
		private bool decided;
		private int total;

		internal EngineArchiveFold(Action<ErrorItem> output)
		{
			this.output = output;
		}

		// The archives with an unmet class or lump entry; need_exclude arrives as an entry with neither, which never counts as unmet
		internal static List<RequiredArchive> FindMissing(IEnumerable<RequiredArchive> archives, Func<string, bool> hasclass, Func<string, bool> haslump)
		{
			List<RequiredArchive> missing = new List<RequiredArchive>();
			foreach(RequiredArchive arc in archives)
			{
				foreach(RequiredArchiveEntry e in arc.Entries)
				{
					if((e.Class != null && !hasclass(e.Class)) || (e.Lump != null && !haslump(e.Lump)))
					{
						missing.Add(arc);
						break;
					}
				}
			}
			return missing;
		}

		// The parsers' UnresolvedClassSink: holds class warnings until Decide, then routes them
		internal bool OnUnresolvedClass(string name, ErrorItem item)
		{
			if(decided) return Route(EngineNameKind.Class, name, item);
			Buffer(name, item);
			return true;
		}

		internal void Buffer(string name, ErrorItem item)
		{
			buffer.Add(new KeyValuePair<string, ErrorItem>(name, item));
		}

		// loadnames only runs when something is missing
		internal void Decide(IReadOnlyList<RequiredArchive> missing, string configtitle, Func<EngineNames> loadnames)
		{
			decided = true;
			this.missing = new List<RequiredArchive>(missing ?? new List<RequiredArchive>());
			this.configtitle = configtitle;
			if(this.missing.Count > 0) names = loadnames();

			foreach(KeyValuePair<string, ErrorItem> e in buffer)
				if(!Route(EngineNameKind.Class, e.Key, e.Value)) output(e.Value);
			buffer.Clear();
		}

		// Returns true when the item was folded; the caller logs it otherwise
		internal bool Route(EngineNameKind kind, string name, ErrorItem item)
		{
			if(!decided || missing.Count == 0 || names == null || !names.Contains(kind, name)) return false;
			counts.TryGetValue(name, out int count);
			counts[name] = count + 1;
			total++;
			return true;
		}

		// ReloadGldefs counts only its own messages
		internal void ResetCount()
		{
			counts.Clear();
			total = 0;
		}

		internal void Emit()
		{
			if(missing.Count == 0) return;

			foreach(RequiredArchive a in missing)
				output(new ErrorItem(ErrorType.Warning, "Game configuration \"" + configtitle + "\" has no \"" + a.FileName
					+ "\" among its resources. Engine classes and lights can't resolve without it. Add it in Tools > Game Configurations > Resources"
					+ (a.ExcludeFromTesting ? ", with \"Exclude this resource from testing parameters\" checked." : ".")));

			if(total == 0) return;
			string files = string.Join(" and ", missing.Select(a => "\"" + a.FileName + "\""));
			string top = string.Join(", ", counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.OrdinalIgnoreCase)
				.Take(TOP_NAMES).Select(c => c.Key + " " + c.Value));
			output(new ErrorItem(ErrorType.Warning, "Folded " + total + " messages about engine classes and lights while " + files
				+ (missing.Count > 1 ? " are" : " is") + " missing (most frequent: " + top + (counts.Count > TOP_NAMES ? ", ..." : "") + ")."));
		}
	}
}
