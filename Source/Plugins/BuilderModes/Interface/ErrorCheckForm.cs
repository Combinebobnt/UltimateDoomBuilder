
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Windows;
using System.Reflection;
using System.Globalization;
using System.Threading;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	public partial class ErrorCheckForm : DelayedForm
	{
		#region ================== Constants

		private const int CHECKER_LOG_ATTEMPTS = 10;
		private const int FLUSH_MAX_RESULTS = 2000;
		private const int START_GUARD_MS = 500;

		#endregion

		#region ================== Variables
		
		private volatile bool running;
		private Thread checksthread;
		private BlockMap<BlockEntry> blockmap;
		private Size initialsize; //mxd
		private List<ErrorResult> resultslist; //mxd 
		private List<Type> hiddentresulttypes; //mxd 
		private bool bathselectioninprogress; //mxd

		// Checker threads queue results and progress here; flushtimer moves them into the controls in batches
		private readonly object pendinglock = new object();
		private List<ErrorResult> pendingresults = new List<ErrorResult>();
		private int pendingprogress;
		private int pendingmaximum = -1;
		private int progressvalue;
		private System.Windows.Forms.Timer flushtimer;
		private Stopwatch stoppedwatch;
#if MONO_WINFORMS
		private readonly Dictionary<ErrorResult, string> resultkeys = new Dictionary<ErrorResult, string>();
		private int widestresult;
#endif

		#endregion

		#region ================== Properties
		
		public List<ErrorResult> SelectedResults { 
			get 
			{
				List<ErrorResult> selection = new List<ErrorResult>();
				foreach(Object ro in results.SelectedItems)
				{
					ErrorResult result = ro as ErrorResult;
					if(result == null) continue;
					selection.Add(result);
				}
				return selection;
			} 
		}
		public BlockMap<BlockEntry> BlockMap { get { return blockmap; } }
		
		#endregion
		
		#region ================== Constructor / Show

		// Constructor
		public ErrorCheckForm()
		{
			// Initialize
			InitializeComponent();
#if MONO_WINFORMS
			results.Sorted = false;
			results.DrawMode = DrawMode.OwnerDrawFixed;
			results.DrawItem += results_DrawItem;
#endif
			
			// Find all error checkers
			Type[] checkertypes = BuilderPlug.Me.FindClasses(typeof(ErrorChecker));
			foreach(Type t in checkertypes)
			{
				object[] attr = t.GetCustomAttributes(typeof(ErrorCheckerAttribute), true);
				if(attr.Length > 0)
				{
					//mxd. Skip this check?..
					ErrorChecker checker;

					try
					{
						// Create instance
						checker = (ErrorChecker)Assembly.GetExecutingAssembly().CreateInstance(t.FullName, false, BindingFlags.Default, null, null, CultureInfo.CurrentCulture, new object[0]);
					}
					catch(TargetInvocationException ex)
					{
						// Error!
						General.ErrorLogger.Add(ErrorType.Error, "Failed to create class instance \"" + t.Name + "\"");
						General.WriteLogLine(ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
						throw;
					}
					catch(Exception ex)
					{
						// Error!
						General.ErrorLogger.Add(ErrorType.Error, "Failed to create class instance \"" + t.Name + "\"");
						General.WriteLogLine(ex.GetType().Name + ": " + ex.Message);
						throw;
					}

					if(checker.SkipCheck) continue;
					
					ErrorCheckerAttribute checkerattr = (attr[0] as ErrorCheckerAttribute);

					// Add the type to the checkbox list
					string checkclassname = t.Name.ToLowerInvariant();
					CheckBox c = checks.Add(checkerattr.DisplayName, t);
					c.Checked = General.Settings.ReadPluginSetting("errorchecks." + checkclassname, checkerattr.DefaultChecked);
				}
			}
			checks.Sort(); //mxd

			//mxd. Store initial height
			initialsize = this.Size;
			resultslist = new List<ErrorResult>();
			hiddentresulttypes = new List<Type>();

			flushtimer = new GuardedTimer(components);
			flushtimer.Interval = 100;
			flushtimer.Tick += (sender, e) => FlushPending(false);
		}
		
		// This shows the window
		public void Show(Form owner)
		{
			// Move controls according to the height of the checkers box
			checks.PerformLayout();
			buttoncheck.Top = checks.Bottom + 14;
			resultspanel.Top = buttoncheck.Bottom + 14;
			this.Text = "Map Analysis"; //mxd

			// Position at left-top of owner
			this.Location = new Point(owner.Location.X + 20, owner.Location.Y + 90);
			
			// Close results part
			resultspanel.Visible = false;
#if MONO_WINFORMS
			// Mono fix: Control.SetBoundsCore no-ops (skipping the anchor engine's refresh of its
			// cached distance-to-parent-bottom baseline) when a size assignment exactly matches the
			// control's current size. Without this, if resultspanel is already at its "open" height
			// from a previous cycle, StartChecking()'s size assignment becomes a no-op and the anchor
			// baseline is never refreshed - a later automatic relayout pass then recomputes the panel's
			// height from that stale baseline, pushing resultinfo's help text off-screen. Forcing a real
			// size change here, while the panel is still invisible, guarantees StartChecking()'s
			// assignment always refreshes the baseline.
			resultspanel.Size = new Size(resultspanel.Width, 0);
#endif
			this.Size = new Size(initialsize.Width, this.Height - this.ClientSize.Height + resultspanel.Top);
			this.MinimumSize = this.Size; //mxd
			this.MaximumSize = this.Size; //mxd
			
			// Show window
			base.Show(owner);
		}
		
		#endregion
		
		#region ================== Thread Calls

		// A blocking Invoke per result serialized every checker on the UI thread, and each single Add
		// re-sorts the whole sorted list under Mono, so many results froze the editor
		public void SubmitResult(ErrorResult result)
		{
			lock(pendinglock) pendingresults.Add(result);
		}

		private void SetProgressMaximum(int maximum)
		{
			Interlocked.Exchange(ref pendingmaximum, maximum);
		}

		public void AddProgressValue(int value)
		{
			Interlocked.Add(ref pendingprogress, value);
		}

		// UI thread only
		private void FlushPending(bool all)
		{
			List<ErrorResult> batch = null;
			lock(pendinglock)
			{
				if(pendingresults.Count > 0)
				{
					if(all || pendingresults.Count <= FLUSH_MAX_RESULTS)
					{
						batch = pendingresults;
						pendingresults = new List<ErrorResult>();
					}
					else
					{
						batch = pendingresults.GetRange(0, FLUSH_MAX_RESULTS);
						pendingresults.RemoveRange(0, FLUSH_MAX_RESULTS);
					}
				}
			}

			int maximum = Interlocked.Exchange(ref pendingmaximum, -1);
			if(maximum >= 0) progress.Maximum = maximum;
			progressvalue += Interlocked.Exchange(ref pendingprogress, 0);
			progress.Value = Math.Min(progressvalue, progress.Maximum);

			if(batch == null) return;

			List<ErrorResult> visible = new List<ErrorResult>(batch.Count);
			foreach(ErrorResult result in batch)
			{
				if(!result.IsHidden && !hiddentresulttypes.Contains(result.GetType())) visible.Add(result); //mxd
			}

#if MONO_WINFORMS
			AddResultsMono(batch, visible);
#else
			if(visible.Count > 0)
			{
				results.BeginUpdate();
				results.Items.AddRange(visible.ToArray());
				results.EndUpdate();
			}
#endif

			resultslist.AddRange(batch); //mxd
			UpdateTitle();
		}

#if MONO_WINFORMS
		private string GetResultKey(ErrorResult result)
		{
			string key;
			if(!resultkeys.TryGetValue(result, out key)) resultkeys[result] = key = result.ToString();
			return key;
		}

		private void AddResultsMono(List<ErrorResult> batch, List<ErrorResult> visible)
		{
			foreach(ErrorResult result in batch) GetResultKey(result);
			if(visible.Count == 0) return;
			foreach(ErrorResult result in visible)
				widestresult = Math.Max(widestresult, TextRenderer.MeasureText(GetResultKey(result), results.Font).Width);

			ErrorResult[] listed = new ErrorResult[results.Items.Count];
			results.Items.CopyTo(listed, 0);
			int[] selected = new int[results.SelectedIndices.Count];
			results.SelectedIndices.CopyTo(selected, 0);
			int top = results.TopIndex;
			int[] oldtonew;
			List<ErrorResult> merged = SortedMerge.Merge(listed, visible, GetResultKey, out oldtonew);

			bathselectioninprogress = true;
			results.BeginUpdate();
			try
			{
				results.Items.Clear();
				results.Items.AddRange(merged.ToArray());
				results.HorizontalExtent = widestresult + 4;
				ArrayList selection = GetMonoSelectionList();
				foreach(int index in selected)
				{
					if(selection != null) selection.Add(oldtonew[index]);
					else results.SelectedIndices.Add(oldtonew[index]);
				}
				if(top >= 0 && top < oldtonew.Length) results.TopIndex = oldtonew[top];
			}
			finally
			{
				results.EndUpdate();
				bathselectioninprogress = false;
			}
		}

		private void results_DrawItem(object sender, DrawItemEventArgs e)
		{
			if(e.Index < 0 || e.Index >= results.Items.Count) return;
			Color backcolor = ((e.State & DrawItemState.Selected) != 0 ? SystemColors.Highlight : e.BackColor);
			using(Brush background = new SolidBrush(backcolor)) e.Graphics.FillRectangle(background, e.Bounds);
			using(Brush foreground = new SolidBrush(e.ForeColor))
			using(StringFormat format = new StringFormat(StringFormatFlags.NoWrap))
				e.Graphics.DrawString(GetResultKey((ErrorResult)results.Items[e.Index]), e.Font, foreground, e.Bounds, format);
			if((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, e.ForeColor, backcolor);
		}
#endif

		//mxd
		private void UpdateTitle()
		{
			int hiddencount = resultslist.Count - results.Items.Count;
			string title = "Map Analysis [" + resultslist.Count + " results";
			if(hiddencount > 0) title += ", " + hiddencount + " hidden";
			title += ", " + results.SelectedItems.Count + " selected";
			this.Text = title + @"]";
		}
		
		// This stops checking (UI thread; owner is the management thread that ran the checks)
		private void StopChecking(Thread owner)
		{
			// Stale call queued by a run CloseWindow already finished
			if(!running || checksthread != owner) return;

			flushtimer.Stop();
			FlushPending(true);
			checksthread = null;
			progress.Value = 0;
			buttoncheck.Text = "Start Analysis";
			Cursor.Current = Cursors.Default;
			running = false;
			blockmap.Dispose();
			blockmap = null;

			// When no results found, show "no results" and disable the list
			if(resultslist.Count == 0)
			{
				results.Items.Add(new ResultNoErrors());
				results.Enabled = false;
				UpdateTitle(); //mxd
			}
			else
			{
				ClearSelectedResult(); //mxd
			}
			if(stoppedwatch == null) stoppedwatch = new Stopwatch();
			stoppedwatch.Restart();
		}
		
		// This starts checking
		private void StartChecking()
		{
			if(running) return;
			
			Cursor.Current = Cursors.WaitCursor;
			
			// Make blockmap
			RectangleF area = MapSet.CreateArea(General.Map.Map.Vertices);
			area = MapSet.IncreaseArea(area, General.Map.Map.Things);
			blockmap = new BlockMap<BlockEntry>(area);
			blockmap.AddLinedefsSet(General.Map.Map.Linedefs);
			blockmap.AddSectorsSet(General.Map.Map.Sectors);
			blockmap.AddThingsSet(General.Map.Map.Things);
			blockmap.AddVerticesSet(General.Map.Map.Vertices); //mxd
			
			//mxd. Open the results panel
			if(!resultspanel.Visible)
			{
				this.MinimumSize = new Size();
#if MONO_WINFORMS
				// Mono fix: XplatUIX11.SetWindowMinMax reads the window's existing X11 size hints and
				// only ever ORs new PMinSize/PMaxSize flags in - passing Size.Empty here never clears a
				// previously-set PMaxSize, so the stale collapsed-window maximum (set by Show()) stays in
				// effect and the window manager clamps the resize below, leaving this panel 0px tall.
				this.MaximumSize = new Size(10000, 10000);
#else
				this.MaximumSize = new Size();
#endif
				this.Size = initialsize;
				resultspanel.Size = new Size(resultspanel.Width, this.ClientSize.Height - resultspanel.Top);
				resultspanel.Visible = true;
			}
			progress.Value = 0;
			progressvalue = 0;
			pendingprogress = 0;
			pendingmaximum = -1;
			lock(pendinglock) pendingresults.Clear();
			results.Items.Clear();
#if MONO_WINFORMS
			resultkeys.Clear();
			widestresult = 0;
			results.HorizontalExtent = 0;
#endif
			results.Enabled = true;
			resultslist = new List<ErrorResult>(); //mxd
			ClearSelectedResult();
			buttoncheck.Text = "Abort Analysis";
			General.Interface.RedrawDisplay();
			
			// Start checking
			running = true;
			checksthread = new Thread(RunChecks);
			checksthread.Name = "Error Checking Management";
			checksthread.Priority = ThreadPriority.Normal;
			checksthread.Start();
			flushtimer.Start();
			
			Cursor.Current = Cursors.Default;
		}

		#endregion

		#region ================== Methods
		
		// This stops the checking
		public void CloseWindow()
		{
			// Currently running?
			if(running)
			{
				Cursor.Current = Cursors.WaitCursor;
				Thread owner = checksthread;
				owner.Interrupt();

				// Wait so no checker still reads the map once the mode lets editing resume
				if(owner.Join(5000)) StopChecking(owner);
				Cursor.Current = Cursors.Default;
			}

			ClearSelectedResult();
			
			//mxd. Clear results 
			resultslist.Clear();
#if MONO_WINFORMS
			resultkeys.Clear();
#endif
			results.Items.Clear();

			// Write checked status of checks to the config
			foreach(CheckBox c in checks.Checkboxes)
			{
				Type t = c.Tag as Type;

				if (t != null)
					General.Settings.WritePluginSetting("errorchecks." + t.Name.ToLowerInvariant(), c.Checked);
			}

			this.Hide();
		}

		// This clears the selected result
		private void ClearSelectedResult()
		{
			results.SelectedItems.Clear(); //mxd
			if(results.Items.Count == 0 && resultslist.Count > 0) //mxd
				resultinfo.Text = "All results are hidden. Use context menu to show them.";
			else
				resultinfo.Text = "Select a result from the list to see more information.\r\nHold 'Ctrl' to select several results.\r\nHold 'Shift' to select a range of results.\r\nRight-click on a result to show context menu.";
			resultinfo.Enabled = false;
			fix1.Visible = false;
			fix2.Visible = false;
			fix3.Visible = false;

			UpdateTitle(); //mxd
		}
		
		// This runs in a seperate thread to manage the checking threads
		private void RunChecks()
		{
			List<ErrorChecker> checkers = new List<ErrorChecker>();
			List<Thread> threads = new List<Thread>();
			int maxthreads = Environment.ProcessorCount;
			int totalprogress = 0;
			int nextchecker = 0;
			
			// Initiate all checkers
			foreach(CheckBox c in checks.Checkboxes)
			{
				// Include this one?
				if(c.Checked)
				{
					Type t = (c.Tag as Type);
					ErrorChecker checker;
					
					try
					{
						// Create instance
						checker = (ErrorChecker)Assembly.GetExecutingAssembly().CreateInstance(t.FullName, false, BindingFlags.Default, null, null, CultureInfo.CurrentCulture, new object[0]);
					}
					catch(TargetInvocationException ex)
					{
						// Error!
						General.ErrorLogger.Add(ErrorType.Error, "Failed to create class instance \"" + t.Name + "\"");
						General.WriteLogLine(ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
						throw;
					}
					catch(Exception ex)
					{
						// Error!
						General.ErrorLogger.Add(ErrorType.Error, "Failed to create class instance \"" + t.Name + "\"");
						General.WriteLogLine(ex.GetType().Name + ": " + ex.Message);
						throw;
					}
					
					// Add to list
					if(checker != null)
					{
						checkers.Add(checker);
						totalprogress += checker.TotalProgress;
					}
				}
			}
			
			// Sort the checkers with highest cost first
			// See CompareTo method in ErrorChecker for sorting comparison
			checkers.Sort();
			
			// Setup
			SetProgressMaximum(totalprogress);
			
			// Continue while threads are running or checks are to be done
			while((nextchecker < checkers.Count) || (threads.Count > 0))
			{
				// Start new thread when less than maximum number of
				// threads running and there is more work to be done
				while((threads.Count < maxthreads) && (nextchecker < checkers.Count))
				{
					ErrorChecker c = checkers[nextchecker++];
					Thread t = new Thread(() => RunChecker(c));
					t.Name = "Error Checker '" + c.GetType().Name + "'";
					t.Priority = ThreadPriority.BelowNormal;
					t.Start();
					threads.Add(t);
				}
				
				// Remove threads that are done
				for(int i = threads.Count - 1; i >= 0; i--)
					if(!threads[i].IsAlive) threads.RemoveAt(i);
				
				// Handle thread interruption
				try { Thread.Sleep(1); }
				catch(ThreadInterruptedException) { break; }
			}
			
			// Stop all running threads
			foreach(Thread t in threads)
			{
				while(t.IsAlive)
				{
					try
					{ 
						t.Interrupt();
						t.Join(1);
					}
					catch(ThreadInterruptedException)
					{
						// We have to continue, we can't just leave the other threads running!
					}
				}
			}
			
			// Done. Async, so CloseWindow can Join this thread from the UI thread without deadlocking
			Thread self = Thread.CurrentThread;
			try { BeginInvoke(new MethodInvoker(() => StopChecking(self))); }
			catch(InvalidOperationException) { } // Window handle already gone
		}

		// An exception escaping a checker thread reaches the AppDomain handler, whose dialog on this thread crashes Mono
		private static void RunChecker(ErrorChecker checker)
		{
			try
			{
				checker.Run();
			}
			catch(ThreadInterruptedException) { }
			catch(Exception e)
			{
				// The log goes first: during abort a contended lock in ErrorLogger.Add throws ThreadInterruptedException
				WriteCheckerFailure(checker, e);
				try { General.ErrorLogger.Add(ErrorType.Error, "Map Analysis check \"" + checker.GetType().Name + "\" failed: " + e.GetType().Name + ": " + e.Message); }
				catch(ThreadInterruptedException) { }
				catch(Exception adderror) { WriteCheckerFailure(checker, adderror); }
			}
		}

		private static void WriteCheckerFailure(ErrorChecker checker, Exception e)
		{
			// Retried since the stop loop in RunChecks interrupts every millisecond or so, and this must never throw
			for(int attempt = 0; attempt < CHECKER_LOG_ATTEMPTS; attempt++)
			{
				try
				{
					General.WriteLogLine("Map Analysis check \"" + checker.GetType().Name + "\" failed: " + e);
					return;
				}
				catch(ThreadInterruptedException) { }
				catch(Exception) { return; }
			}
		}

		//mxd
		private Dictionary<Type, bool> GetSelectedTypes()
		{
			Dictionary<Type, bool> selectedtypes = new Dictionary<Type, bool>();
			foreach(var ro in results.SelectedItems)
			{
				ErrorResult r = ro as ErrorResult;
				if(r == null) continue;
				Type t = r.GetType();
				if(!selectedtypes.ContainsKey(t)) selectedtypes.Add(t, false);
			}

			return selectedtypes;
		}
		
		#endregion
		
		#region ================== Events
		
		// Window closing
		private void ErrorCheckForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			//mxd. Clear results 
			resultslist.Clear();
#if MONO_WINFORMS
			resultkeys.Clear();
#endif
			results.Items.Clear();
			
			// If the user closes the form, then just cancel the mode
			if(e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				General.Interface.Focus();
				General.Editing.CancelMode();
			}
		}
		
		// Start/stop
		private void buttoncheck_Click(object sender, EventArgs e)
		{
			// Currently running?
			if(running)
			{
				Cursor.Current = Cursors.WaitCursor;
				checksthread.Interrupt();
			}
			else
			{
				// A queued Abort must not become a Start when the final flush finishes first.
				if(stoppedwatch != null && stoppedwatch.ElapsedMilliseconds < START_GUARD_MS) return;
				StartChecking();
			}
		}

		// Close
		private void closebutton_Click(object sender, EventArgs e)
		{
			General.Interface.Focus();
			General.Editing.CancelMode();
		}
		
		// Results selection changed
		private void results_SelectedIndexChanged(object sender, EventArgs e)
		{
			//mxd
			if(bathselectioninprogress) return;
			
			// Anything selected?
			if(results.SelectedItems.Count > 0)
			{
				ErrorResult firstresult = (results.SelectedItems[0] as ErrorResult);
				if(firstresult == null)
				{
					ClearSelectedResult();
				}
				else
				{
					bool sametype = true;
					List<ErrorResult> validresults = new List<ErrorResult>();

					// Selected results have the same fixes?
					foreach(var ri in results.SelectedItems)
					{
						ErrorResult result = ri as ErrorResult;
						if(result == null) continue;
						validresults.Add(result);

						if(result.Buttons != firstresult.Buttons || result.Button1Text != firstresult.Button1Text
							|| result.Button2Text != firstresult.Button2Text || result.Button3Text != firstresult.Button3Text)
						{
							sametype = false;
							break;
						}
					}

					resultinfo.Enabled = true;

					if(sametype)
					{
						resultinfo.Text = firstresult.Description;
						fix1.Text = firstresult.Button1Text;
						fix2.Text = firstresult.Button2Text;
						fix3.Text = firstresult.Button3Text;
						fix1.Visible = (firstresult.Buttons > 0);
						fix2.Visible = (firstresult.Buttons > 1);
						fix3.Visible = (firstresult.Buttons > 2);
					}
					else
					{
						resultinfo.Text = "Several types of map analysis results are selected. To display fixes, make sure that only a single result type is selected.";
						fix1.Visible = false;
						fix2.Visible = false;
						fix3.Visible = false;
					}

					// Zoom to area
					if(validresults.Count > 0)
					{
						RectangleF zoomarea = validresults[0].GetZoomArea();
						foreach(ErrorResult result in validresults)
						{
							zoomarea = RectangleF.Union(zoomarea, result.GetZoomArea());
						}

						ClassicMode editmode = (General.Editing.Mode as ClassicMode);
						editmode.CenterOnArea(zoomarea, 0.6f);
					}
				}

				UpdateTitle(); //mxd
			}
			else
			{
				ClearSelectedResult();
			}
			
			General.Interface.RedrawDisplay();
		}
		
		// First button
		private void fix1_Click(object sender, EventArgs e)
		{
			// Anything selected?
			if(results.SelectedItems.Count > 0)
			{
				if(running)
				{
					General.ShowWarningMessage("You must stop the analysis before you can make changes to your map!", MessageBoxButtons.OK);
				}
				else
				{
					ErrorResult r = (results.SelectedItem as ErrorResult);
					if(r.Button1Click(false)) 
					{
						if(results.SelectedItems.Count > 1) FixSimilarErrors(r.GetType(), 1); //mxd
						StartChecking();
					} 
					else 
					{
						General.Interface.RedrawDisplay();
					}
				}
			}
		}
		
		// Second button
		private void fix2_Click(object sender, EventArgs e)
		{
			// Anything selected?
			if(results.SelectedIndex >= 0)
			{
				if(running)
				{
					General.ShowWarningMessage("You must stop the analysis before you can make changes to your map!", MessageBoxButtons.OK);
				}
				else
				{
					ErrorResult r = (results.SelectedItem as ErrorResult);
					if(r.Button2Click(false)) 
					{
						if(results.SelectedItems.Count > 1) FixSimilarErrors(r.GetType(), 2); //mxd
						StartChecking();
					} 
					else 
					{
						General.Interface.RedrawDisplay();
					}
				}
			}
		}
		
		// Third button
		private void fix3_Click(object sender, EventArgs e)
		{
			// Anything selected?
			if(results.SelectedIndex >= 0)
			{
				if(running)
				{
					General.ShowWarningMessage("You must stop the analysis before you can make changes to your map!", MessageBoxButtons.OK);
				}
				else
				{
					ErrorResult r = (results.SelectedItem as ErrorResult);
					if(r.Button3Click(false)) 
					{
						if(results.SelectedItems.Count > 1) FixSimilarErrors(r.GetType(), 3); //mxd
						StartChecking();
					} 
					else 
					{
						General.Interface.RedrawDisplay();
					}
				}
			}
		}

		//mxd
		private void FixSimilarErrors(Type type, int fixIndex) 
		{
			foreach(Object item in results.SelectedItems) 
			{
				if(item == results.SelectedItem) continue;
				if(item.GetType() != type) continue;

				ErrorResult r = item as ErrorResult;

				if(fixIndex == 1 && !r.Button1Click(true)) break;
				if(fixIndex == 2 && !r.Button2Click(true)) break;
				if(fixIndex == 3 && !r.Button3Click(true)) break;
			}
		}

		//mxd
		private void toggleall_CheckedChanged(object sender, EventArgs e) 
		{
			foreach(CheckBox cb in checks.Checkboxes) cb.Checked = toggleall.Checked;
		}

		private void ErrorCheckForm_HelpRequested(object sender, HelpEventArgs hlpevent)
		{
			General.ShowHelp("e_mapanalysis.html");
		}
		
		#endregion

		#region ================== Results Context Menu (mxd)

		private void resultcontextmenustrip_Opening(object sender, System.ComponentModel.CancelEventArgs e) 
		{
			//disable or enable stuff
			bool haveresult = resultslist.Count > 0 && results.SelectedItems.Count > 0;
			resultshowall.Enabled = (resultslist.Count > 0 && resultslist.Count > results.Items.Count);
			resultselectcurrenttype.Enabled = haveresult;
			resultcopytoclipboard.Enabled = haveresult;
			resulthidecurrent.Enabled = haveresult;
			resulthidecurrenttype.Enabled = haveresult;
			resultshowonlycurrent.Enabled = haveresult;
		}

		private void resultshowall_Click(object sender, EventArgs e) 
		{
			// Reset ignored items
			foreach(ErrorResult result in resultslist) result.Hide(false);
			
			// Restore items
			results.Items.Clear();
#if MONO_WINFORMS
			List<ErrorResult> sorted = new List<ErrorResult>(resultslist);
			sorted.Sort((a, b) => string.Compare(GetResultKey(a), GetResultKey(b), StringComparison.CurrentCulture));
			foreach(ErrorResult result in sorted)
				widestresult = Math.Max(widestresult, TextRenderer.MeasureText(GetResultKey(result), results.Font).Width);
			results.HorizontalExtent = widestresult + 4;
			results.Items.AddRange(sorted.ToArray());
#else
			results.Items.AddRange(resultslist.ToArray());
#endif
			hiddentresulttypes.Clear();

			// Do the obvious
			ClearSelectedResult();
		}

		private void resulthidecurrent_Click(object sender, EventArgs e) 
		{
			// Mark results to hide
			foreach(var ro in results.SelectedItems)
			{
				ErrorResult r = ro as ErrorResult;
				if(r == null) return;
				r.Hide(true);
			}
			
			// Under Mono, Items.Remove re-sorts the list per row and can throw invalidating a selected row past the end
			List<ErrorResult> remaining = new List<ErrorResult>(results.Items.Count);
			foreach(ErrorResult result in results.Items)
			{
				if(!result.IsHidden) remaining.Add(result);
			}

			int top = results.TopIndex;
			results.BeginUpdate();
			results.Items.Clear();
			results.Items.AddRange(remaining.ToArray());
			results.TopIndex = Math.Min(top, Math.Max(remaining.Count - 1, 0));
			results.EndUpdate();

			// Do the obvious
			ClearSelectedResult();
		}

		private void resulthidecurrenttype_Click(object sender, EventArgs e)
		{
			Dictionary<Type, bool> tohide = GetSelectedTypes();
			List<ErrorResult> filtered = new List<ErrorResult>();
			hiddentresulttypes.AddRange(tohide.Keys);

			// Apply filtering
			foreach(ErrorResult result in results.Items)
			{
				if(!tohide.ContainsKey(result.GetType())) filtered.Add(result);
			}

			// Replace items
			results.Items.Clear();
			results.Items.AddRange(filtered.ToArray());

			// Do the obvious
			ClearSelectedResult();
		}

		private void resultshowonlycurrent_Click(object sender, EventArgs e) 
		{
			Dictionary<Type, bool> toshow = GetSelectedTypes();
			List<ErrorResult> filtered = new List<ErrorResult>();
			hiddentresulttypes.Clear();

			// Apply filtering
			foreach(ErrorResult result in results.Items)
			{
				Type curresulttype = result.GetType();
				if(!toshow.ContainsKey(curresulttype))
				{
					hiddentresulttypes.Add(curresulttype);
				}
				else
				{
					filtered.Add(result);
				}
			}

			// Replace items
			results.Items.Clear();
			results.Items.AddRange(filtered.ToArray());

			// Do the obvious
			ClearSelectedResult();
		}

		private void resultcopytoclipboard_Click(object sender, EventArgs e)
		{
			// Get results
			StringBuilder sb = new StringBuilder();
			foreach(ErrorResult result in results.SelectedItems) sb.AppendLine(result.ToString());
			
			try
			{
				//mxd. Set on clipboard
				Clipboard.SetDataObject(sb.ToString(), true, 5, 200);

				// Inform the user
				General.Interface.DisplayStatus(StatusType.Info, "Analysis results copied to clipboard.");
			}
			catch(ExternalException)
			{
				// Inform the user
				General.Interface.DisplayStatus(StatusType.Warning, "Failed to perform a Clipboard operation...");
			}
		}

		private void results_KeyUp(object sender, KeyEventArgs e) 
		{
			// Copy descriptions to clipboard?
			if(e.Control && e.KeyCode == Keys.C)
			{
				resultcopytoclipboard_Click(sender, EventArgs.Empty);
			} 
			// Select all?
			else if(e.Control && e.KeyCode == Keys.A)
			{
				List<int> indices = new List<int>(results.Items.Count);
				for(int i = 0; i < results.Items.Count; i++) indices.Add(i);
				SelectResults(indices);
			}
		}

		private void resultselectcurrenttype_Click(object sender, EventArgs e)
		{
			Dictionary<Type, bool> toselect = GetSelectedTypes();
			List<int> indices = new List<int>();
			for(int i = 0; i < results.Items.Count; i++)
			{
				if(toselect.ContainsKey(results.Items[i].GetType())) indices.Add(i);
			}

			SelectResults(indices);
		}

		// Indices must be ascending
		private void SelectResults(List<int> indices)
		{
			// Set first, so the Clear below doesn't redraw as well
			bathselectioninprogress = true;
			results.SelectedItems.Clear();

#if MONO_WINFORMS
			// Mono's AddCore scrolls to every added row, and BeginUpdate doesn't stop it
			ArrayList selection = GetMonoSelectionList();
			if(selection != null)
			{
				foreach(int i in indices) selection.Add(i);
				results.Invalidate();
			}
			else
#endif
			{
				results.BeginUpdate();
				foreach(int i in indices) results.SelectedIndices.Add(i);
				results.EndUpdate();
			}

			bathselectioninprogress = false;
			results_SelectedIndexChanged(this, EventArgs.Empty);
		}

#if MONO_WINFORMS
		// Mono's ListBox.selected_indices.selection, the sorted list behind SelectedIndices; null if renamed
		private ArrayList GetMonoSelectionList()
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			FieldInfo indicesfield = typeof(ListBox).GetField("selected_indices", flags);
			object indices = (indicesfield != null ? indicesfield.GetValue(results) : null);
			if(indices == null) return null;
			FieldInfo selectionfield = indices.GetType().GetField("selection", flags);
			return (selectionfield != null ? selectionfield.GetValue(indices) as ArrayList : null);
		}
#endif

		#endregion

	}
}
