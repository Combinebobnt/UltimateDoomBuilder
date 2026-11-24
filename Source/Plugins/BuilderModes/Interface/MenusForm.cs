
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
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.VisualModes;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.BuilderModes
{
	public partial class MenusForm : Form
	{
		#region ================== Variables

		// Menus list
		private readonly ToolStripItem[] menus;
		private readonly ToolStripMenuItem visualmenu;

		// mxd. More menus
		private readonly ToolStripItem[] exportmenuitems;

		// mxd. Even more menus!
		private readonly ToolStripItem[] propsmenuitems;

		// Buttons list
		private readonly ToolStripItem[] buttons;

		//mxd
		public struct BrightnessGradientModes
		{
			public const string Sectors = "Sector Brightness";
			public const string Light = "Light Color";
			public const string Fade = "Fade Color";
			public const string LightAndFade = "Light and Fade Colors";
			public const string Floors = "Floor Brightness";
			public const string Ceilings = "Ceiling Brightness";
		}

		//mxd
		internal struct GradientInterpolationModes
		{
			public const string Linear = "Linear";
			public const string EaseInOutSine = "EaseInOutSine";
			public const string EaseInSine = "EaseInSine";
			public const string EaseOutSine = "EaseOutSine";
		}

		#endregion

		#region ================== Properties

		public ToolStripButton ViewSelectionNumbers { get { return buttonselectionnumbers; } }
		public ToolStripButton ViewSelectionEffects { get { return buttonselectioneffects; } }
		public ToolStripSeparator SeparatorSectors1 { get { return separatorsectors1; } }
		public ToolStripSeparator SeparatorSectors2 { get { return separatorsectors2; } } //mxd
		public ToolStripSeparator SeparatorSectors3 { get { return separatorsectors3; } } //mxd
		public ToolStripActionButton MakeGradientBrightness { get { return buttonbrightnessgradient; } }
		public ToolStripActionButton MakeGradientFloors { get { return buttonfloorgradient; } }
		public ToolStripActionButton MakeGradientCeilings { get { return buttonceilinggradient; } }
		public ToolStripActionButton FlipSelectionV { get { return buttonflipselectionv; } }
		public ToolStripActionButton FlipSelectionH { get { return buttonflipselectionh; } }
		public ToolStripActionButton RotateSelectionCCW { get { return buttonrotateselection90ccw; } }
		public ToolStripActionButton RotateSelectionCW { get { return buttonrotateselection90cw; } }
		public ToolStripActionButton CurveLinedefs { get { return buttoncurvelinedefs; } }
		public ToolStripActionButton CopyProperties { get { return buttoncopyproperties; } }
		public ToolStripActionButton PasteProperties { get { return buttonpasteproperties; } }
		public ToolStripActionButton PastePropertiesOptions { get { return buttonpastepropertiesoptions; } } //mxd
		public ToolStripSeparator SeparatorCopyPaste { get { return seperatorcopypaste; } }
		public ToolStripComboBox GradientModeMenu { get { return gradientModeMenu; } } //mxd
		public ToolStripComboBox GradientInterpolationMenu { get { return gradientInterpolationMenu; } } //mxd
		public ToolStripButton MarqueSelectTouching { get { return buttonMarqueSelectTouching; } } //mxd
		public ToolStripActionButton AlignThingsToWall { get { return buttonAlignThingsToWall; } } //mxd
		public ToolStripButton TextureOffsetLock { get { return buttonTextureOffsetLock; } } //mxd
		public ToolStripButton TextureOffset3DFloorLock { get { return buttonTextureOffset3DFloorLock; } } 
		public ToolStripActionButton SyncronizeThingEditButton { get { return syncthingteditbutton; } } //mxd
		public ToolStripMenuItem SyncronizeThingEditSectorsItem { get { return syncthingeditsectorsitem; } } //mxd
		public ToolStripMenuItem SyncronizeThingEditLinedefsItem { get { return syncthingeditlinedefsitem; } } //mxd
		public ToolStripActionButton MakeDoor { get { return buttonMakeDoor; } } //mxd

		//mxd. Thing mode radii buttons
		public ToolStripMenuItem ItemLightRadii { get { return itemlightradii; } }
		public ToolStripMenuItem ItemSoundRadii { get { return itemsoundradii; } }
		public ToolStripButton ButtonLightRadii { get { return buttonlightradii; } }
		public ToolStripButton ButtonSoundRadii { get { return buttonsoundradii; } }
		public ToolStripMenuItem ItemSelectionNumbers { get { return itemselectionnumbers; } }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public MenusForm()
		{
			// Initialize
			InitializeComponent();

			// Apply settings
			buttonselectionnumbers.Checked = BuilderPlug.Me.ViewSelectionNumbers;
			buttonselectioneffects.Checked = BuilderPlug.Me.ViewSelectionEffects; //mxd

			//mxd
			gradientModeMenu.Items.AddRange(new[] { BrightnessGradientModes.Sectors, BrightnessGradientModes.Light, BrightnessGradientModes.Fade, BrightnessGradientModes.LightAndFade, BrightnessGradientModes.Ceilings, BrightnessGradientModes.Floors });
			gradientModeMenu.SelectedIndex = 0;
			gradientInterpolationMenu.Items.AddRange(new[] { GradientInterpolationModes.Linear, GradientInterpolationModes.EaseInOutSine, GradientInterpolationModes.EaseInSine, GradientInterpolationModes.EaseOutSine });
			gradientInterpolationMenu.SelectedIndex = 0;
			
			visualmenu = CreateVisualMenu();
			menustrip.Items.Add(visualmenu);

			// List all menus
			menus = new ToolStripItem[menustrip.Items.Count];
			for(int i = 0; i < menustrip.Items.Count; i++) menus[i] = menustrip.Items[i];

			//mxd. Export menu
			exportmenuitems = new ToolStripItem[exportStripMenuItem.DropDownItems.Count];
			for(int i = 0; i < exportStripMenuItem.DropDownItems.Count; i++)
				exportmenuitems[i] = exportStripMenuItem.DropDownItems[i];

			//mxd. Copy-paste propserties items
			propsmenuitems = new ToolStripItem[] { separatorcopyprops, itemcopyprops, itempasteprops, itempastetextures, itempastepropsoptions };

			// List all buttons
			buttons = new ToolStripItem[globalstrip.Items.Count];
			for(int i = 0; i < globalstrip.Items.Count; i++) buttons[i] = globalstrip.Items[i];


#if MONO_WINFORMS
			// Mono fix
			menustrip.Items.Clear();
			exportStripMenuItem.DropDownItems.Clear();
			globalstrip.Items.Clear();
			manualstrip.Items.Clear();
			editmenuitem.DropDownItems.Clear();
			viewmenuitem.DropDownItems.Clear();
#endif

			// Menu-bar twins follow their toolbar buttons on and off the toolbar
			PairWithMenu(buttonselectionnumbers, itemselectionnumbers, MenuSection.ViewHelpers);
			PairWithMenu(buttonselectioneffects, itemselectioneffects, MenuSection.ViewHelpers);
			PairWithMenu(buttonMarqueSelectTouching, itemmarqueselecttouching, MenuSection.EditGeometry);
			PairWithMenu(buttonTextureOffsetLock, itemtextureoffsetlock, MenuSection.EditGeometry);
			PairWithMenu(buttonTextureOffset3DFloorLock, itemtextureoffset3dfloorlock, MenuSection.EditGeometry);
			PairWithMenu(buttonflipselectionh, itemflipselectionh, MenuSection.EditGeometry);
			PairWithMenu(buttonflipselectionv, itemflipselectionv, MenuSection.EditGeometry);
			PairWithMenu(buttonrotateselection90ccw, itemrotateselection90ccw, MenuSection.EditGeometry);
			PairWithMenu(buttonrotateselection90cw, itemrotateselection90cw, MenuSection.EditGeometry);

			AddActionMenuItems();
		}

		#endregion

		#region ================== Methods

		// This registers with the core
		public void Register()
		{
			// Add the menus to the core
			foreach(ToolStripItem i in menus)
				General.Interface.AddMenu(i);
			
			// Add the buttons to the core
			foreach(ToolStripItem b in buttons)
				General.Interface.AddButton(b);

			//mxd. Export menu
			foreach(ToolStripItem i in exportmenuitems)
				General.Interface.AddMenu(i, MenuSection.FileExport);

			//mxd. Copy-paste propserties items
			foreach(ToolStripItem i in propsmenuitems)
				General.Interface.AddMenu(i, MenuSection.EditCopyPaste);
		}

		// This unregisters from the core
		public void Unregister()
		{
			// Remove the menus from the core
			foreach(ToolStripItem i in menus)
				General.Interface.RemoveMenu(i);

			// Remove the buttons from the core
			General.Interface.BeginToolbarUpdate(); //mxd
			foreach(ToolStripItem b in buttons)
				General.Interface.RemoveButton(b);
			General.Interface.EndToolbarUpdate(); //mxd

			//mxd. Export menu
			foreach(ToolStripItem i in exportmenuitems)
				General.Interface.RemoveMenu(i);

			//mxd. Copy-paste propserties items
			foreach(ToolStripItem i in propsmenuitems)
				General.Interface.RemoveMenu(i);
		}

		// The item is on the menu bar while the button is on a toolbar, and shows a toggle button's state
		private static void PairWithMenu(ToolStripItem button, ToolStripMenuItem item, MenuSection section)
		{
			button.OwnerChanged += delegate
			{
				General.Interface.RemoveMenu(item);
				if(button.Owner != null) General.Interface.AddMenu(item, section);
			};

			ToolStripButton toggle = button as ToolStripButton;
			if(toggle != null && toggle.CheckOnClick)
			{
				item.Checked = toggle.Checked;
				toggle.CheckedChanged += delegate { item.Checked = toggle.Checked; };
				toggle.TextChanged += delegate { item.Text = toggle.Text; };
			}
		}

		// Entries for classic-mode actions that otherwise only had a key
		private void AddActionMenuItems()
		{
			ToolStripMenuItem heights = new ToolStripMenuItem("Heights and &Brightness");
			heights.DropDownItems.AddRange(new ToolStripItem[] {
				ActionItem("Raise Floor by 8", "raisefloor8"),
				ActionItem("Lower Floor by 8", "lowerfloor8"),
				ActionItem("Raise Ceiling by 8", "raiseceiling8"),
				ActionItem("Lower Ceiling by 8", "lowerceiling8"),
				new ToolStripSeparator(),
				ActionItem("Increase Brightness by 8", "raisebrightness8"),
				ActionItem("Decrease Brightness by 8", "lowerbrightness8") });
			ToolStripItem[] sectoritems = { heights, ActionItem("Rotate Flats Clockwise", "rotateclockwise"),
				ActionItem("Rotate Flats Counterclockwise", "rotatecounterclockwise") };
			sectorsmenu.DropDownItems.Add(new ToolStripSeparator());
			sectorsmenu.DropDownItems.AddRange(sectoritems);
			EnableInMode(sectorsmenu, typeof(SectorsMode), sectoritems);

			int index = thingsmenu.DropDownItems.IndexOf(pointAtCursorItem) + 1;
			ToolStripItem[] thingitems = { ActionItem("Rotate Clockwise", "rotateclockwise"),
				ActionItem("Rotate Counterclockwise", "rotatecounterclockwise") };
			thingsmenu.DropDownItems.Insert(index, thingitems[0]);
			thingsmenu.DropDownItems.Insert(index + 1, thingitems[1]);
			EnableInMode(thingsmenu, typeof(ThingsMode), thingitems);

			// Edit Selection mode: on the Edit menu while its rotate button is on the toolbar
			ToolStripMenuItem pivot = ActionItem("Pivot at 2D Cursor", "toggle2dcursorpivot");
			ToolStripMenuItem transform = new ToolStripMenuItem("&Move and Rotate Selection");
			transform.DropDownItems.AddRange(new ToolStripItem[] {
				ActionItem("Move Up by Grid Size", "moveselectionup"),
				ActionItem("Move Down by Grid Size", "moveselectiondown"),
				ActionItem("Move Left by Grid Size", "moveselectionleft"),
				ActionItem("Move Right by Grid Size", "moveselectionright"),
				new ToolStripSeparator(),
				ActionItem("Rotate Clockwise", "rotateclockwise"),
				ActionItem("Rotate Counterclockwise", "rotatecounterclockwise"),
				new ToolStripSeparator(),
				pivot });
			transform.DropDownOpening += delegate
			{
				EditSelectionMode mode = General.Editing.Mode as EditSelectionMode;
				pivot.Checked = (mode != null) && mode.PivotAt2DCursor;
			};
			PairWithMenu(buttonrotateselection90cw, transform, MenuSection.EditGeometry);
		}

		// Visual mode's actions; reachable once Free Mouse Cursor lets go of the mouse
		private static ToolStripMenuItem CreateVisualMenu()
		{
			ToolStripMenuItem freecursor = ActionItem("&Free Mouse Cursor", "builder_togglevisualcursor");

			ToolStripMenuItem offsets = Submenu("Texture &Offsets",
				ActionItem("Move Left by 1", "movetextureleft"),
				ActionItem("Move Right by 1", "movetextureright"),
				ActionItem("Move Up by 1", "movetextureup"),
				ActionItem("Move Down by 1", "movetexturedown"),
				new ToolStripSeparator(),
				ActionItem("Move Left by 8", "movetextureleft8"),
				ActionItem("Move Right by 8", "movetextureright8"),
				ActionItem("Move Up by 8", "movetextureup8"),
				ActionItem("Move Down by 8", "movetexturedown8"),
				new ToolStripSeparator(),
				ActionItem("Move Left by Grid Size", "movetextureleftgs"),
				ActionItem("Move Right by Grid Size", "movetexturerightgs"),
				ActionItem("Move Up by Grid Size", "movetextureupgs"),
				ActionItem("Move Down by Grid Size", "movetexturedowngs"),
				new ToolStripSeparator(),
				ActionItem("Reset Offsets", "resettexture"),
				ActionItem("Reset Local Offsets (UDMF)", "resettextureudmf"),
				new ToolStripSeparator(),
				ActionItem("Copy Offsets", "texturecopyoffsets"),
				ActionItem("Paste Offsets", "texturepasteoffsets"));

			ToolStripMenuItem scale = Submenu("Texture &Scale",
				ActionItem("Increase Scale", "scaleup"),
				ActionItem("Decrease Scale", "scaledown"),
				new ToolStripSeparator(),
				ActionItem("Increase Horizontal Scale", "scaleupx"),
				ActionItem("Decrease Horizontal Scale", "scaledownx"),
				ActionItem("Increase Vertical Scale", "scaleupy"),
				ActionItem("Decrease Vertical Scale", "scaledowny"));

			ToolStripMenuItem align = Submenu("&Align",
				ActionItem("Auto-align X and Y", "visualautoalign"),
				ActionItem("Auto-align X", "visualautoalignx"),
				ActionItem("Auto-align Y", "visualautoaligny"),
				new ToolStripSeparator(),
				ActionItem("Auto-align to Selection (X and Y)", "visualautoaligntoselection"),
				ActionItem("Auto-align to Selection (X)", "visualautoaligntoselectionx"),
				ActionItem("Auto-align to Selection (Y)", "visualautoaligntoselectiony"),
				new ToolStripSeparator(),
				ActionItem("Fit Textures...", "visualfittextures"));

			ToolStripMenuItem textures = Submenu("&Textures",
				ActionItem("Select Texture...", "textureselect"),
				ActionItem("Texture Palette...", "texturepalette"),
				new ToolStripSeparator(),
				ActionItem("Copy Texture", "texturecopy"),
				ActionItem("Paste Texture", "texturepaste"),
				ActionItem("Paste Texture Flood-Fill", "floodfilltextures"),
				new ToolStripSeparator(),
				ActionItem("Toggle Upper Unpegged", "toggleupperunpegged"),
				ActionItem("Toggle Lower Unpegged", "togglelowerunpegged"));

			ToolStripMenuItem heights = Submenu("&Heights and Brightness",
				ActionItem("Raise by 1", "raisesector1"),
				ActionItem("Lower by 1", "lowersector1"),
				ActionItem("Raise by 8", "raisesector8"),
				ActionItem("Lower by 8", "lowersector8"),
				ActionItem("Raise by 128", "raisesector128"),
				ActionItem("Lower by 128", "lowersector128"),
				ActionItem("Raise by Grid Size", "raisemapelementbygridsize"),
				ActionItem("Lower by Grid Size", "lowermapelementbygridsize"),
				ActionItem("Raise to Adjacent", "raisesectortonearest"),
				ActionItem("Lower to Adjacent", "lowersectortonearest"),
				new ToolStripSeparator(),
				ActionItem("Increase Brightness by 8", "raisebrightness8"),
				ActionItem("Decrease Brightness by 8", "lowerbrightness8"),
				ActionItem("Match Brightness", "matchbrightness"));

			ToolStripMenuItem properties = Submenu("&Properties",
				ActionItem("Copy Properties", "copyproperties"),
				ActionItem("Paste Properties", "pasteproperties"),
				ActionItem("Paste Properties Special...", "pastepropertieswithoptions"));

			ToolStripMenuItem sidedefpicking = ActionItem("Sidedef Slope Picking", "togglevisualslopepicking");
			ToolStripMenuItem vertexpicking = ActionItem("Vertex Slope Picking", "togglevisualvertexslopepicking");
			ToolStripMenuItem adjacentvertices = ActionItem("Select Adjacent Vertex Slope Handles", "togglevisualvertexslopeadjacentselection");
			ToolStripMenuItem slopes = Submenu("S&lopes",
				ActionItem("Toggle Slope", "toggleslope"),
				ActionItem("Reset Slope", "resetslope"),
				ActionItem("Slope Between Handles", "slopebetweenhandles"),
				ActionItem("Arch Between Handles...", "archbetweenhandles"),
				new ToolStripSeparator(),
				sidedefpicking,
				vertexpicking,
				adjacentvertices);

			ToolStripMenuItem things = Submenu("T&hings",
				ActionItem("Move Left", "builder_movethingleft"),
				ActionItem("Move Right", "builder_movethingright"),
				ActionItem("Move Forward", "builder_movethingfwd"),
				ActionItem("Move Backward", "builder_movethingback"),
				ActionItem("Move to Cursor Location", "builder_placethingatcursor"),
				new ToolStripSeparator(),
				ActionItem("Rotate Clockwise", "rotateclockwise"),
				ActionItem("Rotate Counterclockwise", "rotatecounterclockwise"),
				ActionItem("Pitch Clockwise", "pitchclockwise"),
				ActionItem("Pitch Counterclockwise", "pitchcounterclockwise"),
				ActionItem("Roll Clockwise", "rollclockwise"),
				ActionItem("Roll Counterclockwise", "rollcounterclockwise"),
				new ToolStripSeparator(),
				ActionItem("Look Through Selection", "lookthroughthing"),
				ActionItem("Apply Camera Rotation to Things", "applycamerarotationtothings"));

			ToolStripMenuItem groups = Submenu("&Groups",
				ActionItem("Select Group", "selectgroup"),
				ActionItem("Create Group", "creategroup"),
				ActionItem("Update or Delete Last Selected Group", "updateordeletelastselectedgroup"));

			ToolStripMenuItem gravity = ActionItem("&Gravity", "togglegravity");
			ToolStripMenuItem alphahighlight = ActionItem("Alpha-based Texture Highlighting", "alphabasedtexturehighlighting");

			ToolStripMenuItem menu = Submenu("&Visual", freecursor, new ToolStripSeparator(),
				offsets, scale, align, textures, heights, properties, slopes, things, groups,
				new ToolStripSeparator(),
				ActionItem("Cycle Thing Display", "showvisualthings"),
				gravity,
				alphahighlight);

			menu.DropDownOpening += delegate
			{
				freecursor.Checked = General.Interface.VisualCursorFree;
				gravity.Checked = BuilderPlug.Me.UseGravity;
				alphahighlight.Checked = BuilderPlug.Me.AlphaBasedTextureHighlighting;
			};
			slopes.DropDownOpening += delegate
			{
				BaseVisualMode mode = General.Editing.Mode as BaseVisualMode;
				sidedefpicking.Checked = (mode != null) && (mode.CurrentPickingMode == PickingMode.SidedefSlopeHandles);
				vertexpicking.Checked = (mode != null) && (mode.CurrentPickingMode == PickingMode.VertexSlopeHandles);
				adjacentvertices.Checked = BuilderPlug.Me.SelectAdjacentVisualVertexSlopeHandles;
			};
			return menu;
		}

		private static ToolStripMenuItem Submenu(string text, params ToolStripItem[] items)
		{
			ToolStripMenuItem menu = new ToolStripMenuItem(text);
			menu.DropDownItems.AddRange(items);
			return menu;
		}

		// A menu item that runs an action by its tag; AddMenu prefixes the tag with the plugin name
		internal static ToolStripMenuItem ActionItem(string text, string action)
		{
			ToolStripMenuItem item = new ToolStripMenuItem(text) { Tag = action };
			item.Click += General.Interface.InvokeTaggedAction;
			return item;
		}

		// Finish and vertex-removal entries for a drawing mode's menu
		internal static void AddDrawingItems(ToolStripMenuItem menu, bool rotate)
		{
			if(menu.DropDownItems.Count > 0) menu.DropDownItems.Add(new ToolStripSeparator());
			menu.DropDownItems.Add(ActionItem("&Finish Drawing", "finishdraw"));
			menu.DropDownItems.Add(ActionItem("Remove &Last Vertex", "removepoint"));
			menu.DropDownItems.Add(ActionItem("Remove F&irst Vertex", "removefirstpoint"));
			if(!rotate) return;
			menu.DropDownItems.Add(new ToolStripSeparator());
			menu.DropDownItems.Add(ActionItem("Rotate Clockwise", "rotateclockwise"));
			menu.DropDownItems.Add(ActionItem("Rotate Counterclockwise", "rotatecounterclockwise"));
		}

		// The menu stays up in volatile modes started from its mode, which bind these actions differently or not at all
		private static void EnableInMode(ToolStripMenuItem menu, Type modetype, ToolStripItem[] items)
		{
			menu.DropDownOpening += delegate
			{
				bool enabled = (General.Editing.Mode != null) && (General.Editing.Mode.GetType() == modetype);
				foreach(ToolStripItem item in items) item.Enabled = enabled;
			};
		}

		// A top-level menu mirroring an options panel's buttons; the panel adds and removes it with its buttons
		internal static ToolStripMenuItem CreatePanelMenu(string title, params ToolStripItem[] buttons)
		{
			ToolStripMenuItem menu = new ToolStripMenuItem(title);
			foreach(ToolStripItem b in buttons)
			{
				if(b is ToolStripSeparator)
				{
					menu.DropDownItems.Add(new ToolStripSeparator());
					continue;
				}

				ToolStripButton button = (ToolStripButton)b;
				ToolStripMenuItem item = new ToolStripMenuItem(button.Text, button.Image) { Enabled = button.Enabled };
				button.EnabledChanged += delegate { item.Enabled = button.Enabled; };
				if(button.CheckOnClick)
				{
					// The panels act on CheckedChanged, so flipping the state is the whole click
					item.Checked = button.Checked;
					button.CheckedChanged += delegate { item.Checked = button.Checked; };
					item.Click += delegate { button.Checked = !button.Checked; };
				}
				else
				{
					// Panel buttons sit in the toolbar's custom section, which is never hidden, so PerformClick runs
					item.Click += delegate { button.PerformClick(); };
				}
				menu.DropDownItems.Add(item);
			}
			return menu;
		}

		// Some modes register their panel twice (base OnEngage and their own), so adding must not duplicate the menu
		internal static void ShowPanelMenu(ToolStripMenuItem menu)
		{
			General.Interface.RemoveMenu(menu);
			General.Interface.AddMenu(menu);
		}

		// A menu click doesn't toggle the button, so flip it before the shared handler reads it
		private static void FlipFromMenu(object sender, ToolStripButton button)
		{
			if(sender != button) button.Checked = !button.Checked;
		}

		// This hides all menus
		public void HideAllMenus()
		{
			foreach(ToolStripItem m in menus) m.Visible = false;
		}
		
		// This hides all except one menu
		public void HideAllMenusExcept(ToolStripMenuItem showthis)
		{
			HideAllMenus();
			showthis.Visible = true;
		}
		
		// This shows the menu for the current editing mode
		public void ShowEditingModeMenu(EditMode mode)
		{
			Type sourcemode = typeof(object);
			if(mode != null)
			{
				sourcemode = mode.GetType();

				// When in a volatile mode, check against the last stable mode
				if(mode.Attributes.Volatile) sourcemode = General.Editing.PreviousStableMode;
			}
			
			// Final decision
			bool showcopyprops = true; //mxd
			if(sourcemode == typeof(LinedefsMode)) HideAllMenusExcept(linedefsmenu);
			else if(sourcemode == typeof(SectorsMode)) HideAllMenusExcept(sectorsmenu);
			else if(sourcemode == typeof(ThingsMode)) HideAllMenusExcept(thingsmenu); //mxd
			else if(sourcemode == typeof(VerticesMode)) HideAllMenusExcept(vertsmenu); //mxd
			else if(typeof(BaseVisualMode).IsAssignableFrom(sourcemode))
			{
				HideAllMenusExcept(visualmenu);
				showcopyprops = false;
			}
			else
			{
				HideAllMenus();
				showcopyprops = false; //mxd
			}

			//mxd. Copy-paste properties items
			foreach(ToolStripItem i in propsmenuitems)
				i.Visible = showcopyprops;
			itempastetextures.Visible = (sourcemode == typeof(LinedefsMode) || sourcemode == typeof(SectorsMode));
		}

		// This invokes an action from control event
		private void InvokeTaggedAction(object sender, EventArgs e)
		{
			General.Interface.InvokeTaggedAction(sender, e);
		}

		// View selection numbers clicked
		private void buttonselectionnumbers_Click(object sender, EventArgs e)
		{
			FlipFromMenu(sender, buttonselectionnumbers);
			BuilderPlug.Me.ViewSelectionNumbers = buttonselectionnumbers.Checked;

			//mxd. Notify current mode
			BaseClassicMode mode = General.Editing.Mode as BaseClassicMode;
			if(mode != null) mode.OnViewSelectionNumbersChanged(BuilderPlug.Me.ViewSelectionNumbers);

			General.Interface.RedrawDisplay();
			General.Interface.DisplayStatus(StatusType.Info, (buttonselectionnumbers.Checked ?
				"Show selection numbers" :
				"Don't show selection numbers"));
		}

		//mxd
		private void buttonselectioneffects_Click(object sender, EventArgs e) 
		{
			FlipFromMenu(sender, buttonselectioneffects);
			BuilderPlug.Me.ViewSelectionEffects = buttonselectioneffects.Checked;

			// Notify current mode
			BaseClassicMode mode = General.Editing.Mode as BaseClassicMode;
			if(mode != null) mode.OnViewSelectionEffectsChanged(BuilderPlug.Me.ViewSelectionEffects);

			General.Interface.RedrawDisplay();
			General.Interface.DisplayStatus(StatusType.Info, (buttonselectioneffects.Checked ?
				"Show sector tags and effects" :
				"Don't show sector tags and effects"));
		}

		//mxd
		private void buttonMarqueSelectTouching_Click(object sender, EventArgs e) 
		{
			FlipFromMenu(sender, buttonMarqueSelectTouching);
			BuilderPlug.Me.MarqueSelectTouching = buttonMarqueSelectTouching.Checked;
			General.Interface.DisplayStatus(StatusType.Info, (buttonMarqueSelectTouching.Checked ? 
				"Select map elements touching selection rectangle" :
				"Select map elements inside of selection rectangle"));
		}

		//mxd
		private void buttonTextureOffsetLock_Click(object sender, EventArgs e) 
		{
			FlipFromMenu(sender, buttonTextureOffsetLock);
			BuilderPlug.Me.LockSectorTextureOffsetsWhileDragging = buttonTextureOffsetLock.Checked;
			General.Interface.DisplayStatus(StatusType.Info, (buttonTextureOffsetLock.Checked ? 
				"Lock texture offsets when dragging sectors" : 
				"Don't lock texture offsets when dragging sectors"));
		}

		private void buttonTextureOffset3DFloorLock_Click(object sender, EventArgs e)
		{
			FlipFromMenu(sender, buttonTextureOffset3DFloorLock);
			BuilderPlug.Me.Lock3DFloorSectorTextureOffsetsWhileDragging = buttonTextureOffset3DFloorLock.Checked;
			General.Interface.DisplayStatus(StatusType.Info, (buttonTextureOffset3DFloorLock.Checked ?
				"Lock texture offsets of 3D floors when dragging tagged sectors" :
				"Don't lock texture offsets of 3D floors when dragging tagged sectors"));
		}

		//mxd
		private void linedefsmenu_DropDownOpening(object sender, EventArgs e) 
		{
			aligntexturesitem.Enabled = General.Map.UDMF;
			updatelightfogitem.Enabled = General.Map.UDMF;
			itemgradientbrightnessl.Enabled = General.Map.UDMF;
		}

		//mxd
		private void gradientMode_DropDownClosed(object sender, EventArgs e) 
		{
			General.Interface.FocusDisplay();
		}

		//mxd
		private void buttonlightradii_Click(object sender, EventArgs e)
		{
			BuilderPlug.Me.ShowLightRadii = !BuilderPlug.Me.ShowLightRadii;
			buttonlightradii.Checked = BuilderPlug.Me.ShowLightRadii;
			itemlightradii.Checked = BuilderPlug.Me.ShowLightRadii;

			General.Interface.DisplayStatus(StatusType.Info, "Light radii are " + (BuilderPlug.Me.ShowLightRadii ? "SHOWN" : "HIDDEN"));
			General.Interface.RedrawDisplay();
		}

		//mxd
		private void buttonsoundradii_Click(object sender, EventArgs e)
		{
			BuilderPlug.Me.ShowSoundRadii = !BuilderPlug.Me.ShowSoundRadii;
			buttonsoundradii.Checked = BuilderPlug.Me.ShowSoundRadii;
			itemsoundradii.Checked = BuilderPlug.Me.ShowSoundRadii;

			General.Interface.DisplayStatus(StatusType.Info, "Sound radii are " + (BuilderPlug.Me.ShowSoundRadii ? "SHOWN" : "HIDDEN"));
			General.Interface.RedrawDisplay();
		}

		#endregion
	}
}