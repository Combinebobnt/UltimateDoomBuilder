using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.IO;

namespace CodeImp.DoomBuilder.Data
{
	internal sealed class TextureFavorites
	{
		private readonly Configuration settings;
		private readonly Action<string> warning;
		private readonly List<string> textures = new List<string>();
		private readonly List<string> flats = new List<string>();
		private string settingskey;

		public event EventHandler Changed;

		public TextureFavorites(Configuration settings, string settingskey, Action<string> warning = null)
		{
			if(settings == null) throw new ArgumentNullException(nameof(settings));
			this.settings = settings;
			this.warning = warning;
			Rekey(settingskey);
		}

		public void Rekey(string key)
		{
			if(string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A configuration settings key is required.", nameof(key));
			settingskey = key;
			Read(TextureHistoryKind.Texture, textures);
			Read(TextureHistoryKind.Flat, flats);
			Changed?.Invoke(this, EventArgs.Empty);
		}

		public IReadOnlyList<string> Get(TextureHistoryKind kind)
		{
			return List(kind).AsReadOnly();
		}

		public bool Contains(TextureHistoryKind kind, string name)
		{
			return IndexOf(List(kind), name) >= 0;
		}

		public bool Add(TextureHistoryKind kind, string name)
		{
			if(!ValidName(name) || Contains(kind, name)) return false;
			var next = new List<string>(List(kind)) { name };
			return Store(kind, next);
		}

		public bool Remove(TextureHistoryKind kind, string name)
		{
			var next = new List<string>(List(kind));
			int index = IndexOf(next, name);
			if(index < 0) return false;
			next.RemoveAt(index);
			return Store(kind, next);
		}

		public bool Toggle(TextureHistoryKind kind, string name)
		{
			return Contains(kind, name) ? Remove(kind, name) : Add(kind, name);
		}

		public bool Contains(ImageData image) { return image != null && Contains(Kind(image), image.Name); }
		public bool Toggle(ImageData image) { return image != null && Toggle(Kind(image), image.Name); }
		public bool Add(ImageData image) { return image != null && Add(Kind(image), image.Name); }
		public bool Remove(ImageData image) { return image != null && Remove(Kind(image), image.Name); }

		private static TextureHistoryKind Kind(ImageData image)
		{
			return image.TextureNamespace == TextureNamespace.FLAT ? TextureHistoryKind.Flat : TextureHistoryKind.Texture;
		}

		private List<string> List(TextureHistoryKind kind)
		{
			switch(kind)
			{
				case TextureHistoryKind.Texture: return textures;
				case TextureHistoryKind.Flat: return flats;
				default: throw new ArgumentOutOfRangeException(nameof(kind));
			}
		}

		private string Key(TextureHistoryKind kind)
		{
			return "configurations." + settingskey + (kind == TextureHistoryKind.Flat ? ".favoriteflats" : ".favoritetextures");
		}

		private void Read(TextureHistoryKind kind, List<string> names)
		{
			names.Clear();
			string key = Key(kind);
			var entries = settings.ReadSettingObject(key, null) as IDictionary;
			if(entries == null)
			{
				warning?.Invoke("Texture favorites: " + key + " is missing or is not a list; using an empty list.");
				return;
			}
			foreach(DictionaryEntry entry in entries)
			{
				string name = entry.Value as string;
				if(!ValidName(name))
				{
					warning?.Invoke("Texture favorites: ignoring an invalid name in " + key + ".");
					continue;
				}
				if(IndexOf(names, name) < 0) names.Add(name);
			}
		}

		private bool Store(TextureHistoryKind kind, List<string> next)
		{
			var entries = new ListDictionary();
			for(int i = 0; i < next.Count; i++) entries.Add(i.ToString(CultureInfo.InvariantCulture), next[i]);
			if(!settings.WriteSetting(Key(kind), entries))
			{
				warning?.Invoke("Texture favorites: unable to write " + Key(kind) + ".");
				return false;
			}
			var names = List(kind);
			names.Clear();
			names.AddRange(next);
			Changed?.Invoke(this, EventArgs.Empty);
			return true;
		}

		private static bool ValidName(string name) { return !string.IsNullOrWhiteSpace(name) && name != "-"; }

		private static int IndexOf(List<string> names, string name)
		{
			return names.FindIndex(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
		}
	}
}
