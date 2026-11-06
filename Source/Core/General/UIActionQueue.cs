
#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

#endregion

namespace CodeImp.DoomBuilder
{
	// The worker-to-UI queue behind MainForm.RunOnUIThread, apart from the Form so it runs without a display
	internal class UIActionQueue
	{
		#region ================== Variables

		private readonly object syncobject = new object();
		private List<Action> actions = new List<Action>();

		// A failed notify leaves actions queued with none on its way, so the next Add must send one
		private volatile bool notifyfailed;

		#endregion

		#region ================== Methods

		public void Add(Action action, Action notify)
		{
			bool send;
			lock(syncobject)
			{
				send = (actions.Count == 0) || notifyfailed;
				notifyfailed = false;
				actions.Add(action);
			}

			if(send)
			{
				try
				{
					// Must not wait for the UI thread: callers hold locks the UI thread takes
					notify();
				}
				catch(Exception)
				{
					// No lock here: an interrupted worker lands here too, and a contended lock would throw again
					notifyfailed = true;
					throw;
				}
			}
		}

		public void Process(Action<Exception> log)
		{
			List<Action> batch;
			lock(syncobject)
			{
				batch = actions;
				actions = new List<Action>();
			}

			Exception firstfailure = null;
			foreach(Action action in batch)
			{
				// One failure must not cost the rest of the batch
				try
				{
					action();
				}
				catch(Exception e)
				{
					if(firstfailure == null) firstfailure = e;
					try { log(e); }
					catch(Exception) { }
				}
			}

			// On Windows this reaches the ExceptionDialog. Mono's BeginInvoke discards it, so there the log is the only record
			if(firstfailure != null) ExceptionDispatchInfo.Capture(firstfailure).Throw();
		}

		#endregion
	}
}
