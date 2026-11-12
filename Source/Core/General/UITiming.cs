using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using CodeImp.DoomBuilder.Windows;

namespace CodeImp.DoomBuilder
{
	// Opt-in dialog instrumentation. Logging failures must never affect the editor.
	internal static class UITiming
	{
		internal static readonly bool Enabled = IsEnabled();
		private static readonly long sessionstart = Stopwatch.GetTimestamp();
		private static int counter;
		[ThreadStatic] private static RequestScope pending;

		internal sealed class Timing
		{
			internal string Id;
			internal long Start;
			internal long Previous;
			internal bool Complete;
		}

		private sealed class RequestScope : IDisposable
		{
			internal string Form;
			internal Timing Timing;
			internal RequestScope Previous;
			internal bool Consumed;

			public void Dispose()
			{
				if(pending == this) pending = Previous;
			}
		}

		private static bool IsEnabled()
		{
			try { return Environment.GetEnvironmentVariable("UDB_UI_TIMING") == "1"; }
			catch(Exception) { return false; }
		}

		// Scope only the constructor, so a failed or nested open cannot leave a stale request.
		internal static IDisposable Request(string form)
		{
			if(!Enabled) return null;
			try
			{
				long now = Stopwatch.GetTimestamp();
				Timing timing = new Timing
				{
					Id = form + "#" + Interlocked.Increment(ref counter).ToString(CultureInfo.InvariantCulture),
					Previous = now
				};
				RequestScope scope = new RequestScope { Form = form, Timing = timing, Previous = pending };
				pending = scope;
				Write(timing.Id, "request", 0, 0, now);
				return scope;
			}
			catch(Exception) { return null; }
		}

		internal static void Begin(DelayedForm form)
		{
			if(!Enabled) return;
			try
			{
				RequestScope scope = pending;
				if(scope == null || scope.Consumed || scope.Form != form.GetType().Name) return;
				scope.Consumed = true;
				Timing timing = scope.Timing;
				long now = Stopwatch.GetTimestamp();
				timing.Start = now;
				form.UITimingState = timing;
				Write(timing.Id, "ctor", 0, Milliseconds(now - timing.Previous), now);
				timing.Previous = now;
			}
			catch(Exception) { }
		}

		internal static void Mark(DelayedForm form, string phase)
		{
			if(!Enabled) return;
			try
			{
				Timing timing = form.UITimingState;
				if(timing == null || timing.Complete) return;
				long now = Stopwatch.GetTimestamp();
				Write(timing.Id, phase, Milliseconds(now - timing.Start), Milliseconds(now - timing.Previous), now);
				timing.Previous = now;
				if(phase == "usable" || phase == "closed-before-usable") timing.Complete = true;
			}
			catch(Exception) { }
		}

		private static double Milliseconds(long ticks)
		{
			return ticks * 1000.0 / Stopwatch.Frequency;
		}

		private static void Write(string id, string phase, double ms, double dms, long now)
		{
			try { General.WriteLogLine(FormatLine(id, phase, ms, dms, Milliseconds(now - sessionstart))); }
			catch(Exception) { }
		}

		// ms starts at the base ctor; sessionms also relates request events to later phases.
		internal static string FormatLine(string id, string phase, double ms, double dms, double sessionms)
		{
			return string.Format(CultureInfo.InvariantCulture, "UITIMING {0} {1} {2:F3} {3:F3} sessionms={4:F3}",
				id, phase, ms, dms, sessionms);
		}
	}
}
