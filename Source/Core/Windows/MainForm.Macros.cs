
#region ================== Namespaces

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Macros;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Tools > Macros, the Macros toolbar strip and its visibility toggle
	partial class MainForm
	{
		#region ================== Variables

		private ToolStripMenuItem menumacros;
		private ToolStripMenuItem itemmacrorecord;
		private ToolStripMenuItem itemmacrorunlast;
		private ToolStripSeparator separatormacrolist;
		private ToolStripSeparator separatormacromanage;
		private ToolStripMenuItem itemmacromanage;
		private ToolStripButton buttonmacrorecord;
		private ToolStripSplitButton buttonmacrorun;
		private ToolStripMenuItem toggleMacros;
		private Image macrorecordimage;
		private Image macrostopimage;

		#endregion

		#region ================== Setup

		// Before SetupToolbarDock, which moves the toolbar items into their strip
		private void SetupMacroInterface()
		{
			macrorecordimage = DrawMacroIcon(false);
			macrostopimage = DrawMacroIcon(true);

			itemmacrorecord = new ToolStripMenuItem("&Record Macro");
			itemmacrorecord.Tag = "builder_macrorecord";
			itemmacrorecord.Image = macrorecordimage;
			itemmacrorecord.Click += InvokeTaggedAction;

			itemmacrorunlast = new ToolStripMenuItem("Run &Last Macro");
			itemmacrorunlast.Tag = "builder_macrorunlast";
			itemmacrorunlast.Image = DrawRunIcon();
			itemmacrorunlast.Click += InvokeTaggedAction;

			separatormacrolist = new ToolStripSeparator();
			separatormacromanage = new ToolStripSeparator();

			itemmacromanage = new ToolStripMenuItem("&Manage Macros...");
			itemmacromanage.Tag = "builder_macromanage";
			itemmacromanage.Click += InvokeTaggedAction;

			menumacros = new ToolStripMenuItem("&Macros");
			menumacros.Name = "menumacros";
			menumacros.DropDownItems.AddRange(new ToolStripItem[] { itemmacrorecord, itemmacrorunlast, separatormacrolist, separatormacromanage, itemmacromanage });
			menumacros.DropDownOpening += menumacros_DropDownOpening;
			menutools.DropDownItems.Insert(menutools.DropDownItems.IndexOf(separatortoolsscreenshots) + 1, menumacros);

			buttonmacrorecord = new ToolStripButton();
			buttonmacrorecord.Name = "buttonmacrorecord";
			buttonmacrorecord.DisplayStyle = ToolStripItemDisplayStyle.Image;
			buttonmacrorecord.Image = macrorecordimage;
			buttonmacrorecord.Tag = "builder_macrorecord";
			buttonmacrorecord.ToolTipText = "Record Macro";
			buttonmacrorecord.Click += InvokeTaggedAction;

			buttonmacrorun = new ToolStripSplitButton();
			buttonmacrorun.Name = "buttonmacrorun";
			buttonmacrorun.DisplayStyle = ToolStripItemDisplayStyle.Image;
			buttonmacrorun.Image = itemmacrorunlast.Image;
			buttonmacrorun.Tag = "builder_macrorunlast";
			buttonmacrorun.ToolTipText = "Run Last Macro";
			buttonmacrorun.ButtonClick += InvokeTaggedAction;
			buttonmacrorun.DropDownOpening += buttonmacrorun_DropDownOpening;
			buttonmacrorun.DropDownItems.Add(new ToolStripMenuItem("(no macros)") { Enabled = false }); // Mono shows no arrow on an empty dropdown

			toggleMacros = new ToolStripMenuItem("Macros");
			toggleMacros.Click += toggleMacros_Click;
			toolbarContextMenu.Items.Insert(toolbarContextMenu.Items.IndexOf(toggleRendering) + 1, toggleMacros);
		}

		private static Image DrawMacroIcon(bool stop)
		{
			Bitmap b = new Bitmap(ScaledIconSize.Width, ScaledIconSize.Height);
			using(Graphics g = Graphics.FromImage(b))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				float w = b.Width, h = b.Height;
				RectangleF r = new RectangleF(w * 0.2f, h * 0.2f, w * 0.6f, h * 0.6f);
				if(stop)
				{
					using(Brush br = new SolidBrush(Color.FromArgb(70, 70, 70))) g.FillRectangle(br, r);
				}
				else
				{
					using(Brush br = new SolidBrush(Color.FromArgb(215, 40, 40))) g.FillEllipse(br, r);
					using(Pen p = new Pen(Color.FromArgb(120, 10, 10))) g.DrawEllipse(p, r);
				}
			}
			return b;
		}

		private static Image DrawRunIcon()
		{
			Bitmap b = new Bitmap(ScaledIconSize.Width, ScaledIconSize.Height);
			using(Graphics g = Graphics.FromImage(b))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				float w = b.Width, h = b.Height;
				PointF[] tri = { new PointF(w * 0.25f, h * 0.15f), new PointF(w * 0.85f, h * 0.5f), new PointF(w * 0.25f, h * 0.85f) };
				using(Brush br = new SolidBrush(Color.FromArgb(40, 150, 60))) g.FillPolygon(br, tri);
				using(Pen p = new Pen(Color.FromArgb(20, 80, 30))) g.DrawPolygon(p, tri);
			}
			return b;
		}

		#endregion

		#region ================== Updating

		// Enable state and labels; called from UpdateInterface
		private void UpdateMacroInterface()
		{
			if(menumacros == null) return;
			MacroManager macros = General.Macros;
			bool maploaded = (General.Map != null);
			bool recording = (macros != null) && macros.IsRecording;
			bool running = (macros != null) && macros.IsRunning;

			menumacros.Visible = maploaded;
			itemmacrorecord.Text = recording ? "Stop &Recording" : "&Record Macro";
			itemmacrorecord.Image = recording ? macrostopimage : macrorecordimage;
			itemmacrorecord.Enabled = maploaded && !running;
			itemmacrorunlast.Enabled = maploaded && !recording && !running && (macros != null) && (macros.LastMacro != null);
			itemmacromanage.Enabled = !recording && !running;

			buttonmacrorecord.Visible = General.Settings.ToolbarMacros && maploaded;
			buttonmacrorecord.Image = itemmacrorecord.Image;
			buttonmacrorecord.Checked = recording;
			buttonmacrorecord.Enabled = itemmacrorecord.Enabled;
			buttonmacrorecord.ToolTipText = recording ? "Stop Recording" : "Record Macro";
			buttonmacrorun.Visible = General.Settings.ToolbarMacros && maploaded;
			buttonmacrorun.Enabled = maploaded && !recording && !running;
			Macro last = (macros != null) ? macros.LastMacro : null;
			buttonmacrorun.ToolTipText = (last != null) ? "Run Last Macro (" + last.Name + ")" : "Run Last Macro";
		}

		// Rebuilds a macro list between two anchors (or into an empty dropdown)
		private void FillMacroList(ToolStripItemCollection items, int index)
		{
			MacroManager macros = General.Macros;
			if(macros == null) return;
			bool canrun = (General.Map != null) && !macros.IsRecording && !macros.IsRunning;
			foreach(Macro m in macros.Store.Macros)
			{
				Macro macro = m;
				ToolStripMenuItem item = new ToolStripMenuItem(m.Name.Replace("&", "&&"));
				if(m.Slot > 0)
				{
					string action = "builder_macroslot" + m.Slot;
					int key = General.Actions.Exists(action) ? General.Actions.GetActionByName(action).ShortcutKey : 0;
					item.ShortcutKeyDisplayString = (key > 0) ? Actions.Action.GetShortcutKeyDesc(key) : "Slot " + m.Slot;
				}
				string why = macros.WhyNotRunnable(m);
				item.Enabled = canrun && (why == null);
				if(why != null && canrun) item.ToolTipText = "Can't run: " + why;
				item.Click += delegate { General.Macros.Run(macro); };
				items.Insert(index++, item);
			}
		}

		private void menumacros_DropDownOpening(object sender, EventArgs e)
		{
			UpdateMacroInterface();
			ToolStripItemCollection items = menumacros.DropDownItems;
			int start = items.IndexOf(separatormacrolist) + 1;
			while(items[start] != separatormacromanage) items.RemoveAt(start);
			FillMacroList(items, start);
			separatormacromanage.Visible = (items.IndexOf(separatormacromanage) > start);
		}

		private void buttonmacrorun_DropDownOpening(object sender, EventArgs e)
		{
			buttonmacrorun.DropDownItems.Clear();
			FillMacroList(buttonmacrorun.DropDownItems, 0);
			if(buttonmacrorun.DropDownItems.Count == 0)
				buttonmacrorun.DropDownItems.Add(new ToolStripMenuItem("(no macros)") { Enabled = false });
		}

		private void toggleMacros_Click(object sender, EventArgs e)
		{
			General.Settings.ToolbarMacros = !General.Settings.ToolbarMacros;
			UpdateToolbar();

			if(toolbarContextMenuShiftPressed)
				toggleMacros.Image = General.Settings.ToolbarMacros ? Properties.Resources.Check : null;
		}

		#endregion
	}
}
