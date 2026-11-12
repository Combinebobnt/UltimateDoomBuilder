
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
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using System.Linq; // biwa
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.Rendering;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	internal enum TextureBrowserSortMode { Name, Match, Size, Hue, Brightness, SimilarColor }

	/// <summary>
	/// Holds a title of an item group and its items
	/// </summary>
	internal struct ImageBrowserItemGroup
	{
		public string Title;
		public List<ImageBrowserItem> Items;

		public ImageBrowserItemGroup(string title)
		{
			Title = title;
			Items = new List<ImageBrowserItem>();
		}
	}

	internal partial class ImageBrowserControl : UserControl
	{
		#region ================== Constants
		
		private static readonly HashSet<char> AllowedSpecialChars = new HashSet<char>("!@#$%^&*()-_=+<>,.?/'\"\\;:[]{}`~| ".ToCharArray()); //mxd

		#endregion
		
		#region ================== Delegates / Events

		public delegate void SelectedItemChangedDelegate(ImageBrowserItem item);
		public delegate void SelectedItemDoubleClickDelegate(ImageBrowserItem item);

		public event SelectedItemChangedDelegate SelectedItemChanged;
		public event SelectedItemDoubleClickDelegate SelectedItemDoubleClicked;
		
		#endregion

		#region ================== Variables
		
		// Properties
		private bool preventselection;
        private int imagesize;
		
		// States
		private int keepselected;
		private bool browseflats; //mxd
		private bool uselongtexturenames; //mxd
		private bool blockupdate; //mxd
		
		//mxd. All items
		private List<ImageBrowserItem> items;
		private string usedfirstgroup;
		private string availgroup;

		//mxd
		private int texturetype;
		private Color colorswatch = Color.Empty;
		private TextureNameMatcher matcher;
		private string matcherquery;
		private TextureNameMatchMode matchermode;
		private Queue<ImageData> pendingstats = new Queue<ImageData>();
		private int sourceversion;
		private int pendingversion;
		private bool statsdirty;
		private bool queryfailed;
		private bool debouncefuzzy;
		private bool filling;
		private double laststatsrefill;
		private readonly Stopwatch fillclock = new Stopwatch();
		private long fillworkingstart;
		private bool previewsreported;
		private Dictionary<ImageData, Size> previewdimensions = new Dictionary<ImageData, Size>();
		private TextureFavorites favorites;
		private bool showfavorites;
		private bool favoritesfirst = true;
		
		#endregion

		#region ================== Properties

		public bool PreventSelection { get { return preventselection; } set { preventselection = value; } }
		public bool HideInputBox { get { return splitter.Panel2Collapsed; } set { splitter.Panel2Collapsed = value; } }
		public List<ImageBrowserItem> SelectedItems { get { return list.SelectedItems; } } //mxd
		public ImageBrowserItem SelectedItem { get { return (list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null); } }
		public bool ShowFavorites
		{
			get { return showfavorites; }
			set
			{
				if(favorites != null) favorites.Changed -= FavoritesChanged;
				showfavorites = value;
				favorites = value && General.Map != null ? General.Map.Favorites : null;
				if(favorites != null) favorites.Changed += FavoritesChanged;
				favoritesonly.Visible = value;
				list.IsFavorite = IsFavoriteItem;
				list.Invalidate();
			}
		}

		// Interaction log only: the texture browser's close line
		internal int FilterLength { get { return objectname.Text.Length; } }
		internal bool UsedFirst { get { return usedtexturesfirst.Checked; } }

		// Geometry log only: tells "list never got the width" from "items laid out against a stale width"
		internal Rectangle GeomDebugListBounds { get { return list.Bounds; } }
		internal int GeomDebugListClientWidth { get { return list.ClientRectangle.Width; } }
		internal int GeomDebugListFlowWidth { get { return list.GeomDebugLastFlowWidth; } }
		internal int GeomDebugListFlowCallCount { get { return list.GeomDebugLastFlowCallCount; } }
		internal int GeomDebugListContentRight { get { return list.GeomDebugContentRight; } }
		internal int GeomDebugListItemCount { get { return list.GeomDebugItemCount; } }
		internal int GeomDebugListGroupCount { get { return list.GeomDebugGroupCount; } }
		internal bool GeomDebugListClassicView { get { return list.GeomDebugClassicView; } }
		public string ElementName //mxd
		{
			set
			{
				usedfirstgroup = "Available " + value + " (used first):";
				availgroup = "Available " + value + ":";
                list.ContentType = value;
            }
		}

        public int ImageSize
        {
            get { return imagesize; }
            set { imagesize = value; }
        }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		public ImageBrowserControl()
		{
			// Initialize
			InitializeComponent();
			items = new List<ImageBrowserItem>();
			blockupdate = true;
			matchmode.SelectedIndex = sortmode.SelectedIndex = 0;
			colortolerance.SelectedIndex = 1;
			blockupdate = false;
			BuildColorMenu();

			//mxd
			StepsList sizes = new StepsList { 4, 8, 16, 32, 48, 64, 96, 128, 192, 256, 512, 1024 };
			filterWidth.StepValues = sizes;
			filterHeight.StepValues = sizes;

			//mxd. Looks like SplitterDistance is unaffected by DPI scaling. Let's fix that...
			if(MainForm.DPIScaler.Height != 1.0f)
			{
				splitter.SplitterDistance = splitter.Height - splitter.Panel2.Height - (int)Math.Round(splitter.SplitterWidth * MainForm.DPIScaler.Height);
			}

			//mxd
			list.SelectionChanged += list_SelectionChanged;
			list.ItemClicked += list_ItemClicked;
		}

		// This applies the application settings
		public void ApplySettings(string settingpath, bool browseflats)
		{
			blockupdate = true;

			this.browseflats = browseflats;
            uselongtexturenames = General.Map.Options.UseLongTextureNames;
			texturetype = General.Settings.ReadSetting(settingpath + ".texturetype", 0);
			matchmode.SelectedIndex = Math.Max(0, Math.Min(2, General.Settings.ReadSetting(settingpath + ".searchmode", 0)));
			sortmode.SelectedIndex = Math.Max(0, Math.Min(5, General.Settings.ReadSetting(settingpath + ".sortmode", 0)));
			colortolerance.SelectedIndex = Math.Max(0, Math.Min(2, General.Settings.ReadSetting(settingpath + ".colortolerance", 1)));
			transparency.Checked = General.Settings.ReadSetting(settingpath + ".transparency", false);
			favoritesfirst = General.Settings.ReadSetting(settingpath + ".favoritesfirst", true);
			favoritesonly.Checked = General.Settings.ReadSetting(settingpath + ".favoritesonly", false);
			SetColorSwatch(General.Settings.ReadSetting(settingpath + ".colorenabled", false)
				? Color.FromArgb(General.Settings.ReadSetting(settingpath + ".color", Color.Red.ToArgb())) : Color.Empty);
            ElementName = (texturetype == 2 || (texturetype == 3 && browseflats)) ? "flats" : "textures";
            list.UsedTexturesFirst = usedtexturesfirst.Checked = General.Settings.ReadSetting(settingpath + ".showusedtexturesfirst", false);
            list.ClassicView = classicview.Checked = General.Settings.ReadSetting(settingpath + ".classicview", false);
			list.CenterItem = centeritem.Checked = General.Settings.ReadSetting(settingpath + ".verticallycenteritem", true);
			
			int _imagesize = General.Settings.ReadSetting(settingpath + ".imagesize", 128);
			sizecombo.Text = (_imagesize == 0 ? sizecombo.Items[0].ToString() : _imagesize.ToString());
			list.ImageSize = _imagesize;

			ApplySettings();

			blockupdate = false;
		}

		// This applies the application settings
		public void ApplySettings()
		{
			// Force black background?
			if(General.Settings.BlackBrowsers)
			{
				list.BackColor = Color.Black;
				list.ForeColor = Color.White;
			}

			// Set the size of preview images
			if(General.Map != null)
			{
				//mxd
				if(General.Map.Config.MixTexturesFlats) 
				{
					texturetypecombo.SelectedIndex = texturetype;
				} 
				else 
				{
					labelMixMode.Enabled = false;
					texturetypecombo.Enabled = false;
					texturetype = 0;
				}

				//mxd. Use long texture names?
				longtexturenames.Checked = (uselongtexturenames && General.Map.Config.UseLongTextureNames);
				longtexturenames.Enabled = General.Map.Config.UseLongTextureNames;
			}
			else
			{
				longtexturenames.Enabled = false; //mxd
				uselongtexturenames = false; //mxd
			}

            // If we have override for preview images, set this here.
            if (imagesize > 0) list.ImageSize = imagesize;

			//mxd
			UpdateCasing();
		}

		//mxd. Save settings
		public virtual void OnClose(string settingpath)
		{
			General.Settings.WriteSetting(settingpath + ".showusedtexturesfirst", usedtexturesfirst.Checked);
            General.Settings.WriteSetting(settingpath + ".classicview", classicview.Checked);
			General.Settings.WriteSetting(settingpath + ".verticallycenteritem", list.CenterItem);
			General.Settings.WriteSetting(settingpath + ".imagesize", list.ImageSize);
			General.Settings.WriteSetting(settingpath + ".texturetype", texturetype);
			General.Settings.WriteSetting(settingpath + ".searchmode", matchmode.SelectedIndex);
			General.Settings.WriteSetting(settingpath + ".sortmode", sortmode.SelectedIndex);
			General.Settings.WriteSetting(settingpath + ".colortolerance", colortolerance.SelectedIndex);
			General.Settings.WriteSetting(settingpath + ".transparency", transparency.Checked);
			General.Settings.WriteSetting(settingpath + ".colorenabled", !colorswatch.IsEmpty);
			General.Settings.WriteSetting(settingpath + ".color", colorswatch.ToArgb());
			if(showfavorites)
			{
				General.Settings.WriteSetting(settingpath + ".favoritesfirst", favoritesfirst);
				General.Settings.WriteSetting(settingpath + ".favoritesonly", favoritesonly.Checked);
			}
			
			if (General.Map.Config.UseLongTextureNames) General.Map.Options.UseLongTextureNames = uselongtexturenames;

			CleanUp();
		}

		// This cleans everything up
		public virtual void CleanUp()
		{
			// Stop refresh timer
			refreshtimer.Enabled = false;
			statstimer.Stop();
			filtertimer.Stop();
			pendingstats.Clear();
			statsdirty = false;
			sourceversion++;
			if(favorites != null) favorites.Changed -= FavoritesChanged;
		}

		#endregion

		#region ================== Rendering

		// Refresher
		private void refreshtimer_Tick(object sender, EventArgs e)
		{
			if(!Visible || filling || IsDisposed) { refreshtimer.Stop(); return; }
			bool allpreviewsloaded = true;
			bool redrawneeded = false; //mxd
			bool dimensionschanged = false;
			
			// Go for all items
			foreach(ImageBrowserItem i in list.Items)
			{
				// Check if there are still previews that are not loaded
				allpreviewsloaded &= i.IsPreviewLoaded;

				//mxd. Item needs to be redrawn?
				redrawneeded |= i.CheckRedrawNeeded();
				if(i.ItemType == ImageBrowserItemType.IMAGE)
				{
					Size dimensions = new Size(i.Icon.Width, i.Icon.Height);
					Size previous;
					if(previewdimensions.TryGetValue(i.Icon, out previous) && previous != dimensions) dimensionschanged = true;
					previewdimensions[i.Icon] = dimensions;
				}
			}

			// If all previews were loaded, stop this timer
			if(allpreviewsloaded)
			{
				refreshtimer.Stop();
				if(!previewsreported && fillclock.IsRunning)
				{
					previewsreported = true;
					General.WriteLogLine("Image browser previews: items=" + items.Count + " ms=" + fillclock.ElapsedMilliseconds
						+ " working_start=" + fillworkingstart + " working_end=" + WorkingSet());
				}
			}
			if(dimensionschanged)
			{
				if(!queryfailed && (CurrentSort == TextureBrowserSortMode.Size || filterWidth.GetResult(-1) > 0 || filterHeight.GetResult(-1) > 0))
					RefillList(false, true, "dimensions");
				else
				{
					var anchor = list.CaptureScrollAnchor();
					list.UpdateRectangles();
					list.RestoreScrollAnchor(anchor);
				}
			}

			// Redraw the list if needed
			if(redrawneeded) list.Invalidate();
		}

		private static long WorkingSet()
		{
			using(Process process = Process.GetCurrentProcess()) return process.WorkingSet64;
		}

		private TextureNameMatchMode CurrentMatch { get { return (TextureNameMatchMode)Math.Max(0, matchmode.SelectedIndex); } }
		private TextureBrowserSortMode CurrentSort { get { return (TextureBrowserSortMode)Math.Max(0, sortmode.SelectedIndex); } }
		private bool NeedsColor { get { return !colorswatch.IsEmpty || CurrentSort == TextureBrowserSortMode.Hue
			|| CurrentSort == TextureBrowserSortMode.Brightness || (CurrentSort == TextureBrowserSortMode.SimilarColor && !colorswatch.IsEmpty); } }

		internal static bool IsPending(ImageData image, bool color, bool alpha, bool dimensions)
		{
			if(image.IsDisposed) return false;
			if(color && image.ColorStats == null) return true;
			if(alpha && !image.HasTransparencyInfo && !image.LoadFailed) return true;
			return dimensions
				&& (image.Width <= 0 || image.Height <= 0) && image.PreviewState != ImageLoadState.Ready;
		}

		private void statstimer_Tick(object sender, EventArgs e)
		{
			if(!Visible || filling || queryfailed || pendingversion != sourceversion || IsDisposed)
			{
				statstimer.Stop();
				return;
			}
			int version = sourceversion;
			int count = Math.Min(16, pendingstats.Count);
			int w = filterWidth.GetResult(-1), h = filterHeight.GetResult(-1);
			bool color = NeedsColor, alpha = transparency.Checked, dimensions = w > 0 || h > 0 || CurrentSort == TextureBrowserSortMode.Size;
			Stopwatch budget = Stopwatch.StartNew();
			for(int i = 0; i < count && budget.Elapsed.TotalMilliseconds < 4; i++)
			{
				ImageData image = pendingstats.Dequeue();
				if(!image.IsDisposed)
				{
					if(color) image.UpdateColorStats();
					else if(image.PreviewState == ImageLoadState.None) { bool ready = image.IsPreviewLoaded; }
				}
				if(version != sourceversion) return;
				if(IsPending(image, color, alpha, dimensions)) pendingstats.Enqueue(image);
				else statsdirty = true;
			}
			if(statsdirty && InteractionLog.Now - laststatsrefill >= 500)
			{
				statsdirty = false;
				laststatsrefill = InteractionLog.Now;
				RefillList(false, true, "stats");
			}
			if(pendingstats.Count == 0 && !statsdirty) statstimer.Stop();
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);
			if(refreshtimer == null || statstimer == null) return;
			if(!Visible)
			{
				refreshtimer.Stop();
				statstimer.Stop();
				filtertimer.Stop();
			}
			else if(items != null && items.Count > 0 && !filling)
			{
				refreshtimer.Start();
				if(!queryfailed && (pendingstats.Count > 0 || statsdirty)) statstimer.Start();
			}
		}

		#endregion

		#region ================== Events

		// Name typed
		private void objectname_TextChanged(object sender, EventArgs e)
		{
			if(blockupdate || filling) return;
			filtertimer.Stop();
			ResetPendingWork();
			PrepareMatcher();
			if(matcher.Error != null)
			{
				SetQueryError(matcher.Error);
				return;
			}
			SetQueryError(null);
			if(CurrentMatch == TextureNameMatchMode.Fuzzy && debouncefuzzy && Visible) filtertimer.Start();
			else RefillList(false, false, "text");
		}

		private void filtertimer_Tick(object sender, EventArgs e)
		{
			filtertimer.Stop();
			RefillList(false, false, "text");
		}

		private void UpdateCasing()
		{
			objectname.CharacterCasing = CurrentMatch == TextureNameMatchMode.Regex || longtexturenames.Checked
				? CharacterCasing.Normal : CharacterCasing.Upper;
		}

		private void PrepareMatcher()
		{
			if(matcher != null && matcherquery == objectname.Text && matchermode == CurrentMatch) return;
			matcherquery = objectname.Text;
			matchermode = CurrentMatch;
			matcher = new TextureNameMatcher(matchermode, matcherquery);
			SetQueryError(matcher.Error);
		}

		private void SetQueryError(string error)
		{
			queryfailed = error != null;
			objectname.BackColor = queryfailed ? Color.MistyRose : SystemColors.Window;
			filtertooltip.SetToolTip(objectname, error ?? string.Empty);
		}

		private void ResetPendingWork()
		{
			sourceversion++;
			pendingversion = sourceversion;
			pendingstats.Clear();
			statsdirty = false;
			statstimer.Stop();
		}

		private void matchmode_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(blockupdate) return;
			blockupdate = true;
			UpdateCasing();
			if(CurrentMatch == TextureNameMatchMode.Fuzzy) sortmode.SelectedIndex = (int)TextureBrowserSortMode.Match;
			blockupdate = false;
			RefillList(false, false, "match");
		}

		private void advancedfilter_Changed(object sender, EventArgs e)
		{
			if(!blockupdate) RefillList(false, false, "controls");
		}

		internal bool IsFavoriteItem(ImageBrowserItem item)
		{
			return showfavorites && item.ItemType == ImageBrowserItemType.IMAGE && favorites != null && favorites.Contains(item.Icon);
		}

		internal static bool ValidateFavoriteOnly(ImageData image, TextureFavorites favorites, bool only)
		{
			return !only || (favorites != null && favorites.Contains(image));
		}

		private void FavoritesChanged(object sender, EventArgs e)
		{
			ResetPendingWork();
			RefillList(false, true, "favorites");
			list.Invalidate();
		}

		private void ToggleFavorite(ImageData image)
		{
			if(showfavorites && favorites != null && image != null && !image.IsDisposed) favorites.Toggle(image);
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if(showfavorites && keyData == (Keys.Control | Keys.D))
			{
				if(SelectedItem != null && SelectedItem.ItemType == ImageBrowserItemType.IMAGE) ToggleFavorite(SelectedItem.Icon);
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		private void list_ItemClicked(ImageBrowserItem item, MouseEventArgs e)
		{
			if(!showfavorites || favorites == null || e.Button != MouseButtons.Right || item.ItemType != ImageBrowserItemType.IMAGE) return;
			ImageData image = item.Icon;
			var menu = new ContextMenuStrip();
			menu.Items.Add(favorites.Contains(image) ? "Unpin from favorites" : "Pin to favorites", null, (sender, args) => ToggleFavorite(image));
			var first = new ToolStripMenuItem("Favorites first") { Checked = favoritesfirst, CheckOnClick = true };
			first.CheckedChanged += (sender, args) => { favoritesfirst = first.Checked; RefillList(false, true, "favorites"); };
			menu.Items.Add(first);
			menu.Closed += (sender, args) => menu.Dispose();
			menu.Show(Cursor.Position);
		}

		private void BuildColorMenu()
		{
			string[] names = { "Red", "Orange", "Yellow", "Chartreuse", "Green", "Spring green", "Cyan", "Azure", "Blue", "Violet", "Magenta", "Rose", "Black", "Gray", "White" };
			Color[] colors = { Color.Red, Color.FromArgb(255, 128, 0), Color.Yellow, Color.FromArgb(128, 255, 0), Color.Lime,
				Color.FromArgb(0, 255, 128), Color.Cyan, Color.FromArgb(0, 128, 255), Color.Blue, Color.FromArgb(128, 0, 255),
				Color.Magenta, Color.FromArgb(255, 0, 128), Color.Black, Color.Gray, Color.White };
			for(int i = 0; i < colors.Length; i++)
			{
				Color color = colors[i];
				var item = new ToolStripMenuItem(names[i]) { BackColor = color, ForeColor = color.GetBrightness() < 0.45 ? Color.White : Color.Black };
				item.Click += (sender, e) => SetColorSwatch(color);
				colormenu.Items.Add(item);
			}
			colormenu.Items.Add("Custom...", null, (sender, e) =>
			{
				using(var dialog = new ColorDialog { Color = colorswatch.IsEmpty ? Color.Red : colorswatch, FullOpen = true })
					if(dialog.ShowDialog(this) == DialogResult.OK) SetColorSwatch(dialog.Color);
			});
			colormenu.Items.Add("Off", null, (sender, e) => SetColorSwatch(Color.Empty));
			filtertooltip.SetToolTip(colorfilter, "Filter by mean or dominant color. Off disables the filter.");
			filtertooltip.SetToolTip(colortolerance, "Color distance: Tight 15, Normal 25, Loose 40 (CIE Delta E 76).");
		}

		private void colorfilter_Click(object sender, EventArgs e) { colormenu.Show(colorfilter, new Point(0, colorfilter.Height)); }

		private void SetColorSwatch(Color color)
		{
			colorswatch = color;
			colorfilter.UseVisualStyleBackColor = color.IsEmpty;
			colorfilter.BackColor = color.IsEmpty ? SystemColors.Control : color;
			colorfilter.ForeColor = !color.IsEmpty && color.GetBrightness() < 0.45 ? Color.White : Color.Black;
			colorfilter.Text = color.IsEmpty ? "Color" : "Color *";
			if(!blockupdate && items != null) RefillList(false, false, "color");
		}

		// Key pressed in textbox
		private void objectname_KeyDown(object sender, KeyEventArgs e)
		{
			// Toggle used items sorting
			if(e.KeyData == Keys.Tab)
			{
				usedtexturesfirst.Checked = !usedtexturesfirst.Checked;
				e.SuppressKeyPress = true;
			}
			//mxd. Clear text field instead of typing strange chars...
			else if(e.KeyData == (Keys.Back | Keys.Control))
			{
				if(objectname.Text.Length > 0) objectname.Clear();
				e.SuppressKeyPress = true;
			}
		}

		//mxd
		private void objectclear_Click(object sender, EventArgs e)
		{
			objectname.Clear();
			list.Focus();
		}

		//mxd
		private void filterSize_WhenTextChanged(object sender, EventArgs e)
		{
			RefillList(false, false, "size");
		}

		//mxd
		protected override bool ProcessTabKey(bool forward)
		{
			if (list.SelectedItems.Count == 0)
				return false;

			list.JumpBetweenItems(list.SelectedItems[0]);

			return false;
		}
		
		// Selection changed
		private void list_SelectionChanged(object sender, List<ImageBrowserItem> selection)
		{
			// Prevent selecting?
			if(preventselection)
			{
				if(selection.Count > 0) list.ClearSelection(); //mxd
			}
			else
			{
				// Raise event
				if(SelectedItemChanged != null)
					SelectedItemChanged(list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null);
			}
		}
		
		// Doublelicking an item
		private void list_ItemDoubleClicked(object sender, ImageBrowserItem item)
		{
			if(!preventselection && (list.SelectedItems.Count > 0))
				if(SelectedItemDoubleClicked != null) SelectedItemDoubleClicked(item);
		}

		//mxd. Transfer input to Filter textbox
		private void list_KeyPress(object sender, KeyPressEventArgs e)
		{
			if(e.KeyChar == 8) // Backspace
			{
				if(objectname.Text.Length > 0)
				{
					if(objectname.SelectionLength > 0)
					{
						objectname.Text = objectname.Text.Substring(0, objectname.SelectionStart) +
							objectname.Text.Substring(objectname.SelectionStart + objectname.SelectionLength);
					}
					else
					{
						objectname.Text = objectname.Text.Substring(0, objectname.Text.Length - 1);
					}
				}
			}
			else if(e.KeyChar == 127) // Ctrl-Backspace
			{
				if(objectname.Text.Length > 0) objectname.Clear();
			}
			else if((e.KeyChar >= 'a' && e.KeyChar <= 'z') || (e.KeyChar >= 'A' && e.KeyChar <= 'Z')
				|| (e.KeyChar >= '0' && e.KeyChar <= '9') || AllowedSpecialChars.Contains(e.KeyChar))
			{
				if(objectname.SelectionLength > 0)
				{
					objectname.Text = objectname.Text.Substring(0, objectname.SelectionStart) +
										 e.KeyChar +
										 objectname.Text.Substring(objectname.SelectionStart + objectname.SelectionLength);
				}
				else
				{
					objectname.Text += e.KeyChar;
				}
			}
		}

		//mxd
		private void texturetypecombo_SelectedIndexChanged(object sender, EventArgs e) 
		{
			texturetype = texturetypecombo.SelectedIndex;
            ElementName = (texturetype == 2 || (texturetype == 3 && browseflats)) ? "flats" : "textures";

            RefillList(false);
		}

		//mxd
		private void sizecombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(blockupdate) return;
			list.ImageSize = (sizecombo.SelectedIndex == 0 ? 0 : Convert.ToInt32(sizecombo.SelectedItem));
			list.Focus();
        }

		//mxd
		private void longtexturenames_CheckedChanged(object sender, EventArgs e)
		{
			if(!blockupdate)
			{
				uselongtexturenames = longtexturenames.Checked;
				UpdateCasing();

				foreach(var item in items) item.ShowFullName = uselongtexturenames;
				RefillList(false, false, "names");
				list.Focus();
			}
		}

		//mxd
		private void usedtexturesfirst_CheckedChanged(object sender, EventArgs e)
		{
			if(!blockupdate)
			{
                list.UsedTexturesFirst = usedtexturesfirst.Checked;
                RefillList(false);
				list.UpdateRectangles();
				list.Focus();
			}
		}

        //
        private void classicview_CheckedChanged(object sender, EventArgs e)
        {
            if(!blockupdate)
            {
                list.ClassicView = classicview.Checked;
                list.Focus();
            }
        }

        #endregion

        #region ================== Methods

        // This selects an item by longname (mxd - changed from name to longname)
        public void SelectItem(long longname)
		{
			// Not when selecting is prevented
			if(preventselection) return;

			// Search for item
			ImageBrowserItem target = null; //mxd
			foreach(ImageBrowserItem item in list.Items)
			{
				if(item.ItemType == ImageBrowserItemType.IMAGE && item.Icon.LongName == longname) //mxd
				{
					target = item;
					break;
				}
			}
			
			if(target != null)
			{
				// Select the item
				list.SetSelectedItem(target);
			}
		}
		
		// This selectes the first item
		private void SelectFirstItem()
		{
			// Not when selecting is prevented
			if(preventselection) return;

			// Select first texture
			if (list.Items.Count > 0)
			{
				foreach (ImageBrowserItem item in list.Items)
					if (item.ItemType == ImageBrowserItemType.IMAGE)
					{
						list.SetSelectedItem(item);
						break;
					}
			}
		}
		
		// This begins adding items
		public void BeginAdding(bool keepselectedindex)
		{
			if(keepselectedindex && (list.SelectedItems.Count > 0))
				keepselected = list.Items.IndexOf(list.SelectedItems[0]);
			else
				keepselected = -1;
			
			// Clean list
			items.Clear();
			filling = true;
			ResetPendingWork();
			filtertimer.Stop();
			previewdimensions.Clear();
			previewsreported = false;
			fillclock.Reset();
			
			// Stop updating
			refreshtimer.Enabled = false;
		}

		// This ends adding items
		public void EndAdding()
		{
			filling = false;
			fillclock.Restart();
			fillworkingstart = WorkingSet();
			// Fill list with items
			RefillList(true);

			// Start updating
			refreshtimer.Enabled = Visible && items.Count > 0;
		}

		//mxd. This adds a category item
		public void AddFolder(ImageBrowserItemType itemtype, string categoryname)
		{
			switch(itemtype)
			{
				case ImageBrowserItemType.FOLDER: case ImageBrowserItemType.FOLDER_UP:
					items.Add(new ImageBrowserCategoryItem(itemtype, categoryname));
					break;

				default: throw new Exception("Unsupported ImageBrowserItemType");
			}
		}
		
		// This adds an item
        // [ZZ] having nice string.Empty does not justify having two functions doing the same thing, with one parameter difference.
        //      C# not Java.
		public void AddItem(ImageData image, string tooltip = "")
		{
            ImageBrowserItem newItem = new ImageBrowserItem(image, tooltip, uselongtexturenames);
			items.Add(newItem);
		}

		// biwa. Removes all duplicates. That was done each time in AddItem before. Much faster
		// to do it in one go. Not sure when there are actually duplicates
		// Depending on whether a floor or a wall is selected the corrosponding images come first
		// in the list, so alawys keep the first occurence
		public void MakeTexturesUnique()
		{
			items = items.GroupBy(item => item.TextureName).Select(item => item.First()).ToList();
		}

		// This fills the list based on the objectname filter
		private void RefillList(bool selectfirst, bool quiet = false, string source = "refill")
		{
			if(blockupdate || filling) return;
			filtertimer.Stop();
			if(!quiet) ResetPendingWork();
			PrepareMatcher();
			if(queryfailed) return;
			Stopwatch elapsed = Stopwatch.StartNew();
			ImageBrowserItemGroup directoryitems = new ImageBrowserItemGroup("Directories");
			ImageBrowserItemGroup useditems = new ImageBrowserItemGroup("Used textures");
			ImageBrowserItemGroup visibleitems = new ImageBrowserItemGroup("All textures");
			ImageBrowserItemGroup favoriteitems = new ImageBrowserItemGroup("Favorites");
			Dictionary<ImageBrowserItem, int> scores = new Dictionary<ImageBrowserItem, int>();
			Dictionary<ImageBrowserItem, double> distances = new Dictionary<ImageBrowserItem, double>();
			HashSet<ImageData> pending = new HashSet<ImageData>();

			//mxd. Filtering by texture size?
			int w = filterWidth.GetResult(-1);
			int h = filterHeight.GetResult(-1);
			PixelColor swatch = PixelColor.FromColor(colorswatch.IsEmpty ? Color.Red : colorswatch);
			double tolerance = ColorTolerance(colortolerance.SelectedIndex);
			bool coloractive = !colorswatch.IsEmpty;
			bool color = NeedsColor, alpha = transparency.Checked, dimensions = w > 0 || h > 0 || CurrentSort == TextureBrowserSortMode.Size;
			bool onlyfavorites = showfavorites && favoritesonly.Checked;
			TextureNameMatchMode mode = CurrentMatch;
			
			// Go for all items
			ImageBrowserItem previtem = null; //mxd
			for(int i = items.Count - 1; i > -1; i--)
			{
				if(mode == TextureNameMatchMode.Regex && elapsed.Elapsed.TotalMilliseconds > 250)
				{
					SetQueryError("Pattern too slow (250 ms candidate limit).");
					ResetPendingWork();
					LogFilter(source, elapsed, visibleitems.Items.Count, pending.Count, "timeout");
					return;
				}
				// Add item if valid
				items[i].ShowFullName = uselongtexturenames; //mxd
				if(items[i].ItemType == ImageBrowserItemType.IMAGE && !previewdimensions.ContainsKey(items[i].Icon))
					previewdimensions.Add(items[i].Icon, new Size(items[i].Icon.Width, items[i].Icon.Height));
				int score;
				string error = null;
				switch(items[i].ItemType)
				{
					case ImageBrowserItemType.IMAGE:
						if(ValidateItem(items[i], previtem) && MatchWithinBudget(matcher, items[i].TextureName, mode == TextureNameMatchMode.Regex, elapsed, out score, out error)
							&& ValidateItemSize(items[i], w, h) && ValidateFavoriteOnly(items[i].Icon, favorites, onlyfavorites))
						{
							ImageData image = items[i].Icon;
							double distance = !coloractive || image.ColorStats == null ? double.PositiveInfinity : image.ColorStats.DistanceTo(swatch);
							if(!ValidateAttributes(image, alpha, coloractive, distance, tolerance)) break;
							if(IsPending(image, color, alpha, dimensions)) pending.Add(image);
							scores[items[i]] = score;
							distances[items[i]] = distance;
							visibleitems.Items.Add(items[i]);
							if(showfavorites && favoritesfirst && IsFavoriteItem(items[i])) favoriteitems.Items.Add(items[i]);

							if (items[i].Icon.UsedInMap)
								useditems.Items.Add(items[i]);

							previtem = items[i];
						}
						break;

					case ImageBrowserItemType.FOLDER_UP: //mxd. "Browse Up" items are always valid
						directoryitems.Items.Add(items[i]);
						break;

					case ImageBrowserItemType.FOLDER: //mxd. Only apply name filtering to "Folder" items
						if(MatchWithinBudget(matcher, items[i].TextureName, mode == TextureNameMatchMode.Regex, elapsed, out score, out error))
							directoryitems.Items.Add(items[i]);
						break;

					default: throw new NotImplementedException("Unknown ImageBrowserItemType");
				}
				if(error != null)
				{
					SetQueryError(error);
					ResetPendingWork();
					LogFilter(source, elapsed, visibleitems.Items.Count, pending.Count, "timeout");
					return;
				}
			}
			if(mode == TextureNameMatchMode.Regex && elapsed.Elapsed.TotalMilliseconds > 250)
			{
				SetQueryError("Pattern too slow (250 ms candidate limit).");
				ResetPendingWork();
				LogFilter(source, elapsed, visibleitems.Items.Count, pending.Count, "timeout");
				return;
			}

			// Fill list
			directoryitems.Items.Sort();
			TextureBrowserSortMode sort = CurrentSort;
			if(sort == TextureBrowserSortMode.SimilarColor && colorswatch.IsEmpty) sort = TextureBrowserSortMode.Name;
			Comparison<ImageBrowserItem> comparer = (left, right) => CompareItems(left, right, sort, scores, distances);
			useditems.Items.Sort(comparer);
			visibleitems.Items.Sort(comparer);
			favoriteitems.Items.Sort(comparer);

			List<ImageBrowserItemGroup> itemgroups = new List<ImageBrowserItemGroup>();

			// Always add the directories
			if(directoryitems.Items.Count > 0) itemgroups.Add(directoryitems);
			if(favoriteitems.Items.Count > 0) itemgroups.Add(favoriteitems);

			// Add used textures if the user wants to and there are any
			if (usedtexturesfirst.Checked && useditems.Items.Count > 0)
				itemgroups.Add(useditems);

			// Finally add all items
			if (visibleitems.Items.Count > 0)
				itemgroups.Add(visibleitems);

			pendingstats = new Queue<ImageData>(pending);
			pendingversion = sourceversion;
			statsdirty = false;
			list.Title = (usedtexturesfirst.Checked ? usedfirstgroup : availgroup)
				+ (pending.Count > 0 ? " (" + pending.Count + " loading)" : string.Empty);
			list.SetItems(itemgroups, quiet);
			if(Visible && pending.Count > 0) statstimer.Start();
			if(Visible && items.Count > 0) refreshtimer.Start();
			if(quiet)
			{
				laststatsrefill = InteractionLog.Now;
				LogFilter(source, elapsed, visibleitems.Items.Count, pending.Count, "ok");
				return;
			}

			// Make selection?
			if (!preventselection && list.Items.Count > 0)
			{
				// Select specific item?
				if(keepselected > -1)
				{
					// If the last item doesn't exist anymore select the new last item
					if(keepselected >= list.Items.Count)
						list.SetSelectedItem(list.Items[list.Items.Count-1]);
					else
						list.SetSelectedItem(list.Items[keepselected]);
				}
				// Select first item?
				else if(selectfirst)
				{
					SelectFirstItem();
				}
				//mxd. Try reselecting the same/next closest item
				else if(!string.IsNullOrEmpty(objectname.Text))
				{
					if(CurrentMatch != TextureNameMatchMode.Name) SelectFirstItem();
					else
					{
					ImageBrowserItem bestmatch = null;
					int charsmatched = 1;
					foreach(ImageBrowserItem item in list.Items)
					{
						if(item.ItemType == ImageBrowserItemType.IMAGE && item.Icon.Name[0] == objectname.Text[0])
						{
							if (bestmatch == null)
								bestmatch = item;

							if(item.Icon.Name == objectname.Text)
							{
								bestmatch = item;
								break;
							}

							for(int i = 1; i < Math.Min(item.Icon.Name.Length, objectname.Text.Length); i++)
							{
								if(item.Icon.Name[i] == objectname.Text[i])
								{
									if(i > charsmatched)
									{
										bestmatch = item;
										charsmatched = i;
									}
									break;
								}
							}
						}
					}
					
					// Select found item
					if (bestmatch != null)
					{
						list.SetSelectedItem(bestmatch);
					}
					else
					{
						SelectFirstItem();
					}
					}
				}
			}
			if(!preventselection && list.SelectedItems.Count == 0) SelectFirstItem();
			
			// Raise event
			if((SelectedItemChanged != null) && !preventselection)
				SelectedItemChanged(list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null);
			if(CurrentMatch == TextureNameMatchMode.Fuzzy && elapsed.Elapsed.TotalMilliseconds > 50) debouncefuzzy = true;
			LogFilter(source, elapsed, visibleitems.Items.Count, pending.Count, "ok");
		}

		internal static double ColorTolerance(int index) { return index == 0 ? 15 : index == 2 ? 40 : 25; }

		internal static bool ValidateAttributes(ImageData image, bool alpha, bool color, double distance, double tolerance)
		{
			if(alpha && image.HasTransparencyInfo && !image.IsMasked && !image.IsTranslucent) return false;
			return !color || image.ColorStats == null || distance <= tolerance;
		}

		internal static bool MatchWithinBudget(TextureNameMatcher matcher, string name, bool regex, Stopwatch elapsed, out int score, out string error)
		{
			score = 0;
			error = matcher.Error;
			if(error != null) return false;
			if(regex && elapsed.Elapsed.TotalMilliseconds > 250)
			{
				error = "Pattern too slow (250 ms candidate limit).";
				return false;
			}
			bool matched = matcher.Match(name, out score);
			error = matcher.Error;
			if(regex && error == null && elapsed.Elapsed.TotalMilliseconds > 250) error = "Pattern too slow (250 ms candidate limit).";
			return error == null && matched;
		}

		private void LogFilter(string source, Stopwatch elapsed, int matched, int pending, string result)
		{
			if(InteractionLog.Enabled) InteractionLog.Write("browser", "stage", "imgfilter", "src", source,
				"mode", CurrentMatch.ToString(), "items", items.Count.ToString(CultureInfo.InvariantCulture),
				"matched", matched.ToString(CultureInfo.InvariantCulture), "pending", pending.ToString(CultureInfo.InvariantCulture),
				"ms", elapsed.Elapsed.TotalMilliseconds.ToString("0.000", CultureInfo.InvariantCulture), "result", result);
		}

		// This validates an item
		private bool ValidateItem(ImageBrowserItem item, ImageBrowserItem previtem)
		{
            //mxd. mixMode: 0 = All, 1 = Textures, 2 = Flats, 3 = Based on BrowseFlats
            //if (!splitter.Panel2Collapsed) 
			{
                if (texturetype == 0 && previtem != null && item.TextureName == previtem.TextureName) return false;
				if (texturetype == 1 && item.Icon.TextureNamespace == TextureNamespace.FLAT) return false;
				if (texturetype == 2 && !(item.Icon.TextureNamespace == TextureNamespace.FLAT)) return false;
				if (texturetype == 3 && (browseflats != (item.Icon.TextureNamespace == TextureNamespace.FLAT))) return false;
			}
            //else if (previtem != null && item.TextureName == previtem.TextureName) return false;

			return true;
		}

		//mxd. This validates an item's texture size
		internal static bool ValidateItemSize(ImageBrowserItem i, int w, int h)
		{
			if(w > 0 && i.Icon.Width > 0 && i.Icon.Width != w) return false;
			if(h > 0 && i.Icon.Height > 0 && i.Icon.Height != h) return false;
			return true;
		}

		internal static int CompareItems(ImageBrowserItem left, ImageBrowserItem right, TextureBrowserSortMode mode,
			IDictionary<ImageBrowserItem, int> scores, IDictionary<ImageBrowserItem, double> distances)
		{
			int result = 0;
			if(left.ItemType != ImageBrowserItemType.IMAGE || right.ItemType != ImageBrowserItemType.IMAGE) return left.CompareTo(right);
			switch(mode)
			{
				case TextureBrowserSortMode.Match:
					result = scores[right].CompareTo(scores[left]);
					break;
				case TextureBrowserSortMode.Size:
					bool leftknown = left.Icon.Width > 0 && left.Icon.Height > 0;
					bool rightknown = right.Icon.Width > 0 && right.Icon.Height > 0;
					if(leftknown != rightknown) result = leftknown ? -1 : 1;
					else if(leftknown)
					{
						result = ((long)left.Icon.Width * left.Icon.Height).CompareTo((long)right.Icon.Width * right.Icon.Height);
						if(result == 0) result = left.Icon.Width.CompareTo(right.Icon.Width);
					}
					break;
				case TextureBrowserSortMode.Hue:
					result = ImageColorStats.CompareHue(left.Icon.ColorStats, right.Icon.ColorStats);
					break;
				case TextureBrowserSortMode.Brightness:
					bool lc = left.Icon.ColorStats != null && left.Icon.ColorStats.HasColor;
					bool rc = right.Icon.ColorStats != null && right.Icon.ColorStats.HasColor;
					if(lc != rc) result = lc ? -1 : 1;
					else if(lc) result = left.Icon.ColorStats.LabL.CompareTo(right.Icon.ColorStats.LabL);
					break;
				case TextureBrowserSortMode.SimilarColor:
					result = distances[left].CompareTo(distances[right]);
					break;
			}
			return result != 0 ? result : left.CompareTo(right);
		}
		
		//mxd. This sends the focus to the textures list
		public void FocusList()
		{
			list.Focus();
		}

		private void centeritem_CheckedChanged(object sender, EventArgs e)
		{
			list.CenterItem = centeritem.Checked;
		}

		#endregion
	}
}
