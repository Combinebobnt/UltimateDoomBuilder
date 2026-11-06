
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
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	internal partial class DockersControl : UserControl
	{
		#region ================== Constants
		
		#endregion

		#region ================== Delegates
		
		public event EventHandler MouseContainerEnter;
		public event EventHandler MouseContainerLeave;
		public event EventHandler Collapsed;
		public event EventHandler Expanded;
		public event EventHandler UserResize;
#if MONO_WINFORMS
		public event EventHandler CollapsedWidthChanged;
#endif

		#endregion
		
		#region ================== Variables
		
		// Behaviour
		private bool rightalign;
		private bool iscollapsed;
		
		// Collapsing
		private int expandedwidth;		// width when expanded
		
		// Splitting
		private int splitstartoffset;
		
		// Selection
		private string currentselected;
		private string previousselected;
		private bool controlledselection;

		// Docker state trace, enabled by the UDB_DOCKER_TRACE environment variable
		private static readonly bool traceenabled = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UDB_DOCKER_TRACE"));
		private static bool tracedstyles;
#if MONO_WINFORMS
		// Mono X11 hwnd internals read by the trace; hwndtraceoff after any reflection failure
		private static readonly string[] hwndfieldnames = { "mapped", "visible", "zero_sized", "expose_pending", "nc_expose_pending", "width", "height", "invalid_list", "whole_window", "client_window" };
		private static System.Reflection.MethodInfo hwndfromhandle;
		private static System.Reflection.PropertyInfo hwndmappedchain;
		private static System.Reflection.FieldInfo[] hwndfields;
		private static bool hwndtraceoff;
		private System.Windows.Forms.Timer tracetimer;
		private string tracetimerwhy;
#endif

		#endregion

		#region ================== Properties
		
		public bool IsCollpased { get { return iscollapsed; } }
		public string SelectedTabName { get { return (tabs.SelectedTab == null ? "None" : tabs.SelectedTab.Text); } } //mxd

		// This returns true when the focus is here, but not in some special cases
		public bool IsFocused
		{
			get
			{
				Control ac = FindActiveControl();
				
				// We have focus when we need the keyboard for input
				// Otherwise we don't want the focus and the docker may collapse
				return (ac is TextBox) || (ac is RichTextBox) || (ac is NumericUpDown) || (ac is ComboBox);
			}
		}
		
		#endregion
		
		#region ================== Constructor

		// Constructor
		public DockersControl()
		{
			InitializeComponent();
			expandedwidth = (int)(this.Width * (this.CurrentAutoScaleDimensions.Width / this.AutoScaleDimensions.Width));
#if MONO_WINFORMS
			tabs.TabBoundsReady += tabs_TabBoundsReady;
#endif
		}
		
		#endregion
		
		#region ================== Methods
		
		// This returns the active child control
		private Control FindActiveControl()
		{
			Control c = this.ActiveControl;
			
			while(c is IContainerControl)
			{
				IContainerControl cc = (c as IContainerControl);
				if(cc.ActiveControl != null) c = cc.ActiveControl;
				else break;
			}
			
			return c;
		}

		// Selection key of a docker; the Help docker has no full name, so fall back to its title
		private static string KeyOf(Docker d)
		{
			if(d == null) return null;
			return (string.IsNullOrEmpty(d.FullName) ? d.Title : d.FullName);
		}

		// Page index to select on expand: the stored docker, else the previous one, else the first page
		internal static int FindExpandTarget(IList<string> keys, string stored, string previous)
		{
			if(keys.Count == 0) return -1;
			int index = (string.IsNullOrEmpty(stored) ? -1 : keys.IndexOf(stored));
			if(index == -1 && !string.IsNullOrEmpty(previous)) index = keys.IndexOf(previous);
			return (index == -1 ? 0 : index);
		}

		// Selection keys of all pages, in tab order
		private List<string> GetPageKeys()
		{
			List<string> keys = new List<string>(tabs.TabPages.Count);
			foreach(TabPage page in tabs.TabPages) keys.Add(KeyOf(page.Tag as Docker));
			return keys;
		}

		// Mono's TabControl leaves deselected pages visible, so set every page's visibility explicitly
		private void SyncPageVisibility()
		{
#if MONO_WINFORMS
			// The cast matters: Mono's TabPage.Visible setter is an empty "new" property
			TabPage selected = (iscollapsed ? null : tabs.SelectedTab);
			foreach(TabPage page in tabs.TabPages) ((Control)page).Visible = (page == selected);
#endif
		}

#if MONO_WINFORMS
		// Mono's X11 driver unmaps a child squeezed to zero width by the collapsed layout and maps it
		// again only on a later real resize, so resize the selected page's docker 1 px and back
		private void NudgeSelectedPage()
		{
			Docker d = (tabs.SelectedTab != null ? tabs.SelectedTab.Tag as Docker : null);
			if(d == null || d.Control == null || d.Control.Width < 2) return;
			d.Control.Width -= 1;
			d.Control.Width += 1;
		}

		// Looks up the Mono hwnd members once; false when this Mono lacks them
		private static bool InitHwndTrace()
		{
			if(hwndtraceoff) return false;
			if(hwndfields != null) return true;
			try
			{
				const System.Reflection.BindingFlags inst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
				Type hwndtype = typeof(Control).Assembly.GetType("System.Windows.Forms.Hwnd", true);
				hwndfromhandle = hwndtype.GetMethod("ObjectFromHandle", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				hwndmappedchain = hwndtype.GetProperty("Mapped", inst);
				if(hwndfromhandle == null || hwndmappedchain == null) throw new MissingMemberException("Hwnd.ObjectFromHandle or Hwnd.Mapped");
				System.Reflection.FieldInfo[] fields = new System.Reflection.FieldInfo[hwndfieldnames.Length];
				for(int i = 0; i < fields.Length; i++)
				{
					fields[i] = hwndtype.GetField(hwndfieldnames[i], inst);
					if(fields[i] == null) throw new MissingFieldException("Hwnd", hwndfieldnames[i]);
				}
				hwndfields = fields;
				return true;
			}
			catch(Exception e)
			{
				hwndtraceoff = true;
				General.WriteLogLine("DockerTrace hwnd dump disabled: " + e.GetType().Name + ": " + e.Message);
				return false;
			}
		}

		// Appends the Mono hwnd state of a control and its whole subtree
		private static void AppendHwndTree(StringBuilder sb, Control c, int depth)
		{
			if(hwndtraceoff) return;
			sb.AppendFormat("\n    {0}{1} ({2}) visible={3} bounds={4}", new string(' ', depth * 2), c.Name, c.GetType().Name, c.Visible, c.Bounds);
			if(c.IsHandleCreated)
			{
				try
				{
					object hwnd = hwndfromhandle.Invoke(null, new object[] { c.Handle });
					if(hwnd == null)
					{
						sb.Append(" hwnd=(none)");
					}
					else
					{
						sb.AppendFormat(" Mapped={0}", hwndmappedchain.GetValue(hwnd, null));
						for(int i = 0; i < hwndfields.Length; i++)
						{
							object v = hwndfields[i].GetValue(hwnd);
							if(v is System.Collections.ICollection) v = ((System.Collections.ICollection)v).Count;
							else if(v is IntPtr) v = "0x" + ((IntPtr)v).ToInt64().ToString("x");
							sb.AppendFormat(" {0}={1}", hwndfieldnames[i], v);
						}
					}
				}
				catch(Exception e)
				{
					hwndtraceoff = true;
					General.WriteLogLine("DockerTrace hwnd dump disabled: " + e.GetType().Name + ": " + e.Message);
					return;
				}
			}
			else
			{
				sb.Append(" handle=False");
			}

			foreach(Control child in c.Controls) AppendHwndTree(sb, child, depth + 1);
		}

		// Dumps the selected page's hwnd tree about 300 ms later, once queued paints are served
		private void TraceDeferred(string why)
		{
			if(!traceenabled) return;
			if(tracetimer == null)
			{
				tracetimer = new System.Windows.Forms.Timer();
				tracetimer.Interval = 300;
				tracetimer.Tick += delegate
				{
					tracetimer.Stop();
					Trace(tracetimerwhy, true);
				};
			}
			tracetimer.Stop();
			tracetimerwhy = why;
			tracetimer.Start();
		}
#endif

		// Writes the docker state to the log when UDB_DOCKER_TRACE is set
		private void Trace(string why) { Trace(why, false); }

		// With hwnds, also dumps the selected page's Mono hwnd tree (Mono only)
		private void Trace(string why, bool hwnds)
		{
			if(!traceenabled) return;

			StringBuilder sb = new StringBuilder();
			sb.AppendFormat("DockerTrace {0}: collapsed={1} selidx={2} previous={3} current={4} width={5} tabs={6} tabshandle={7} display={8}",
				why, iscollapsed, tabs.SelectedIndex, previousselected ?? "(null)", currentselected ?? "(null)", this.Width, tabs.Bounds, tabs.IsHandleCreated, tabs.DisplayRectangle);
			if(!tracedstyles)
			{
				tracedstyles = true;
				sb.AppendFormat(" visualstyles={0}", VisualStyleInformation.IsSupportedByOS && VisualStyleInformation.IsEnabledByUser);
			}

			for(int i = 0; i < tabs.TabPages.Count; i++)
			{
				TabPage page = tabs.TabPages[i];
				Docker d = page.Tag as Docker;
				Control dc = (d != null ? d.Control : null);
				sb.AppendFormat("\n  [{0}] \"{1}\" visible={2} bounds={3}", i, page.Text, ((Control)page).Visible, page.Bounds);
				if(dc == null) continue;
				sb.AppendFormat(" control visible={0} bounds={1} handle={2}", dc.Visible, dc.Bounds, dc.IsHandleCreated);
				foreach(Control child in dc.Controls)
					sb.AppendFormat("\n    {0} visible={1} bounds={2} handle={3}", child.Name, child.Visible, child.Bounds, child.IsHandleCreated);
			}

#if MONO_WINFORMS
			if(hwnds && tabs.SelectedTab != null && InitHwndTrace())
			{
				sb.AppendFormat("\n  hwnd tree of \"{0}\":", tabs.SelectedTab.Text);
				AppendHwndTree(sb, tabs.SelectedTab, 0);
			}
#endif

			General.WriteLogLine(sb.ToString());
		}

		// This sets up the controls for left or right alignment
		public void Setup(bool right)
		{
			int voffset = pinbutton.Bottom + pinbutton.Margin.Bottom; //mxd
			rightalign = right;
			if(rightalign)
			{
				splitter.Dock = DockStyle.Left;
				tabs.Alignment = TabAlignment.Right;
				tabs.Location = new Point(0, voffset);
			}
			else
			{
				splitter.Dock = DockStyle.Right;
				tabs.Alignment = TabAlignment.Left;
				tabs.Location = new Point(-2, voffset);
			}

			tabs.Size = new Size(this.ClientRectangle.Width + 2, this.ClientRectangle.Height - voffset);
			tabs.SendToBack();
			UpdatePinIcon(); //mxd
			Trace("Setup");
		}
		
		// This collapses the docker
		public void Collapse()
		{
			if(iscollapsed) return;

			controlledselection = true;
			splitter.Enabled = false;
			splitter.BackColor = SystemColors.Control;
			splitter.Width = (int)(2.0f * (this.CurrentAutoScaleDimensions.Width / this.AutoScaleDimensions.Width));
			if(tabs.SelectedTab != null) currentselected = KeyOf(tabs.SelectedTab.Tag as Docker);
			expandedwidth = this.Width;
			tabs.SelectedIndex = -1;
			SyncPageVisibility(); // Hide before shrinking, so no page is squeezed to zero width while visible
			General.MainWindow.LockUpdate();
			if(rightalign) this.Left = this.Right - GetCollapsedWidth();
			this.Width = GetCollapsedWidth();
			General.MainWindow.UnlockUpdate();
			this.Invalidate(true);
			controlledselection = false;
			
			iscollapsed = true;
			SyncPageVisibility();
			Trace("Collapse");

			if(Collapsed != null) Collapsed(this, EventArgs.Empty);
		}
		
		// This expands the docker
		public void Expand()
		{
			if(!iscollapsed) return;

			controlledselection = true;
			splitter.Enabled = true;
			splitter.BackColor = Color.Transparent;
			splitter.Width = (int)(4.0f * (this.CurrentAutoScaleDimensions.Width / this.AutoScaleDimensions.Width));
			General.MainWindow.LockUpdate();
			if(rightalign) this.Left = this.Right - expandedwidth;
			this.Width = expandedwidth;
			General.MainWindow.UnlockUpdate();
			tabs.SelectedIndex = FindExpandTarget(GetPageKeys(), currentselected, previousselected);
			tabs.Invalidate(true);
			controlledselection = false;

			iscollapsed = false;
			SyncPageVisibility();
#if MONO_WINFORMS
			NudgeSelectedPage();
#endif
			Trace("Expand", true);
#if MONO_WINFORMS
			TraceDeferred("Expand+300ms");
#endif

			if(Expanded != null) Expanded(this, EventArgs.Empty);
		}
		
		// This calculates the collapsed width
		public int GetCollapsedWidth()
		{
			Rectangle r;
			if(tabs.TabPages.Count > 0)
				r = tabs.GetTabRect(0);
			else
				r = new Rectangle(0, 0, 26, 26);
			return r.Width + (int)(1.0f * (this.CurrentAutoScaleDimensions.Width / this.AutoScaleDimensions.Width));
		}
		
		// This adds a docker
		public void Add(Docker d, bool notify)
		{
			// Set up page
			TabPage page = new TabPage(d.Title);
			page.SuspendLayout();
			page.Font = this.Font;
			page.Tag = d;
			page.UseVisualStyleBackColor = false;
			page.Controls.Add(d.Control);
			d.Control.Dock = DockStyle.Fill;
			tabs.TabPages.Add(page);
			page.ResumeLayout(true);
			if(iscollapsed)
			{
				tabs.SelectedIndex = -1;
				if(notify) tabs.PlayNotifyAnimation(tabs.TabPages.Count - 1); //mxd
			}
			
			// Go for all controls to add events
			Queue<Control> todo = new Queue<Control>();
			todo.Enqueue(d.Control);
			while(todo.Count > 0)
			{
				Control c = todo.Dequeue();
				c.MouseEnter += RaiseMouseContainerEnter;
				c.MouseLeave += RaiseMouseContainerLeave;
				foreach(Control cc in c.Controls)
					todo.Enqueue(cc);
			}

			SyncPageVisibility();
			Trace("Add " + d.Title);
		}
		
		// This removes a docker
		public bool Remove(Docker d)
		{
			foreach(TabPage page in tabs.TabPages)
			{
				if((page.Tag as Docker) == d)
				{
					// Go for all controls to remove events
					Queue<Control> todo = new Queue<Control>();
					todo.Enqueue(d.Control);
					while(todo.Count > 0)
					{
						Control c = todo.Dequeue();
						c.MouseEnter -= RaiseMouseContainerEnter;
						c.MouseLeave -= RaiseMouseContainerLeave;
						foreach(Control cc in c.Controls)
							todo.Enqueue(cc);
					}
					
					// Take down that page
					if(page == tabs.SelectedTab || tabs.SelectedTab == null) SelectPrevious();
					page.Controls.Clear();
					tabs.TabPages.Remove(page);
					SyncPageVisibility();
					Trace("Remove " + d.Title);
					return true;
				}
			}
			
			return false;
		}

		//mxd. This checks if given docker exists in this control
		public bool Contains(Docker d) 
		{
			foreach(TabPage page in tabs.TabPages) 
				if((page.Tag as Docker) == d) return true;
			return false;
		}
		
		// This selects a docker
		public bool SelectDocker(Docker d)
		{
			foreach(TabPage page in tabs.TabPages)
			{
				if((page.Tag as Docker) == d)
				{
					if(iscollapsed)
					{
						if(!string.IsNullOrEmpty(currentselected)) previousselected = currentselected; //mxd
						currentselected = KeyOf(d); //mxd
					}
					else
						tabs.SelectedTab = page;

					Trace("SelectDocker " + d.Title);
					return true;
				}
			}
			
			return false;
		}
		
		// This selectes the previous docker
		public void SelectPrevious()
		{
			//mxd. First one is better than nothing
			if(string.IsNullOrEmpty(previousselected) && tabs.TabPages.Count > 0)
			{
				previousselected = KeyOf(tabs.TabPages[0].Tag as Docker);
			}

			if(!string.IsNullOrEmpty(previousselected))
			{
				foreach(TabPage page in tabs.TabPages)
				{
					if(KeyOf(page.Tag as Docker) == previousselected)
					{
						if(iscollapsed)
							currentselected = previousselected;
						else
							tabs.SelectedTab = page;

						break;
					}
				}
			}

			Trace("SelectPrevious");
		}
		
		// This sorts tabs by their full name
		public void SortTabs(IEnumerable<string> fullnames)
		{
			Dictionary<string, TabPage> pages = new Dictionary<string, TabPage>(tabs.TabPages.Count, StringComparer.Ordinal);
			foreach(TabPage p in tabs.TabPages) pages.Add((p.Tag as Docker).FullName, p);
			tabs.TabPages.Clear();
			
			// Add tabs in order as in fullnames
			foreach(string name in fullnames)
			{
				if(pages.ContainsKey(name))
				{
					tabs.TabPages.Add(pages[name]);
					pages.Remove(name);
				}
			}
			
			// Add remaining tabs
			foreach(KeyValuePair<string, TabPage> p in pages)
				tabs.TabPages.Add(p.Value);
		}

		//mxd
		private void UpdatePinIcon() 
		{
			if(tabs.Alignment == TabAlignment.Left)
				pinbutton.Image = (General.Settings.CollapseDockers ? Properties.Resources.DockerCollapse : Properties.Resources.DockerExpand);
			else
				pinbutton.Image = (General.Settings.CollapseDockers ? Properties.Resources.DockerExpand : Properties.Resources.DockerCollapse);
		}
		
		#endregion
		
		#region ================== Events
		
		// This raises the MouseContainerEnter event
		private void RaiseMouseContainerEnter(object sender, EventArgs e)
		{
			if(MouseContainerEnter != null)
				MouseContainerEnter(sender, e);
		}

		// This raises the MouseContainerLeave event
		private void RaiseMouseContainerLeave(object sender, EventArgs e)
		{
			if(MouseContainerLeave != null)
				MouseContainerLeave(sender, e);
		}
		
		// We don't want the focus
		private void tabs_Enter(object sender, EventArgs e) { General.MainWindow.FocusDisplay(); }
		private void tabs_MouseUp(object sender, MouseEventArgs e) { General.MainWindow.FocusDisplay(); }
		private void tabs_Selected(object sender, TabControlEventArgs e) { General.MainWindow.FocusDisplay(); }
		
		// Tab selected
		private void tabs_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(!controlledselection)
			{
				// Keep track of previous selected tab
				if(!string.IsNullOrEmpty(currentselected)) previousselected = currentselected;

				if(tabs.SelectedTab != null)
				{
					Docker d = (tabs.SelectedTab.Tag as Docker);
					currentselected = KeyOf(d);
				}
				else
				{
					currentselected = null;
				}
			}
			
			SyncPageVisibility();
			General.MainWindow.FocusDisplay();
			bool tabclick = (!controlledselection && !iscollapsed);
#if MONO_WINFORMS
			// A page first shown by a tab click can carry the same unmapped hwnd as on expand
			if(tabclick) NudgeSelectedPage();
#endif
			Trace("SelectedIndexChanged", tabclick);
#if MONO_WINFORMS
			if(tabclick) TraceDeferred("SelectedIndexChanged+300ms");
#endif
		}

#if MONO_WINFORMS
		// A Collapse() before the tabs had a handle used an empty tab rect, so apply the real width
		private void tabs_TabBoundsReady(object sender, EventArgs e)
		{
			if(!iscollapsed) return;
			int w = GetCollapsedWidth();
			if(this.Width == w) return;
			if(rightalign) this.Left = this.Right - w;
			this.Width = w;
			if(CollapsedWidthChanged != null) CollapsedWidthChanged(this, EventArgs.Empty);
			Trace("CollapsedResync");
		}
#endif

		// Splitting begins
		private void splitter_MouseDown(object sender, MouseEventArgs e)
		{
			if(e.Button == MouseButtons.Left)
			{
				splitstartoffset = e.X;
				splitter.BackColor = SystemColors.Highlight;
			}
		}
		
		// Splitting ends
		private void splitter_MouseUp(object sender, MouseEventArgs e)
		{
			splitter.BackColor = Color.Transparent;
			tabs.Invalidate(true);
			this.Update();
			General.MainWindow.RedrawDisplay();
			General.MainWindow.Update();
			Trace("SplitterMouseUp", true);
#if MONO_WINFORMS
			TraceDeferred("SplitterMouseUp+300ms");
#endif
		}

		// Splitting dragged
		private void splitter_MouseMove(object sender, MouseEventArgs e)
		{
			if(e.Button == MouseButtons.Left)
			{
				General.MainWindow.LockUpdate();
				
				// Resize the control
				int delta = e.X - splitstartoffset;
				int collapsedwidth = GetCollapsedWidth();
				if(rightalign)
				{
					if((this.Width > collapsedwidth) || (delta < 0))
					{
						this.Left += delta;
						this.Width -= delta;
						if(this.Width < collapsedwidth)
						{
							this.Left -= collapsedwidth - this.Width;
							this.Width = collapsedwidth;
						}
					}
				}
				else
				{
					if((this.Width > collapsedwidth) || (delta > 0))
					{
						this.Width += delta;
						if(this.Width < collapsedwidth)
							this.Width = collapsedwidth;
					}
				}

				General.MainWindow.UnlockUpdate();
				this.Update();
				General.MainWindow.RedrawDisplay();
				General.MainWindow.Update();
				
				// Raise event
				if(UserResize != null) UserResize(this, EventArgs.Empty);
			}
		}

		//mxd
		private void pinbutton_Click(object sender, EventArgs e)
		{
			General.Settings.CollapseDockers = !General.Settings.CollapseDockers;
			General.MainWindow.SetupInterface();
			UpdatePinIcon();
		}

		//mxd
		private void DockersControl_Resize(object sender, EventArgs e)
		{
			pinbutton.Width = this.Width - pinbutton.Margin.Left - pinbutton.Margin.Right;
		}
		
		#endregion
	}
}
