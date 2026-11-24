#region === Copyright (c) 2010 Pascal van der Heiden ===

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.Plugins.VisplaneExplorer
{
	public partial class InterfaceForm : DelayedForm
	{
		#region ================== Constants

		#endregion

		#region ================== mxd. Event handlers

		public event EventHandler OnVisplaneSettingsChanged;

		#endregion

		#region ================== Variables

		private ViewStats viewstats;
		private Point oldttposition;
		private int viewheight;
		private int viewheightcustom;
		private int viewheightdefault;
		private ToolStripMenuItem visplanesmenu;

		#endregion

		#region ================== Properties

		internal ViewStats ViewStats { get { return viewstats; } }
		internal bool OpenDoors { get { return cbopendoors.Checked; } } //mxd
		internal bool ShowHeatmap { get { return cbheatmap.Checked; } } //mxd
		internal int ViewHeight { get { return viewheight; } }
		internal int ViewHeightDefault { get { return viewheightdefault; } }

		#endregion

		#region ================== Constructor / Destructor

		// Constructor
		public InterfaceForm()
		{
			viewheightdefault = General.Map.Config.VisplaneViewHeightDefault;
			InitializeComponent();
			cbopendoors.Checked = General.Settings.ReadPluginSetting("opendoors", false); //mxd
			cbheatmap.Checked = General.Settings.ReadPluginSetting("showheatmap", false); //mxd
			viewheight = General.Settings.ReadPluginSetting("viewheight", viewheightdefault);
			viewheightcustom = General.Settings.ReadPluginSetting("viewheightcustom", 0);

			RedrawViewHeightMenuItems();

			// Menu-bar twin of the toolbar controls, refilled on opening (it needs items to open at all)
			visplanesmenu = new ToolStripMenuItem("V&isplanes");
			visplanesmenu.DropDownOpening += delegate { FillVisplanesMenu(); };
			FillVisplanesMenu();
		}

		#endregion

		#region ================== Methods

		// This adds the buttons to the toolbar
		public void AddToInterface()
		{
			General.Interface.BeginToolbarUpdate(); //mxd
			General.Interface.AddButton(statsbutton);
			General.Interface.AddButton(separator); //mxd
			General.Interface.AddButton(cbopendoors); //mxd
			General.Interface.AddButton(cbheatmap); //mxd
			General.Interface.AddButton(heightbutton);
			General.Interface.EndToolbarUpdate(); //mxd
			General.Interface.AddMenu(visplanesmenu);
		}

		// This removes the buttons from the toolbar
		public void RemoveFromInterface()
		{
			General.Interface.BeginToolbarUpdate(); //mxd
			General.Interface.RemoveMenu(visplanesmenu);
			General.Interface.RemoveButton(heightbutton);
			General.Interface.RemoveButton(cbheatmap); //mxd
			General.Interface.RemoveButton(cbopendoors); //mxd
			General.Interface.RemoveButton(separator); //mxd
			General.Interface.RemoveButton(statsbutton);
			General.Interface.EndToolbarUpdate(); //mxd

			//mxd. Save settings
			General.Settings.WritePluginSetting("opendoors", cbopendoors.Checked);
			General.Settings.WritePluginSetting("showheatmap", cbheatmap.Checked);
			General.Settings.WritePluginSetting("viewheight", viewheight);
			General.Settings.WritePluginSetting("viewheightcustom", viewheightcustom);
		}

		private void FillVisplanesMenu()
		{
			visplanesmenu.DropDownItems.Clear();
			visplanesmenu.DropDownItems.Add(MirrorDropDown(statsbutton, "&Statistic"));
			visplanesmenu.DropDownItems.Add(MirrorCheckBox(cbopendoors, cbopendoors_Click));
			visplanesmenu.DropDownItems.Add(MirrorCheckBox(cbheatmap, cbheatmap_Click));
			visplanesmenu.DropDownItems.Add(MirrorDropDown(heightbutton, "View &Height"));
		}

		// Copies a toolbar dropdown's current items; a click is passed on to the toolbar item
		private static ToolStripMenuItem MirrorDropDown(ToolStripDropDownItem source, string title)
		{
			ToolStripMenuItem menu = new ToolStripMenuItem(title, source.Image);
			foreach(ToolStripItem i in source.DropDownItems)
			{
				if(!i.Available) continue;
				if(i is ToolStripSeparator) menu.DropDownItems.Add(new ToolStripSeparator());
				else menu.DropDownItems.Add(MirrorItem(i));
			}
			return menu;
		}

		// A hosted check box flips only on a real click, so flip it here, then run its handler
		private static ToolStripMenuItem MirrorCheckBox(CodeImp.DoomBuilder.Controls.ToolStripCheckBox source, EventHandler handler)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(source.Text) { Checked = source.Checked };
			item.Click += delegate(object sender, EventArgs e) { source.Checked = !source.Checked; handler(source, e); };
			return item;
		}

		// The toolbar buttons sit in the custom section, which is never hidden, so PerformClick runs
		private static ToolStripMenuItem MirrorItem(ToolStripItem source)
		{
			bool ischecked = (source is ToolStripButton && ((ToolStripButton)source).Checked) || (source is ToolStripMenuItem && ((ToolStripMenuItem)source).Checked);
			ToolStripMenuItem item = new ToolStripMenuItem(source.Text, source.Image) { Checked = ischecked };
			item.Click += delegate { source.PerformClick(); };
			return item;
		}

		// This shows a tooltip
		public void ShowTooltip(string text, Point p)
		{
			Point sp = General.Interface.Display.PointToScreen(p);
			Point fp = (General.Interface as Form).Location;
			Point tp = new Point(sp.X - fp.X, sp.Y - fp.Y);

			if(oldttposition != tp)
			{
				tooltip.Show(text, General.Interface, tp);
				oldttposition = tp;
			}
		}

		// This hides the tooltip
		public void HideTooltip()
		{
			tooltip.Hide(General.Interface);
		}

		#endregion

		#region ================== Events

		// Selecting a type of stats to view
		private void stats_Click(object sender, EventArgs e)
		{
			foreach(ToolStripMenuItem i in statsbutton.DropDownItems)
				i.Checked = false;
			
			ToolStripMenuItem item = (ToolStripMenuItem)sender;
			viewstats = (ViewStats)int.Parse(item.Tag.ToString(), CultureInfo.InvariantCulture);
			item.Checked = true;
			statsbutton.Image = item.Image;

			General.Interface.RedrawDisplay();
		}

		//mxd
		private void cbheatmap_Click(object sender, EventArgs e)
		{
			General.Interface.RedrawDisplay();
		}

		//mxd
		private void cbopendoors_Click(object sender, EventArgs e)
		{
			if(OnVisplaneSettingsChanged != null) OnVisplaneSettingsChanged(this, EventArgs.Empty);
		}

		// Select the height above the floor the Visplane Explorer renderer draws from.
		private void viewheight_Click(object sender, EventArgs e)
		{
			foreach (ToolStripMenuItem i in heightbutton.DropDownItems)
				i.Checked = false;

			ToolStripMenuItem item = (ToolStripMenuItem)sender;
			viewheight = int.Parse(item.Tag.ToString(), CultureInfo.InvariantCulture);
			item.Checked = true;

			RedrawViewHeightButtonText();

			General.Interface.RedrawDisplay();

			if(OnVisplaneSettingsChanged != null) OnVisplaneSettingsChanged(this, EventArgs.Empty);
		}

		// Prompt user to enter a custom height, saving to the menu dropdown.
		private void heightcustomadd_Click(object sender, EventArgs e)
		{
			customheightdialog.CustomHeight = viewheightcustom;

			if (customheightdialog.ShowDialog() != DialogResult.OK)
				return;

			int oldviewheight = viewheight;
			viewheightcustom = customheightdialog.CustomHeight;

			viewheight = viewheightcustom > 0 ? viewheightcustom : viewheightdefault;

			if (General.Map.Config.VisplaneViewHeights.ContainsKey(viewheightcustom.ToString()))
				viewheightcustom = 0;

			RedrawViewHeightMenuItems();

			if (oldviewheight != viewheight)
			{
				General.Interface.RedrawDisplay();

				if (OnVisplaneSettingsChanged != null) OnVisplaneSettingsChanged(this, EventArgs.Empty);
			}
		}

		private void RedrawViewHeightButtonText()
		{
			heightbutton.Text = "View Height (" + viewheight.ToString() + ")";
		}

		private void RedrawViewHeightMenuItems()
		{
			RedrawViewHeightButtonText();

			heightcustomitem.Tag = viewheightcustom.ToString();
			heightcustomitem.Visible = viewheightcustom > 0;
			heightcustomitem.Text = viewheightcustom.ToString() + " - Custom";

			foreach (ToolStripMenuItem heightitem in heightbutton.DropDownItems)
				heightitem.Checked = viewheight == int.Parse((string)heightitem.Tag, CultureInfo.InvariantCulture);
		}

		#endregion
	}
}
