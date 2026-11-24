
#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Windows;
using Action = CodeImp.DoomBuilder.Actions.Action;

#endregion

namespace CodeImp.DoomBuilder.Macros
{
	// Records editor actions into macros and replays them, each run as one undo level
	internal sealed class MacroManager : IDisposable
	{
		#region ================== Variables

		private readonly MacroStore store = new MacroStore();
		private bool recording;
		private List<MacroStep> recorded;
		private string recordedmode;
		private UndoManager recordingundo;
		private bool running;
		private string lastname;
		private bool isdisposed;

		// Undo levels made while recording that no recorded step accounts for (yet)
		private readonly List<string> unrecordedundos = new List<string>();
		private bool flushposted;

		#endregion

		#region ================== Properties

		public MacroStore Store { get { return store; } }
		public bool IsRecording { get { return recording; } }
		public bool IsRunning { get { return running; } }
		public int RecordedStepCount { get { return (recorded != null) ? CountSteps(recorded) : 0; } }

		// Last run or last saved this session
		public Macro LastMacro { get { return (lastname != null) ? store.ByName(lastname) : null; } }

		#endregion

		#region ================== Constructor / Disposer

		internal MacroManager()
		{
			store.Load(General.Settings.Config);
			foreach(string error in store.LoadErrors)
				General.WriteLogLine("WARNING: skipped an unreadable saved macro (" + error + "); it is dropped when macros are next saved.");
			General.Actions.BindMethods(this);
			General.Actions.ActionInvoked += OnActionInvoked;
		}

		public void Dispose()
		{
			if(isdisposed) return;
			General.Actions.ActionInvoked -= OnActionInvoked;
			General.Actions.UnbindMethods(this);
			DetachUndo();
			isdisposed = true;
		}

		#endregion

		#region ================== Queries

		internal static string CurrentModeName
		{
			get { return ((General.Editing != null) && (General.Editing.Mode != null)) ? General.Editing.Mode.GetType().Name : ""; }
		}

		internal static string ModeTitle(string modename)
		{
			if((General.Editing != null) && (General.Editing.ModesInfo != null))
				foreach(EditModeInfo info in General.Editing.ModesInfo)
					if(info.Type.Name == modename && !string.IsNullOrEmpty(info.ButtonDesc)) return info.ButtonDesc;
			return modename;
		}

		// "Sectors Mode" stays as is, "Visual" becomes "Visual mode"
		internal static string ModePhrase(string modename)
		{
			string title = ModeTitle(modename);
			return title.EndsWith(" mode", StringComparison.OrdinalIgnoreCase) ? title : title + " mode";
		}

		internal static string ActionTitle(string name)
		{
			return General.Actions.Exists(name) ? General.Actions.GetActionByName(name).Title : name;
		}

		internal static bool ActionExists(string name) { return General.Actions.Exists(name); }

		// Null when the macro can run here, else why not
		internal string WhyNotRunnable(Macro m)
		{
			if(General.Map == null) return "no map is open";
			if(running) return "a macro is already running";
			if(recording) return "a macro is being recorded";
			if((General.Editing.Mode != null) && General.Editing.Mode.Attributes.Volatile) return "finish drawing, pasting or editing the selection first";
			if(General.Map.UndoRedo.IgnorePropChanges) return "finish the current edit first";
			if(m.MalformedSteps != null) return "its saved steps can't be read";
			if(m.Steps.Count == 0) return "it has no steps";
			List<string> missing = m.MissingActions(ActionExists);
			if(missing.Count > 0) return "missing action" + (missing.Count > 1 ? "s " : " ") + string.Join(", ", missing.ToArray());
			if(!m.AnyMode && m.Mode != CurrentModeName) return "it runs in " + ModePhrase(m.Mode);
			return null;
		}

		private static int CountSteps(List<MacroStep> steps)
		{
			int n = 0;
			foreach(MacroStep s in steps) n += s.Count;
			return n;
		}

		#endregion

		#region ================== Storage

		// Writes the macros to the program configuration and saves it, so a crash loses nothing
		internal void Save()
		{
			store.Save(General.Settings.Config);
			General.SaveSettings();
		}

		// Adds or replaces a macro and saves; a rename keeps it the last macro
		internal bool Put(Macro macro, Macro existing)
		{
			bool waslast = (existing != null) && (lastname != null) && string.Equals(existing.Name, lastname, StringComparison.OrdinalIgnoreCase);
			if(!store.Put(macro, existing)) return false;
			if(waslast) lastname = macro.Name;
			Save();
			return true;
		}

		#endregion

		#region ================== Recording

		[BeginAction("macrorecord")]
		internal void ToggleRecording()
		{
			if(recording) StopRecording(true);
			else StartRecording();
		}

		internal void StartRecording()
		{
			if(recording) return;
			if(General.Map == null) return;
			if(running)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Can't record while a macro is running.");
				return;
			}

