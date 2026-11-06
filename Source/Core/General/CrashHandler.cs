#region ================== Namespaces

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder
{
	// Decides who handles a worker's crash: the UI delegate if it picks up in time, else the worker's timeout path
	internal sealed class CrashPickup
	{
		private const int PENDING = 0;
		private const int PICKEDUP = 1;
		private const int ABANDONED = 2;

		private readonly long started;
		private readonly long window;
		private readonly long cap;
		private long armed;
		private int state;

		internal bool IsPickedUp { get { return Volatile.Read(ref state) == PICKEDUP; } }

		internal CrashPickup(long nowms, long windowms, long capms)
		{
			started = nowms;
			armed = nowms;
			window = windowms;
			cap = capms;
		}

		// UI delegate side. False means the worker already gave up, so the delegate must not touch the map
		internal bool TryPickUp() { return Interlocked.CompareExchange(ref state, PICKEDUP, PENDING) == PENDING; }

		// Worker side. False means the UI picked up first, so the worker waits for it instead
		internal bool TryAbandon() { return Interlocked.CompareExchange(ref state, ABANDONED, PENDING) == PENDING; }

		// Each poll that sees a save in progress re-arms the window, so a long save is never cut short
		internal bool WindowExpired(long nowms, bool saving)
		{
			if(saving) armed = nowms;
			return nowms - started >= cap || nowms - armed >= window;
		}

		internal bool CapReached(long nowms) { return nowms - started >= cap; }
	}

	// Mono-safe handlers for exceptions nothing else caught. Release builds only
	internal static class CrashHandler
	{
		#region ================== Constants

		private const int PICKUP_WINDOW_MS = 10000;
		private const int TOTAL_CAP_MS = 300000;
		private const int BACKUP_JOIN_MS = 60000;
		private const int POLL_MS = 100;

		#endregion

		#region ================== Variables

		private static int uithreadid;
		private static int terminalclaimed;
		private static int backupclaimed;
		private static bool dialogopen; // UI thread only
		private static readonly Stopwatch clock = Stopwatch.StartNew();

		#endregion

		#region ================== Install

		internal static void Install()
		{
			uithreadid = Thread.CurrentThread.ManagedThreadId;
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
			Application.ThreadException += OnThreadException;
		}

		#endregion

		#region ================== Recoverable path

		// Always on the UI thread: window procedures and GuardedTimer ticks land here
		private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
		{
			Exception ex = e.Exception;
			try
			{
				Log("Unhandled UI thread exception: " + ex);
				if(dialogopen)
				{
					Log("A crash dialog is already open, so this one goes no further");
					return;
				}

				try { ExceptionDialog.WriteCrashReport(ex); }
				catch(Exception) { }
				// The logger doesn't exist early in startup
				try { General.ErrorLogger.Add(ErrorType.Error, ex.Source + ": " + ex.Message); }
				catch(Exception) { }

				if(General.MainWindow != null) General.MainWindow.BreakExclusiveMouseInput();
				DialogResult result;
				dialogopen = true;
				try
				{
					using(ExceptionDialog dlg = new ExceptionDialog(ex, false))
					{
						dlg.Setup();
						result = dlg.ShowDialog();
					}
				}
				finally
				{
					dialogopen = false;
				}

				if(result == DialogResult.Cancel)
				{
					// Quit or the window's close button
					RunBackupOnce();
					General.Terminate(false);
				}
				else if(General.MainWindow != null)
				{
					General.MainWindow.ResumeExclusiveMouseInput();
				}
			}
			catch(Exception failure)
			{
				Fallback(ex, failure);
			}
		}

		#endregion

		#region ================== Terminal path

		private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			try
			{
				Exception ex = e.ExceptionObject as Exception ?? new Exception("Non-exception object thrown: " + e.ExceptionObject);
				Thread current = Thread.CurrentThread;
				Log("Unhandled exception on thread \"" + current.Name + "\" (" + current.ManagedThreadId + "), terminating=" + e.IsTerminating + ": " + ex);

				// The first terminal exception owns the crash. Returning would kill the process under its dialog
				if(Interlocked.CompareExchange(ref terminalclaimed, 1, 0) != 0)
				{
					Log("Another crash is already being handled, parking thread " + current.ManagedThreadId);
					Park();
				}

				try { ExceptionDialog.WriteCrashReport(ex); }
				catch(Exception) { }

				if(current.ManagedThreadId == uithreadid) ShowTerminal(ex);
				else HandleWorkerCrash(ex);
			}
			catch(Exception failure)
			{
				try { Log("The crash handler failed: " + failure); }
				catch(Exception) { }
			}
			Kill();
		}

		// UI thread only. Freezes the editor, writes the backup, then shows the dialog
		private static void ShowTerminal(Exception ex)
		{
			try
			{
				dialogopen = true;
				if(General.AutoSaver != null) General.AutoSaver.StopTimer();
				MainForm main = General.MainWindow;
				if(main != null)
				{
					main.StopProcessing();
					main.BreakExclusiveMouseInput();
				}

				// Before the dialog, so a dialog failure can't lose it
				RunBackupOnce();

				using(ExceptionDialog dlg = new ExceptionDialog(ex, true))
				{
					dlg.Setup();
					dlg.ShowDialog();
				}
				General.Terminate(false);
			}
			catch(Exception failure)
			{
				Fallback(ex, failure);
			}
			Kill();
		}

		// Marshals the dialog to the UI thread, or falls back to a helper-thread backup when the UI doesn't respond
		private static void HandleWorkerCrash(Exception ex)
		{
			CrashPickup pickup = new CrashPickup(Now(), PICKUP_WINDOW_MS, TOTAL_CAP_MS);
			ManualResetEvent done = new ManualResetEvent(false);
			MainForm main = General.MainWindow;
			bool posted = false;

			if(main != null && main.IsHandleCreated && !main.IsDisposed)
			{
				Action show = () =>
				{
					if(!pickup.TryPickUp())
					{
						// A second post from a retried BeginInvoke: the first one owns the dialog
						if(pickup.IsPickedUp) return;

						// Lost to the worker's timeout: park so a live UI can't race the helper's backup
						Log("Crash pickup arrived after the worker gave up, parking the UI thread");
						Park();
					}
					try { ShowTerminal(ex); }
					finally { done.Set(); }
				};

				// BeginInvoke takes a lock, so a stop loop's Interrupt can land in it
				for(int attempt = 0; attempt < 20 && !posted; attempt++)
				{
					try
					{
						main.BeginInvoke(show);
						posted = true;
					}
					catch(ThreadInterruptedException) { }
					catch(Exception e)
					{
						Log("Unable to pass the crash to the UI thread: " + e.GetType().Name + ": " + e.Message);
						break;
					}
				}
			}

			if(posted)
			{
				while(!pickup.IsPickedUp)
				{
					WaitInterruptible(done, POLL_MS);
					if(pickup.IsPickedUp || !pickup.WindowExpired(Now(), IsMapSaving())) continue;
					if(pickup.TryAbandon()) break;
				}

				if(pickup.IsPickedUp)
				{
					// A human is reading the dialog: no deadline
					while(!WaitInterruptible(done, Timeout.Infinite)) { }
					return;
				}
				Log("The UI thread did not pick up the crash in time");
			}

			// A post that threw after it was queued can still win pickup, so claim the crash first
			if(!posted && !pickup.TryAbandon())
			{
				while(!WaitInterruptible(done, Timeout.Infinite)) { }
				return;
			}

			// Abandoned, no window, or BeginInvoke threw. The UI is blocked or parks on arrival
			Thread helper = new Thread(RunBackupOnce) { Name = "Crash backup", IsBackground = true };
			helper.Start();
			long joinuntil = Now() + BACKUP_JOIN_MS;
			while(Now() < joinuntil)
			{
				try { if(helper.Join(POLL_MS)) break; }
				catch(ThreadInterruptedException) { }
			}

			// Don't cut a map save short while the WAD write isn't atomic
			while(IsMapSaving() && !pickup.CapReached(Now()))
			{
				try { Thread.Sleep(POLL_MS); }
				catch(ThreadInterruptedException) { }
			}

			try { Console.Error.WriteLine("Ultimate Doom Builder: fatal error on thread \"" + Thread.CurrentThread.Name + "\": " + ex.GetType().Name + ": " + ex.Message + ". See UDBCrash.txt and UDBuilder.log."); }
			catch(Exception) { }
			Kill();
		}

		#endregion

		#region ================== Helpers

		// Run-once across every path, so two writers never share the temp file
		private static void RunBackupOnce()
		{
			try
			{
				if(Interlocked.Exchange(ref backupclaimed, 1) != 0) return;
				MapManager map = General.Map;
				if(map == null) return;
				if(!map.ShouldBackupOnCrash)
				{
					Log("Crash backup skipped: no unsaved map changes");
					return;
				}
				map.SaveMapBackup();
			}
			catch(Exception e)
			{
				Log("Crash backup failed: " + e);
			}
		}

		// UI thread only: the dialog itself failed
		private static void Fallback(Exception ex, Exception failure)
		{
			try { Log("The crash dialog failed: " + failure); }
			catch(Exception) { }
			RunBackupOnce();
			try
			{
				MessageBox.Show("Fatal error: " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine + Environment.NewLine
					+ "The details are in UDBCrash.txt and UDBuilder.log.", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
			}
			catch(Exception) { }
			Kill();
		}

		// WriteLogLine takes a lock, and a stop loop may be interrupting this thread
		private static void Log(string line)
		{
			for(int i = 0; i < 20; i++)
			{
				try
				{
					General.WriteLogLine(line);
					return;
				}
				catch(ThreadInterruptedException) { }
			}
		}

		private static void Park()
		{
			while(true)
			{
				try { Thread.Sleep(Timeout.Infinite); }
				catch(ThreadInterruptedException) { }
			}
		}

		// False on timeout or interrupt; callers recheck their deadline against the clock
		private static bool WaitInterruptible(WaitHandle handle, int ms)
		{
			try { return handle.WaitOne(ms); }
			catch(ThreadInterruptedException) { return false; }
		}

		private static bool IsMapSaving()
		{
			MapManager map = General.Map;
			return map != null && map.IsSaving;
		}

		private static long Now() { return clock.ElapsedMilliseconds; }

		private static void Kill()
		{
			Process.GetCurrentProcess().Kill();
		}

		#endregion
	}
}
