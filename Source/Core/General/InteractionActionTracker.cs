#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Globalization;

#endregion

namespace CodeImp.DoomBuilder
{
	internal delegate void InteractionEmit(string ev, string[] fields);

	// Pairs action Begin/End calls into action-begin and action-end lines for the interaction log.
	// End can run inside its own Begin (ReleaseAllKeys from a dialog), without any Begin
	// ([EndAction]-only actions) or after several Begins (key repeat), so this keeps per-action state.
	internal sealed class InteractionActionTracker
	{
		// A Begin with no further Begin for this long is treated as abandoned by the next Begin
		internal const double STALE_MS = 1500.0;

		private sealed class Entry
		{
			internal int Seq;
			internal string Name;
			internal double BeginAt;
			internal double LastBeginAt;
			internal double BeginMs = -1;
			internal bool BeginRunning;
			internal bool Ended;
			internal bool HeldEnd;
			internal double EndAt;
			internal double EndMs;
			internal string Trigger;
		}

		private sealed class Trigger
		{
			internal string Kind;
			internal string Key;
		}

		private readonly Func<double> clock;
		private readonly InteractionEmit emit;
		private readonly Dictionary<object, Entry> open = new Dictionary<object, Entry>();
		private readonly Dictionary<object, Trigger> triggers = new Dictionary<object, Trigger>();
		private readonly Stack<KeyValuePair<object, Entry>> running = new Stack<KeyValuePair<object, Entry>>();
		private readonly Dictionary<object, Entry> ending = new Dictionary<object, Entry>();
		private string ambient;
		private int seq;

		internal InteractionActionTracker(Func<double> clock, InteractionEmit emit)
		{
			this.clock = clock;
			this.emit = emit;
		}

		internal int Depth { get { return running.Count; } }

		// End with nothing bound and nothing open is a key release with no effect: not worth a line
		internal bool IsOpen(object action)
		{
			return open.ContainsKey(action);
		}

		// The trigger for the next Begin of this action only
		internal void SetTrigger(object action, string kind, string key)
		{
			triggers[action] = new Trigger { Kind = kind, Key = key };
		}

		// The trigger for the next outermost Begin during a menu, toolbar or palette call; returns the previous one
		internal string SetAmbient(string kind)
		{
			string previous = ambient;
			ambient = kind;
			return previous;
		}

		internal void RestoreAmbient(string previous)
		{
			ambient = previous;
		}

		internal void BeginEnter(object action, string name)
		{
			double now = clock();
			Trigger trig;
			if(triggers.TryGetValue(action, out trig)) triggers.Remove(action);
			string kind = trig != null ? trig.Kind : (running.Count == 0 && ambient != null ? ambient : "code");

			Entry current;
			bool repeat = false;
			if(open.TryGetValue(action, out current))
			{
				if(current.BeginRunning || now - current.LastBeginAt < STALE_MS)
				{
					repeat = true;
					current.LastBeginAt = now;
				}
				else
				{
					open.Remove(action);
					EmitLost(current);
				}
			}

			Entry e = new Entry { Seq = ++seq, Name = name, BeginAt = now, LastBeginAt = now, BeginRunning = true };
			if(!repeat) open[action] = e;
			running.Push(new KeyValuePair<object, Entry>(action, e));

			List<string> f = new List<string> { "name", name, "seq", Num(e.Seq), "trig", kind };
			if(trig != null && !string.IsNullOrEmpty(trig.Key)) { f.Add("key"); f.Add(trig.Key); }
			f.Add("depth"); f.Add(Num(running.Count - 1));
			if(repeat) { f.Add("rep"); f.Add("1"); }
			emit("action-begin", f.ToArray());
		}

		internal void BeginExit(object action)
		{
			if(running.Count == 0 || running.Peek().Key != action) return;
			Entry e = running.Pop().Value;
			e.BeginRunning = false;
			e.BeginMs = clock() - e.BeginAt;

			Entry primary;
			bool isprimary = open.TryGetValue(action, out primary) && primary == e;
			if(e.HeldEnd)
			{
				EmitEnd(e, true);
			}
			else if(!isprimary && !e.Ended)
			{
				// A key-repeat Begin: its own end line carries the Begin cost only
				emit("action-end", new[] { "name", e.Name, "seq", Num(e.Seq), "begin_ms", Ms(e.BeginMs),
					"end_ms", "-", "hold_ms", "-", "rep", "1" });
			}
		}

		internal void EndEnter(object action, string name)
		{
			double now = clock();
			Entry e;
			if(open.TryGetValue(action, out e)) open.Remove(action);
			else
			{
				Trigger trig;
				if(triggers.TryGetValue(action, out trig)) triggers.Remove(action);
				e = new Entry { Seq = 0, Name = name, Trigger = trig != null ? trig.Kind : (ambient ?? "code") };
			}
			e.Ended = true;
			e.EndAt = now;
			ending[action] = e;
		}

		internal void EndExit(object action)
		{
			Entry e;
			if(!ending.TryGetValue(action, out e)) return;
			ending.Remove(action);
			e.EndMs = clock() - e.EndAt;
			if(e.BeginRunning) e.HeldEnd = true; // written by BeginExit, once begin_ms is known
			else EmitEnd(e, false);
		}

		private void EmitEnd(Entry e, bool nested)
		{
			List<string> f = new List<string> { "name", e.Name, "seq", e.Seq > 0 ? Num(e.Seq) : "-",
				"begin_ms", e.BeginMs >= 0 ? Ms(e.BeginMs) : "-", "end_ms", Ms(e.EndMs),
				"hold_ms", e.Seq > 0 ? Ms(e.EndAt - e.BeginAt) : "-" };
			if(nested) { f.Add("nested"); f.Add("1"); }
			if(e.Seq == 0) { f.Add("trig"); f.Add(e.Trigger); }
			emit("action-end", f.ToArray());
		}

		private void EmitLost(Entry e)
		{
			emit("action-end", new[] { "name", e.Name, "seq", Num(e.Seq), "begin_ms", e.BeginMs >= 0 ? Ms(e.BeginMs) : "-",
				"end_ms", "-", "hold_ms", "-", "lost", "1" });
		}

		internal static string Ms(double ms)
		{
			return ms.ToString("0.0", CultureInfo.InvariantCulture);
		}

		private static string Num(int n)
		{
			return n.ToString(CultureInfo.InvariantCulture);
		}
	}
}