			recording = true;
			recorded = new List<MacroStep>();
			recordedmode = CurrentModeName;
			unrecordedundos.Clear();
			recordingundo = General.Map.UndoRedo;
			recordingundo.UndoRequested += OnUndoRequested;
			General.WriteLogLine("Macro recording started in " + recordedmode + ".");
			General.Interface.DisplayStatus(StatusType.Info, "Recording macro. Use Tools > Macros > Stop Recording when done.");
			General.MainWindow.UpdateInterface();
		}

		// Stops recording; with offersave, opens the save dialog for what was recorded
		internal void StopRecording(bool offersave)
		{
			if(!recording) return;
			recording = false;
			DetachUndo();
			List<MacroStep> steps = recorded;
			string mode = recordedmode;
			recorded = null;
			General.WriteLogLine("Macro recording stopped, " + CountSteps(steps) + " steps.");
			General.MainWindow.UpdateInterface();

			if(steps.Count == 0)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Macro recording stopped: nothing was recorded.");
				return;
			}
			if(!offersave) return;

			Macro m = new Macro { Name = store.UniqueName("Macro " + (store.Macros.Count + 1)), Mode = mode, Steps = steps };
			using(MacroEditForm form = new MacroEditForm(m, true))
			{
				if(form.ShowDialog(General.MainWindow) == DialogResult.OK)
				{
					Put(form.Result, null);
					lastname = form.Result.Name;
					General.Interface.DisplayStatus(StatusType.Info, "Macro '" + form.Result.Name + "' saved.");
				}
				else
				{
					General.Interface.DisplayStatus(StatusType.Info, "Recorded macro discarded.");
				}
			}
			General.MainWindow.UpdateInterface();
		}

		// The map is closing or being replaced: recording can't go on, but the steps so far are offered
		internal void OnMapClosing()
		{
			if(!recording) return;
			DetachUndo();
			General.MainWindow.BeginInvoke(new MethodInvoker(() => StopRecording(true)));
		}

		private void DetachUndo()
		{
			if(recordingundo != null) recordingundo.UndoRequested -= OnUndoRequested;
			recordingundo = null;
		}

		private void OnActionInvoked(ActionInvokedArgs e)
		{
			if(!recording) return;
			bool volatileafter = (General.Editing != null) && (General.Editing.Mode != null) && General.Editing.Mode.Attributes.Volatile;
			bool bound = e.Action.BeginBound || e.Action.EndBound;
			MacroClassification c = MacroRecording.Classify(e.Action.Name, e.Key, e.DepthAtEntry, e.VolatileBefore, volatileafter, bound);
			if(c.Record)
			{
				MacroStore.AppendStep(recorded, e.Action.Name);
				unrecordedundos.Clear();
				General.Interface.DisplayStatus(StatusType.Info, "Recording macro: " + CountSteps(recorded) + " steps (" + e.Action.Title + ").");
			}
			else if(c.Reason != null)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Not recorded: " + e.Action.Title + ", " + c.Reason + ".");
			}
		}

		// Resolved once the input that made it is handled: a recorded step clears it first
		private void OnUndoRequested(string description)
		{
			if(!recording) return;
			unrecordedundos.Add(description);
			if(flushposted) return;
			flushposted = true;
			General.MainWindow.BeginInvoke(new MethodInvoker(FlushUnrecorded));
		}

		private void FlushUnrecorded()
		{
			flushposted = false;
			if(!recording || (unrecordedundos.Count == 0)) return;

			// Still inside an action (a modal dialog): its own invocation decides
			if(Action.DelegateDepth > 0) return;
			General.Interface.DisplayStatus(StatusType.Warning, "Not recorded: " + unrecordedundos[unrecordedundos.Count - 1] + " (only editor actions are recorded).");
			unrecordedundos.Clear();
		}

		#endregion

		#region ================== Running

		[BeginAction("macrorunlast")]
		internal void RunLast()
		{
			Macro m = LastMacro;
			if(m == null)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "No macro has been run or saved yet.");
				return;
			}
			Run(m);
		}

		[BeginAction("macroslot1")] internal void RunSlot1() { RunSlot(1); }
		[BeginAction("macroslot2")] internal void RunSlot2() { RunSlot(2); }
		[BeginAction("macroslot3")] internal void RunSlot3() { RunSlot(3); }
		[BeginAction("macroslot4")] internal void RunSlot4() { RunSlot(4); }
		[BeginAction("macroslot5")] internal void RunSlot5() { RunSlot(5); }
		[BeginAction("macroslot6")] internal void RunSlot6() { RunSlot(6); }
		[BeginAction("macroslot7")] internal void RunSlot7() { RunSlot(7); }
		[BeginAction("macroslot8")] internal void RunSlot8() { RunSlot(8); }
		[BeginAction("macroslot9")] internal void RunSlot9() { RunSlot(9); }
		[BeginAction("macroslot10")] internal void RunSlot10() { RunSlot(10); }

		private void RunSlot(int slot)
		{
			Macro m = store.BySlot(slot);
			if(m == null)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "No macro is assigned to slot " + slot + ".");
				return;
			}
			Run(m);
		}

		[BeginAction("macromanage")]
		internal void ShowManager()
		{
			if(recording)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Stop recording before managing macros.");
				return;
			}
			Macro torun = null;
			using(MacroManagerForm form = new MacroManagerForm(this))
				if(form.ShowDialog(General.MainWindow) == DialogResult.OK) torun = form.MacroToRun;
			General.MainWindow.UpdateInterface();
			if(torun != null) Run(torun);
		}

		// Runs every step as one undo level; any failure rolls the whole run back
		internal bool Run(Macro m)
		{
			if(General.Map == null) return false;
			string refusal = WhyNotRunnable(m);
			if(refusal != null)
			{
				General.Interface.DisplayStatus(StatusType.Warning, "Macro '" + m.Name + "' can't run: " + refusal + ".");
				return false;
			}

			lastname = m.Name;
			running = true;
			try { return RunSteps(m); }
			finally
			{
				running = false;

				// Only an unexpected exception gets here with the run open; keep its changes undoable
				if((General.Map != null) && General.Map.UndoRedo.CompoundActive) General.Map.UndoRedo.AbandonCompound();
				if(General.Map != null) General.Interface.RedrawDisplay();
				General.MainWindow.UpdateInterface();
			}
		}

		private bool RunSteps(Macro m)
		{
			UndoManager undo = General.Map.UndoRedo;
			string failure = null;
			bool dropped = false;
			int stepnumber = 0;
			string steptitle = "";
			General.WriteLogLine("Running macro '" + m.Name + "' (" + MacroStore.EncodeSteps(m.Steps) + ")...");

			string ilogprevious = InteractionLog.BeginAmbient("macro");
			bool dialogcancelled = false;
			EventHandler oncancel = (s, e) => dialogcancelled = true;
			DelayedForm.ModalCancelled += oncancel;
			try
			{
				undo.BeginCompound("Macro: " + m.Name);
				foreach(MacroStep step in m.Steps)
				{
					stepnumber++;
					Action a = General.Actions.GetActionByName(step.Action);
					steptitle = a.Title;
					for(int i = 0; (i < step.Count) && (failure == null) && !dropped; i++)
					{
						if(!a.BeginBound && !a.EndBound) { failure = "it does nothing in " + ModePhrase(CurrentModeName); break; }

						try { General.Actions.InvokeAction(step.Action); }
						catch(Exception e)
						{
							General.WriteLogLine("Macro step " + stepnumber + " (" + step.Action + ") failed: " + e);
							General.ErrorLogger.Add(ErrorType.Error, "Macro '" + m.Name + "' step " + stepnumber + " (" + a.Title + ") failed. " + e.GetType().Name + ": " + e.Message);
							failure = "error: " + e.Message;
							break;
						}

						if(undo.CompoundDropped || (General.Map == null) || (General.Map.UndoRedo != undo)) { dropped = true; break; }
						if(undo.CompoundAborted) { failure = undo.CompoundAbortReason; break; }
						if(dialogcancelled) { failure = "its dialog was cancelled"; break; }
						if((General.Editing.Mode != null) && General.Editing.Mode.Attributes.Volatile)
						{
							failure = "it left a drawing or editing mode open";
							if(!General.Editing.CancelVolatileMode()) General.Editing.DisengageVolatileMode();
							break;
						}
					}
					if((failure != null) || dropped) break;
				}
			}
			finally
			{
				DelayedForm.ModalCancelled -= oncancel;
				InteractionLog.EndAmbient(ilogprevious);
			}

			string prefix = "Macro '" + m.Name + "'";
			if(dropped)
			{
				General.Interface.DisplayStatus(StatusType.Warning, prefix + " stopped at step " + stepnumber + " (" + steptitle + "): the map was replaced.");
			}
			else if(failure == null)
			{
				undo.EndCompound();
				General.Interface.DisplayStatus(StatusType.Action, prefix + ": " + m.StepCount + " steps.");
			}
			else
			{
				bool rolledback;
				try { rolledback = undo.RollbackCompound(); }
				catch(Exception e)
				{
					General.WriteLogLine("Rolling back macro '" + m.Name + "' failed: " + e);
					General.ErrorLogger.Add(ErrorType.Error, "Rolling back macro '" + m.Name + "' failed. " + e.GetType().Name + ": " + e.Message);
					undo.AbandonCompound();
					rolledback = false;
				}
				General.Interface.DisplayStatus(StatusType.Warning, prefix + " stopped at step " + stepnumber + " (" + steptitle + "): " + failure +
					(rolledback ? "; changes rolled back." : "; rolling back failed, use Undo."));
			}

			return (failure == null) && !dropped;
		}

		#endregion
	}
}
