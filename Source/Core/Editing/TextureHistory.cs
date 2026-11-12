using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CodeImp.DoomBuilder.Editing
{
	public enum TextureHistoryKind { Texture, Flat }

	public sealed class TextureHistory
	{
		private readonly List<string> textures = new List<string>();
		private readonly List<string> flats = new List<string>();
		private readonly ReadOnlyCollection<string> textureview;
		private readonly ReadOnlyCollection<string> flatview;
		private int capacity;

		public event EventHandler Changed;
		public bool Mixed { get; private set; }
		public int Capacity
		{
			get { return capacity; }
			set
			{
				int next = Math.Max(0, Math.Min(64, value));
				if(next == capacity) return;
				capacity = next;
				Prune(textures);
				Prune(flats);
				OnChanged();
			}
		}

		public TextureHistory(int capacity, bool mixed)
		{
			Mixed = mixed;
			textureview = textures.AsReadOnly();
			flatview = flats.AsReadOnly();
			this.capacity = Math.Max(0, Math.Min(64, capacity));
		}

		public IReadOnlyList<string> Get(TextureHistoryKind kind)
		{
			return Mixed || kind == TextureHistoryKind.Texture ? textureview : flatview;
		}

		public bool Record(string name, TextureHistoryKind kind) { return Add(name, kind, true); }
		public bool Touch(string name, TextureHistoryKind kind) { return Add(name, kind, false); }

		private bool Add(string name, TextureHistoryKind kind, bool reorder)
		{
			if(capacity == 0 || string.IsNullOrWhiteSpace(name) || name == "-") return false;
			List<string> entries = Mixed || kind == TextureHistoryKind.Texture ? textures : flats;
			int index = entries.FindIndex(s => s.ToUpperInvariant() == name.ToUpperInvariant());
			if(index >= 0)
			{
				if(!reorder || (index == 0 && entries[0] == name)) return false;
				entries.RemoveAt(index);
			}
			entries.Insert(0, name);
			Prune(entries);
			OnChanged();
			return true;
		}

		public void Remove(string name, TextureHistoryKind kind)
		{
			if(name == null) return;
			List<string> entries = Mixed || kind == TextureHistoryKind.Texture ? textures : flats;
			int index = entries.FindIndex(s => s.ToUpperInvariant() == name.ToUpperInvariant());
			if(index < 0) return;
			entries.RemoveAt(index);
			OnChanged();
		}

		public void Clear()
		{
			if(textures.Count == 0 && flats.Count == 0) return;
			textures.Clear();
			flats.Clear();
			OnChanged();
		}

		private void Prune(List<string> entries)
		{
			if(entries.Count > capacity) entries.RemoveRange(capacity, entries.Count - capacity);
		}

		private void OnChanged()
		{
			if(Changed != null) Changed(this, EventArgs.Empty);
		}
	}
}
