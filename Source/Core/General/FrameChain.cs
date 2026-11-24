#region ================== Namespaces

using System;
using System.Reflection;

#endregion

namespace CodeImp.DoomBuilder
{
	// What one pump pass's idle call did with the chain
	internal enum ChainIdle
	{
		Posted,
		Pending,
		Unwanted,
		Paints,
	}

	// Runs processor ticks from the message pump's idle pass instead of the WinForms timer.
	// Mono's timer fires at most every 15 ms and makes a tick that ends early wait a full 15 ms more,
	// so Visual mode on Mono uses this to run one tick per pump pass, paced by vsync and a minimum frame time.
	internal sealed class FrameChain
	{
		internal const int DEFAULT_MIN_FRAME_MS = 4;
		internal const int MAX_MIN_FRAME_MS = 100;

		private readonly Func<bool> wanted;
		private readonly Func<int> pendingpaints;
		private readonly Func<Action, bool> post;
		private readonly Func<long> clock;
		private readonly Action<int> sleep;
		private readonly Action tick;

		private bool pending;
		private int frames;
		private int timerseen;
		private long lastframe = -1;

		internal int MinFrameMs { get; set; }
		internal int Frames { get { return frames; } }
		internal bool Pending { get { return pending; } }

		// pendingpaints returns the paints waiting in the pump, or -1 when unknown (which keeps the chain off); post returns false when it couldn't post
		internal FrameChain(Func<bool> wanted, Func<int> pendingpaints, Func<Action, bool> post, Func<long> clock, Action<int> sleep, Action tick)
		{
			this.wanted = wanted;
			this.pendingpaints = pendingpaints;
			this.post = post;
			this.clock = clock;
			this.sleep = sleep;
			this.tick = tick;
			MinFrameMs = DEFAULT_MIN_FRAME_MS;
		}

		// Called once per pump pass. Mono dispatches a posted frame before it reads input or runs timers again,
		// and skips both while paints wait, so only one frame is posted per pass and none while paints wait.
		internal ChainIdle OnIdle()
		{
			if(pending) return ChainIdle.Pending;
			if(!wanted()) return ChainIdle.Unwanted;
			if(pendingpaints() != 0) return ChainIdle.Paints;
			if(lastframe >= 0)
			{
				long elapsed = clock() - lastframe;
				if(elapsed >= 0 && elapsed < MinFrameMs) sleep((int)(MinFrameMs - elapsed));
			}
			pending = true;
			if(post(RunFrame)) return ChainIdle.Posted;
			pending = false;
			return ChainIdle.Unwanted;
		}

		private void RunFrame()
		{
			pending = false;
			if(!wanted()) return;
			frames++;
			lastframe = clock();
			tick();
		}

		// The timer is the fallback: it ticks only when no chained frame ran since its own previous tick
		internal bool TimerShouldTick()
		{
			if(frames == timerseen) return true;
			timerseen = frames;
			return false;
		}

		// Mono's count of windows waiting to paint on this thread, through its internal X11 queue; null on any other backend
		internal static Func<int> MonoPaintCounter(IntPtr handle)
		{
			try
			{
				Assembly swf = typeof(System.Windows.Forms.Control).Assembly;
				Type hwndtype = swf.GetType("System.Windows.Forms.Hwnd");
				if(hwndtype == null) return null;
				MethodInfo fromhandle = hwndtype.GetMethod("ObjectFromHandle", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(IntPtr) }, null);
				object hwnd = fromhandle != null ? fromhandle.Invoke(null, new object[] { handle }) : null;
				if(hwnd == null) return null;
				object queue = GetProperty(hwnd, "Queue");
				if(queue == null || queue.GetType().FullName != "System.Windows.Forms.XEventQueue") return null;
				object paint = GetProperty(queue, "Paint");
				if(paint == null) return null;
				PropertyInfo count = paint.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
				if(count == null || count.PropertyType != typeof(int)) return null;
				return (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), paint, count.GetGetMethod(true));
			}
			catch(Exception)
			{
				return null;
			}
		}

		private static object GetProperty(object target, string name)
		{
			PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			return p != null ? p.GetValue(target, null) : null;
		}
	}
}
