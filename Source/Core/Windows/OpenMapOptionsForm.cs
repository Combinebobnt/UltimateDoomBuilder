
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
using System.Windows.Forms;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using System.IO;
using System.Collections;
using System.Text.RegularExpressions;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Config;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	internal partial class OpenMapOptionsForm : DelayedForm
	{
		// Variables
		private Configuration mapsettings;
		private MapOptions options;
		private WAD wadfile;
		private readonly string filepathname;
		private string selectedmapname;
		private readonly ResourceWarningResize warningresize;

		// Archive mode: the maps of every WAD inside a PK3/ZIP
		private readonly ArchiveMapScan scan;
		private readonly ArchiveMapStore store;
		private readonly Dictionary<ArchiveMapCandidate, Configuration> candidatesettings = new Dictionary<ArchiveMapCandidate, Configuration>();
		private ArchiveMapCandidate selectedcandidate;
		private string preselectentry;
		private string preselectmap;
		private readonly MapOptions initialoptions;

		private sealed class ArchiveRow
		{
			public ArchiveMapCandidate Candidate;
			public string MapName;
		}

		// Properties
		//public string FilePathName { get { return filepathname; } }
		public MapOptions Options { get { return options; } }
		public ArchiveMapCandidate SelectedCandidate { get { return selectedcandidate; } }
		private bool ArchiveMode { get { return scan != null; } }

		// Constructor
		public OpenMapOptionsForm(string filepathname)
		{
			// Initialize
			InitializeComponent();
			warningresize = new ResourceWarningResize(this, panelres, datalocations, apply);
			General.ApplyMonoListViewFix(mapslist);
			this.Text = "Open Map from " + Path.GetFileName(filepathname);
			this.filepathname = filepathname;
			datalocations.StartPath = filepathname; //mxd
			datalocations.IsMapControl = true;
			this.options = null;
		}

		// Constructor
		public OpenMapOptionsForm(string filepathname, MapOptions options)
		{
			// Initialize
			InitializeComponent();
			warningresize = new ResourceWarningResize(this, panelres, datalocations, apply);
			General.ApplyMonoListViewFix(mapslist);
			this.Text = "Open Map from " + Path.GetFileName(filepathname);
			this.filepathname = filepathname;
			this.options = options;
			datalocations.StartPath = filepathname; //mxd
			datalocations.IsMapControl = true;
		}

		// Archive mode. options, when given, preselects its map and configuration (command line -map).
		public OpenMapOptionsForm(ArchiveMapScan scan, MapOptions options)
		{
			InitializeComponent();
			warningresize = new ResourceWarningResize(this, panelres, datalocations, apply);
			General.ApplyMonoListViewFix(mapslist);
			this.scan = scan;
			this.store = ArchiveMapStore.ForArchive(scan.ArchivePath);
			this.Text = "Open Map from " + Path.GetFileName(scan.ArchivePath);
			this.filepathname = scan.ArchivePath;
			this.options = options;
			this.initialoptions = options;
			datalocations.StartPath = scan.ArchivePath;
			datalocations.IsMapControl = true;

			// Two columns, so maps with the same name in different entries can be told apart
			mapslist.Columns.Clear();
			mapslist.Columns.Add("Map", 90);
			mapslist.Columns.Add("Archive entry", mapslist.ClientSize.Width - 90 - SystemInformation.VerticalScrollBarWidth);
			mapslist.View = View.Details;
			mapslist.HeaderStyle = ColumnHeaderStyle.Nonclickable;
			mapslist.Sorting = SortOrder.None;

			string lastentry, lastmap;
			store.ReadIndex(out lastentry, out lastmap);
			preselectentry = lastentry;
			preselectmap = (options != null && !string.IsNullOrEmpty(options.CurrentName)) ? options.CurrentName : lastmap;
			if(options != null && !string.IsNullOrEmpty(options.CurrentName) && lastmap != options.CurrentName) preselectentry = null;
		}

		// The stored map settings of an archive entry, read once
		private Configuration SettingsFor(ArchiveMapCandidate candidate)
		{
			if(candidate == null) return mapsettings;

			Configuration settings;
			if(!candidatesettings.TryGetValue(candidate, out settings))
			{
				string dbsfile = store.SettingsFile(candidate.Source);
				settings = new Configuration(true);
				if(File.Exists(dbsfile))
				{
					try { settings = new Configuration(dbsfile, true); }
					catch(Exception) { settings = new Configuration(true); }
				}
				candidatesettings[candidate] = settings;
			}

			return settings;
		}

		// A map's resource list from its settings. A map never opened from the archive gets the archive itself, visible and removable.
		private DataLocationList MapResources(ArchiveMapCandidate candidate, string mapname)
		{
			Configuration settings = SettingsFor(candidate);
			if(candidate != null && !settings.SettingExists("maps." + mapname))
			{
				DataLocationList archiveonly = new DataLocationList();
				archiveonly.Add(new DataLocation(DataLocation.RESOURCE_PK3, scan.ArchivePath, false, false, false, null));
				return archiveonly;
			}

			return new DataLocationList(settings, "maps." + mapname + ".resources");
		}

		private string ArchiveEmptyMessage()
		{
			string message = "This archive contains no WAD files with maps for any game configuration.";
			if(scan.Warnings.Count > 0) message += "\n\n" + string.Join("\n", scan.Warnings);
			return message;
		}

		// "maps/X.wad" holding a map not named X: GZDoom-family engines name that map X
		private static string EngineNameHint(ArchiveMapCandidate candidate, string mapname)
		{
			string entry = candidate.Source.EntryName.Replace('\\', '/');
			if(!entry.StartsWith("maps/", StringComparison.OrdinalIgnoreCase) || entry.IndexOf('/', 5) != -1) return string.Empty;
			string filename = Path.GetFileNameWithoutExtension(entry);
			return string.Equals(filename, mapname, StringComparison.OrdinalIgnoreCase) ? string.Empty : " (engine name: " + filename.ToUpperInvariant() + ")";
		}

		private bool MatchesAny(Configuration cfg)
		{
			if(!ArchiveMode) return MatchConfiguration(cfg, wadfile);
			foreach(ArchiveMapCandidate c in scan.Candidates)
				if(MatchConfiguration(cfg, c.Wad)) return true;
			return false;
		}

		private GameConfiguration GetGameConfiguration()
		{
			if (config.Items.Count == 0 || config.SelectedIndex < 0) return null;

			return new GameConfiguration((config.SelectedItem as ConfigurationInfo).Configuration);
		}

		// This loads the settings and attempt to find a suitable config
		private void LoadSettings()
		{
			string gameconfig;
			int index;
			
			// Busy
			Cursor.Current = Cursors.WaitCursor;

			if(ArchiveMode)
			{
				// The last opened entry's settings pick the configuration
				ArchiveMapCandidate last = scan.Candidates.Find(c => ArchiveMapStore.EntryKey(c.Source.EntryNameBytes) == preselectentry);
				mapsettings = last != null ? SettingsFor(last) : new Configuration(true);
			}
			// Check if the file exists
			else if(!File.Exists(filepathname))
			{
				// WAD file does not exist
				MessageBox.Show(this, "Could not open the WAD file: The file does not exist.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
				this.DialogResult = DialogResult.Cancel;
				this.Close();
				return;
			}

			if(!ArchiveMode)
			{
				try
				{
					// Open the WAD file
					wadfile = new WAD(filepathname, true);
				}
				catch(Exception)
				{
					// Unable to open WAD file (or its config)
					MessageBox.Show(this, "Could not open the WAD file for reading. Please make sure the file you selected is valid and is not in use by any other application.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
					if(wadfile != null) wadfile.Dispose();
					this.DialogResult = DialogResult.Cancel;
					this.Close();
					return;
				}

				// Open the Map Settings configuration
				string dbsfile = filepathname.Substring(0, filepathname.Length - 4) + ".dbs";
				if(File.Exists(dbsfile))
					try { mapsettings = new Configuration(dbsfile, true); }
					catch(Exception) { mapsettings = new Configuration(true); }
				else
					mapsettings = new Configuration(true);
			}

			// Check strict patches box, check what game configuration is preferred
			if(options != null) 
			{
				strictpatches.Checked = options.StrictPatches;
				gameconfig = options.ConfigFile;
			} 
			else 
			{
				strictpatches.Checked = mapsettings.ReadSetting("strictpatches", false);
				gameconfig = mapsettings.ReadSetting("gameconfig", "");
			}

			//mxd. Fill script compilers list
			foreach(KeyValuePair<string, ScriptConfiguration> group in General.CompiledScriptConfigs) 
			{
				scriptcompiler.Items.Add(group.Value);
			}

			// Go for all configurations
			foreach(ConfigurationInfo info in General.Configs)
			{
				// Add config name to list
				index = config.Items.Add(info);

				// Select this item
				if(info.Filename == gameconfig) config.SelectedIndex = index;
			}

			// Still no configuration selected?
			if(config.SelectedIndex == -1) 
			{
				//mxd. Then go for all ENABLED configurations with resources to find a suitable one
				foreach(ConfigurationInfo info in General.Configs)
				{
					// Check if a resource location is set for this configuration, if so, match the wad against this configuration
					if(info.Enabled && info.Resources.Count > 0 && MatchesAny(info.Configuration))
					{
						//mxd. Already added?
						index = config.Items.IndexOf(info);

						// Select or add and select this item
						config.SelectedIndex = (index != -1 ? index : config.Items.Add(info)); 
						break;
					}
				}
			}

			//mxd. Still no configuration selected?
			if(config.SelectedIndex == -1)
			{
				// Then go for all DISABLED configurations with resources to find a suitable one
				foreach(ConfigurationInfo info in General.Configs)
				{
					// Check if a resource location is set for this configuration, if so, match the wad against this configuration
					if(!info.Enabled && info.Resources.Count > 0 && MatchesAny(info.Configuration))
					{
						//mxd. Already added?
						index = config.Items.IndexOf(info);

						// Select or add and select this item
						config.SelectedIndex = (index != -1 ? index : config.Items.Add(info));
						break;
					}
				}
			}

			//mxd. Still no configuration selected?
			if(config.SelectedIndex == -1)
			{
				//mxd. Then go for all ENABLED configurations without resources to find a suitable one
				foreach(ConfigurationInfo info in General.Configs)
				{
					// Check if a resource location is not set for this configuration, if so, match the wad against this configuration
					if(info.Enabled && info.Resources.Count == 0 && MatchesAny(info.Configuration))
					{
						//mxd. Already added?
						index = config.Items.IndexOf(info);

						// Select or add and select this item
						config.SelectedIndex = (index != -1 ? index : config.Items.Add(info));
						break;
					}
				}
			}

			//mxd. Still no configuration selected?
			if(config.SelectedIndex == -1)
			{
				// Then go for all DISABLED configurations without resources to find a suitable one
				foreach(ConfigurationInfo info in General.Configs)
				{
					// Check if a resource location is not set for this configuration, if so, match the wad against this configuration
					if(!info.Enabled && info.Resources.Count == 0 && MatchesAny(info.Configuration))
					{
						//mxd. Already added?
						index = config.Items.IndexOf(info);

						// Select or add and select this item
						config.SelectedIndex = (index != -1 ? index : config.Items.Add(info));
						break;
					}
				}
			}

            // [ZZ] dispose of wadfile
            if(wadfile != null) wadfile.Dispose();

            //mxd. Bail out if still no dice...
            if (config.SelectedIndex == -1 || mapslist.Items.Count == 0)
			{
				General.ShowWarningMessage(ArchiveMode ? ArchiveEmptyMessage() : "Unable to find maps using any game configuration.\nDoes this wad contain any maps at all?..", MessageBoxButtons.OK);
				cancel_Click(this, EventArgs.Empty);
			}
			else
			{
				// Show the window
				this.Opacity = 1;
				datalocations.GameConfiguration = GetGameConfiguration();
			}

			// Done
			Cursor.Current = Cursors.Default;
		}
		
		// mxd. This matches a WAD file with the specified game configuration
		// by checking if the specific lumps are detected
		private static bool MatchConfiguration(Configuration cfg, WAD wadfile)
		{
			return MapLumpScanner.Matches(cfg, wadfile);
		}

		// One row per map per entry, by entry then map; keeps the selected row, else the preselected one
		private void FillArchiveMaps(Configuration cfg)
		{
			ArchiveRow selected = mapslist.SelectedItems.Count > 0 ? mapslist.SelectedItems[0].Tag as ArchiveRow : null;
			List<ArchiveMapCandidate> candidates = new List<ArchiveMapCandidate>(scan.Candidates);
			candidates.Sort((a, b) => string.Compare(a.Source.EntryName, b.Source.EntryName, StringComparison.OrdinalIgnoreCase));

			mapslist.Items.Clear();
			ListViewItem reselect = null;
			ListViewItem preselect = null;
			foreach(ArchiveMapCandidate c in candidates)
			{
				List<string> maps = MapLumpScanner.FindMaps(cfg, c.Wad, true);
				maps.Sort(StringComparer.OrdinalIgnoreCase);
				foreach(string mapname in maps)
				{
					string entry = c.Source.EntryName + EngineNameHint(c, mapname) + (c.Writable ? "" : " (read-only: Save will ask for a new WAD)");
					ListViewItem item = new ListViewItem(new[] { mapname, entry }) { Tag = new ArchiveRow { Candidate = c, MapName = mapname } };
					mapslist.Items.Add(item);

					if(selected != null && selected.Candidate == c && selected.MapName == mapname) reselect = item;
					bool entrymatches = preselectentry == null || ArchiveMapStore.EntryKey(c.Source.EntryNameBytes) == preselectentry;
					if(preselect == null && entrymatches && mapname == preselectmap) preselect = item;
				}
			}

			ListViewItem pick = reselect ?? preselect;
			if(pick != null)
			{
				pick.Selected = true;
				pick.EnsureVisible();
			}
		}

		// Configuration is selected
		private void config_SelectedIndexChanged(object sender, EventArgs e)
		{
			//
			datalocations.GameConfiguration = GetGameConfiguration();

			// Anything selected?
			if(config.SelectedIndex < 0) return;

			// Keep selected name, if any
			string selectedname = (mapslist.SelectedItems.Count > 0 ? mapslist.SelectedItems[0].Text : "");

            // Get selected configuration info
            ConfigurationInfo ci = (config.SelectedItem as ConfigurationInfo);

			//mxd. Get configuration
			Configuration cfg = ci.Configuration;

			// Clear the list and add the new map names
			mapslist.BeginUpdate();
			if(ArchiveMode)
			{
				FillArchiveMaps(cfg);
			}
			else
			{
				List<string> mapnames = MapLumpScanner.FindMaps(cfg, wadfile, true);
				mapslist.Items.Clear();
				foreach(string mapname in mapnames)
					mapslist.Items.Add(mapname);
				mapslist.Sort();

				// Go for all items in the list
				foreach(ListViewItem item in mapslist.Items)
				{
					// Was this item previously selected?
					if(item.Text == selectedname)
					{
						// Select it again
						item.Selected = true;
						break;
					}
				}
			}
			if((mapslist.SelectedItems.Count == 0) && (mapslist.Items.Count > 0))
				mapslist.Items[0].Selected = true;
			mapslist.EndUpdate();

			//mxd. Disable script compiler selector when there are no maps detected using current configuration
			if(mapslist.Items.Count == 0) 
			{
				scriptcompiler.Enabled = false;
				scriptcompiler.SelectedIndex = -1;
				scriptcompilerlabel.Enabled = false;
			}

			// Show configuration resources
			datalocations.FixedResourceLocationList(ci.Resources);

			// Update long texture names checkbox (mxd)
			longtexturenames.Enabled = cfg.ReadSetting("longtexturenames", false);
			longtexturenames.Checked = (longtexturenames.Enabled && options != null && options.UseLongTextureNames);
		}
		
		// OK clicked
		private void apply_Click(object sender, EventArgs e)
		{
			// Configuration selected?
			if(config.SelectedIndex == -1)
			{
				// Select a configuration!
				MessageBox.Show(this, "Please select a game configuration to use for editing your map.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				config.Focus();
				return;
			}

			//mxd. Script configuration selected?
			if(scriptcompiler.Enabled && scriptcompiler.SelectedIndex == -1)
			{
				// Select a configuration!
				MessageBox.Show(this, "Please select a script type to use for editing your map.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				scriptcompiler.Focus();
				return;
			}
			
			// Collect information
			ConfigurationInfo configinfo = (config.SelectedItem as ConfigurationInfo); //mxd
			DataLocationList locations = datalocations.GetResources();

			// Resources are valid? (mxd)
			if(!datalocations.ResourcesAreValid())
			{
				MessageBox.Show(this, "Cannot open map: at least one resource doesn't exist!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				datalocations.Focus();
				return;
			}
			
			// No map selected?
			if(mapslist.SelectedItems.Count == 0)
			{
				// Choose a map!
				MessageBox.Show(this, "Please select a map to load for editing.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				mapslist.Focus();
				return;
			}

			//mxd. We cant't deal with this... We just can't... 
			if(!configinfo.ValidateMapName(mapslist.SelectedItems[0].Text.ToUpperInvariant()))
			{
				// Choose a different map!
				MessageBox.Show(this, "Selected map name conflicts with a lump name defined for current map format.\nPlease rename the map and try again.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				mapslist.Focus();
				return;
			}
			
			// Check if we should warn the user for missing resources
			WAD selectedwad = ArchiveMode ? selectedcandidate.Wad : wadfile;
			if((!selectedwad.IsIWAD) && (locations.Count == 0) && (configinfo.Resources.Count == 0))
			{
				if(MessageBox.Show(this, "You are about to load a map without selecting any resources. Textures, flats and " +
										 "sprites may not be shown correctly or may not show up at all. Do you want to continue?", Application.ProductName,
										 MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.No)
				{
					return;
				}
			}
			
			// Apply changes
			options.ClearResources();
			options.ConfigFile = configinfo.Filename;
			options.CurrentName = mapslist.SelectedItems[0].Text;
			options.StrictPatches = strictpatches.Checked;
			options.CopyResources(locations);

			//mxd. Store script compiler
			if(scriptcompiler.Enabled) 
			{
				ScriptConfiguration scriptcfg = scriptcompiler.SelectedItem as ScriptConfiguration;
				foreach(KeyValuePair<string, ScriptConfiguration> group in General.CompiledScriptConfigs) 
				{
					if(group.Value == scriptcfg) 
					{
						options.ScriptCompiler = group.Key;
						break;
					}
				}
			}

			//mxd. Use long texture names?
			if(longtexturenames.Enabled) options.UseLongTextureNames = longtexturenames.Checked;

			// Hide window
			if(wadfile != null) wadfile.Dispose();
			this.DialogResult = DialogResult.OK;
			this.Close();
		}
		
		// Cancel clicked
		private void cancel_Click(object sender, EventArgs e)
		{
			// Just hide window
			if(wadfile != null) wadfile.Dispose();
			this.DialogResult = DialogResult.Cancel;
			this.Close();
		}

		// Window is shown
		private void OpenMapOptionsForm_Shown(object sender, EventArgs e)
		{
			// Update window
			this.Update();
			
			// Load settings
			LoadSettings();

			// Show
			if(ArchiveMode)
			{
				// Command line resources join the selected map's own
				if(initialoptions != null && initialoptions.Resources.Count > 0)
				{
					DataLocationList listed = datalocations.GetResources();
					foreach(DataLocation dl in initialoptions.Resources)
						if(!listed.Contains(dl)) listed.Add(dl);
					datalocations.EditResourceLocationList(listed);
				}
			}
			else if (options != null)
				datalocations.EditResourceLocationList(options.Resources);
		}

		// Map name doubleclicked
		private void mapslist_DoubleClick(object sender, EventArgs e)
		{
			// Click OK
			if(mapslist.SelectedItems.Count > 0) apply.PerformClick();
		}

		// Map name selected
		private void mapslist_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			//
			datalocations.GameConfiguration = GetGameConfiguration();

			if (!e.IsSelected) return; //mxd. Don't want to trigger this twice
			
			DataLocationList locations;
			DataLocationList listedlocations;
			string scriptconfig = string.Empty; //mxd
			
			// Map previously selected?
			if(!string.IsNullOrEmpty(selectedmapname))
			{
				// Get locations from previous selected map settings
				locations = MapResources(selectedcandidate, selectedmapname);
				listedlocations = datalocations.GetResources();
				
				// Remove data locations that this map has in its config
				foreach(DataLocation dl in locations)
					listedlocations.Remove(dl);

				// Set new data locations
				datalocations.EditResourceLocationList(listedlocations);

				// Done
				selectedmapname = null;
				selectedcandidate = null;
			}
			
			// Anything selected?
			if(mapslist.SelectedItems.Count > 0)
			{
				// Get the map name
				selectedmapname = mapslist.SelectedItems[0].Text;
				ArchiveRow row = mapslist.SelectedItems[0].Tag as ArchiveRow;
				selectedcandidate = row != null ? row.Candidate : null;
				options = new MapOptions(SettingsFor(selectedcandidate), selectedmapname, longtexturenames.Enabled);
				
				// Get locations from previous selected map settings
				locations = MapResources(selectedcandidate, selectedmapname);
				listedlocations = datalocations.GetResources();

				// Add data locations that this map has in its config
				foreach(DataLocation dl in locations)
					if(!listedlocations.Contains(dl)) listedlocations.Add(dl);

				// Set new data locations
				datalocations.EditResourceLocationList(listedlocations);

				//mxd. Select script compiler
				ConfigurationInfo info = config.SelectedItem as ConfigurationInfo;
				if(info != null)
				{
					if(!string.IsNullOrEmpty(options.ScriptCompiler) && General.CompiledScriptConfigs.ContainsKey(options.ScriptCompiler))
					{
						scriptconfig = options.ScriptCompiler;
					}
					else if(!string.IsNullOrEmpty(info.DefaultScriptCompiler) && General.CompiledScriptConfigs.ContainsKey(info.DefaultScriptCompiler))
					{
						scriptconfig = info.DefaultScriptCompiler;
					}
				}
			}

			//mxd. Select proper script compiler
			if(!string.IsNullOrEmpty(scriptconfig))
			{
				scriptcompiler.Enabled = true;
				scriptcompiler.SelectedItem = General.CompiledScriptConfigs[scriptconfig];
				scriptcompilerlabel.Enabled = true;
			}
			else
			{
				scriptcompiler.Enabled = false;
				scriptcompiler.SelectedIndex = -1;
				scriptcompilerlabel.Enabled = false;
			}
		}

		// Help
		private void OpenMapOptionsForm_HelpRequested(object sender, HelpEventArgs hlpevent)
		{
			General.ShowHelp("w_openmapoptions.html");
			hlpevent.Handled = true;
		}

        private void datalocations_OnWarningsChanged(int size)
        {
			warningresize.Apply(size);
		}
    }
}
