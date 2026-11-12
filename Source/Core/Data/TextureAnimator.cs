using System;
using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.ZDoom;

namespace CodeImp.DoomBuilder.Data
{
	internal struct WarpInfo
	{
		internal int Style;
		internal double Speed;
	}

	internal sealed class AnimDef
	{
		internal readonly int[] Frames;
		internal readonly int[] MinMs;
		internal readonly int[] RangeMs;
		internal readonly bool Discrete;
		internal AnimationType Type;
		internal int CurrentFrame;
		internal long SwitchTime;
		internal bool Armed;

		internal AnimDef(int[] frames, int[] minimums, int[] maximums, bool discrete, AnimationType type)
		{
			if(frames.Length < 2 || minimums.Length != (discrete ? frames.Length : 1) || maximums.Length != minimums.Length)
				throw new ArgumentException("Invalid animation frame or duration count");
			Frames = (int[])frames.Clone();
			MinMs = (int[])minimums.Clone();
			RangeMs = new int[minimums.Length];
			for(int i = 0; i < minimums.Length; i++) RangeMs[i] = (int)Math.Min(int.MaxValue, Math.Max(0L, (long)maximums[i] - Math.Max(1, minimums[i])));
			Discrete = discrete;
			Type = type;
		}
	}

	public sealed class TextureAnimator
	{
		private readonly Dictionary<int, AnimDef> animations = new Dictionary<int, AnimDef>();
		private readonly List<int> bases = new List<int>();
		private readonly Dictionary<int, int> translations = new Dictionary<int, int>();
		private readonly Random random;
		private long? lastnow;
		internal int Count { get { return animations.Count; } }
		internal IEnumerable<int> FrameKeys { get { return bases.Concat(animations.Values.SelectMany(a => a.Frames)).Distinct(); } }

		internal TextureAnimator(Random random = null) { this.random = random ?? new Random(0); }

		internal void Add(int basekey, AnimDef definition, Action<string> warning = null)
		{
			for(int i = 0; i < definition.MinMs.Length; i++)
			{
				if(definition.MinMs[i] >= 1) continue;
				definition.MinMs[i] = 1;
				if(warning != null) warning("Animation duration clamped to 1 ms");
			}
			definition.Armed = false;
			definition.SwitchTime = 0;
			if(!animations.ContainsKey(basekey)) bases.Add(basekey);
			animations[basekey] = definition;
			Translate();
		}

		public void Rearm()
		{
			foreach(AnimDef definition in animations.Values) { definition.Armed = false; definition.SwitchTime = 0; }
			lastnow = null;
		}

		internal void Update(long nowMs)
		{
			if(lastnow.HasValue && nowMs < lastnow.Value) Rearm();
			lastnow = nowMs;
			foreach(int basekey in bases)
			{
				AnimDef anim = animations[basekey];
				if(!anim.Armed || anim.SwitchTime <= nowMs)
				{
					if(anim.Armed) Advance(anim);
					int frame = anim.Discrete ? anim.CurrentFrame : 0;
					long duration = (long)anim.MinMs[frame] + (anim.RangeMs[frame] > 0 ? random.Next(anim.RangeMs[frame]) : 0);
					anim.SwitchTime = nowMs > long.MaxValue - duration ? long.MaxValue : nowMs + duration;
					anim.Armed = true;
				}
			}
			Translate();
		}

		private void Advance(AnimDef anim)
		{
			switch(anim.Type)
			{
				case AnimationType.Backward: anim.CurrentFrame = (anim.CurrentFrame + anim.Frames.Length - 1) % anim.Frames.Length; break;
				case AnimationType.Random:
					int next = random.Next(anim.Frames.Length - 1);
					if(next == anim.CurrentFrame) next++;
					anim.CurrentFrame = next; break;
				case AnimationType.OscillateUp:
					anim.CurrentFrame++;
					if(anim.CurrentFrame == anim.Frames.Length - 1) anim.Type = AnimationType.OscillateDown;
					break;
				case AnimationType.OscillateDown:
					// The engine frame counter is unsigned 16-bit, including a reversed range's initial step.
					anim.CurrentFrame = (anim.CurrentFrame - 1) & 65535;
					if(anim.CurrentFrame == 0) anim.Type = AnimationType.OscillateUp;
					break;
				default: anim.CurrentFrame = (anim.CurrentFrame + 1) % anim.Frames.Length; break;
			}
		}

		private void Translate()
		{
			translations.Clear();
			foreach(int basekey in bases)
			{
				AnimDef anim = animations[basekey];
				if(anim.Discrete) translations[basekey] = anim.Frames[anim.CurrentFrame];
				else for(int i = 0; i < anim.Frames.Length; i++) translations[anim.Frames[i]] = anim.Frames[(i + anim.CurrentFrame) % anim.Frames.Length];
			}
		}

		internal int Resolve(int key) { int frame; return translations.TryGetValue(key, out frame) ? frame : key; }
	}

	internal sealed class TextureAnimationOrder
	{
		private sealed class Entry
		{
			internal int Key;
			internal int Type;
			internal object Source;
		}
		private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
		private readonly Dictionary<object, List<Entry>> sources = new Dictionary<object, List<Entry>>();

		internal void Register(int key, object source, int usetype)
		{
			if(entries.ContainsKey(key)) return;
			var entry = new Entry { Key = key, Type = usetype, Source = source };
			entries.Add(key, entry);
			if(!sources.ContainsKey(source)) sources.Add(source, new List<Entry>());
			sources[source].Add(entry);
		}

		internal void Seal()
		{
			foreach(object source in sources.Keys.ToArray()) sources[source] = sources[source].OrderBy(e => e.Type).ToList();
		}

		internal int? Frame(int basekey, int number)
		{
			Entry entry;
			if(!entries.TryGetValue(basekey, out entry)) return null;
			List<Entry> list = sources[entry.Source];
			long index = (long)list.IndexOf(entry) + number - 1;
			return index >= 0 && index < list.Count ? (int?)list[(int)index].Key : null;
		}

		internal int[] Range(int first, int last, out bool backward)
		{
			backward = false;
			Entry start, end;
			if(!entries.TryGetValue(first, out start) || !entries.TryGetValue(last, out end) || start.Source != end.Source || start.Type != end.Type || first == last) return null;
			List<Entry> list = sources[start.Source];
			int a = list.IndexOf(start), b = list.IndexOf(end);
			backward = a > b;
			return list.Skip(Math.Min(a, b)).Take(Math.Abs(b - a) + 1).Select(e => e.Key).ToArray();
		}
	}
}
