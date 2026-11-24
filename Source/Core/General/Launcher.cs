
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
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder
{
	internal class Launcher : IDisposable
	{
		#region ================== Constants

		internal const string STALE_LIGHTMAP_WARNING = "Testing without the lightmap: the map changed since it was baked (Tools > Bake Lightmaps).";
		private const string STALE_LIGHTMAP_QUESTION = "This map changed since its lightmap was baked, so the test will run without the lightmap.";
		private const string STALE_LIGHTMAP_REBAKE = "\n\nRebake before testing?\nYes: bake, then test.\nNo: test without the lightmap.\nCancel: don't test.";
		private const string STALE_LIGHTMAP_NO_REBAKE = "\n\nOK: test without the lightmap.\nCancel: don't test.";

		#endregion

		#region ================== Enums

		// What Test does about a bake its save could not write
		internal enum StaleLightmapAction { None, Warn, Ask, Rebake }

		#endregion

		#region ================== Variables

		private string tempwad;
		private Dictionary<Process, string> processes; //mxd
		private bool isdisposed;
		private bool retestingafterbake; // The Test after an auto-bake only warns if its save still misses the bake

		private static Dictionary<int, string> additionalexceptiontext = new Dictionary<int, string>() {
			{ 216,
				"It looks like your test program ({0}) is a DOS executable, which is not compatible with your operating system. Please use an engine that is compatible with your operating system instead."
			},
			{ 1223,
				"It looks like your test program ({0}) was blocked by Microsoft Defender SmartScreen. To unblock the test program you have two options.\n" +
				"\n" +
				"Option 1:\n" +
				"- Run the program manually\n" +
				"- Click on \"More info\"\n" +
				"- Click on \"Run anyway\"\n" +
				"\n" +
				"Option 2:\n" +
				"- Right-click on the program and select \"Properties\"\n" +
				"- In the \"General\" tab check the \"Unblock\" checkbox\n" +
				"- Click OK\n" +
				"\n" +
				"After performing one of these options, please try launching the test again."
			}
		};

		delegate void EngineExitedCallback(Process p); //mxd
		
		#endregion

		#region ================== Properties

		public string TempWAD { get { return tempwad; } }

		#endregion

		#region ================== Constructor / Destructor

		// Constructor
		public Launcher(MapManager manager)
		{
			// Initialize
			InitializeTempFile(manager);
			processes = new Dictionary<Process, string>(); //mxd

			// Bind actions
			General.Actions.BindMethods(this);
		}

		// Disposer
		public void Dispose()
		{
			// Not yet disposed?
			if(!isdisposed)
			{
				// Unbind actions
				General.Actions.UnbindMethods(this);

				//mxd. Terminate running processes?
				if(processes != null) 
				{
					foreach(KeyValuePair<Process, string> group in processes)
					{
						// Close engine
						group.Key.CloseMainWindow();
						group.Key.Close();

						// Remove temporary file
						if(File.Exists(group.Value))
						{
							try { File.Delete(group.Value); }
							catch { }
						}
					}
				}
				
				// Remove temporary file
				if(File.Exists(tempwad))
				{
					try { File.Delete(tempwad); }
					catch { }
				}
				
				// Done
				isdisposed = true;
			}
		}

		#endregion

		#region ================== Parameters

		// Appends the configuration's lightmap parameter when the test WAD has a LIGHTMAP, unless the
		// arguments already carry it
		internal static string AddLightmapParameter(string args, bool haslightmap, string parameter)
		{
			if(!haslightmap || string.IsNullOrWhiteSpace(parameter)) return args;
			string[] tokens = (args ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if(Array.IndexOf(tokens, parameter.Trim()) > -1) return args;
			return (string.IsNullOrEmpty(args) ? parameter.Trim() : args + " " + parameter.Trim());
		}

		// Asks (or, opted in, rebakes) once per stale episode, then only warns; a save that wrote a lightmap needs neither
		internal static StaleLightmapAction StaleLightmapNotice(bool savedwithlightmap, bool stale, bool asked, bool autobake)
		{
			if(savedwithlightmap || !stale) return StaleLightmapAction.None;
			if(asked) return StaleLightmapAction.Warn;
			return (autobake ? StaleLightmapAction.Rebake : StaleLightmapAction.Ask);
		}

		// The stale-lightmap question offers a rebake only when the map can be baked; testing without is the default
		internal static string StaleLightmapQuestion(bool canbake, out MessageBoxButtons buttons, out MessageBoxDefaultButton defaultbutton)
		{
			buttons = (canbake ? MessageBoxButtons.YesNoCancel : MessageBoxButtons.OKCancel);
			defaultbutton = (canbake ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1);
			return STALE_LIGHTMAP_QUESTION + (canbake ? STALE_LIGHTMAP_REBAKE : STALE_LIGHTMAP_NO_REBAKE);
		}

		// This takes the unconverted parameters (with placeholders) and converts it
		// to parameters with full paths, names and numbers where placeholders were put.
		// The tempfile must be the full path and filename to the PWAD file to test.
		public string ConvertParameters(string parameters, int skill, bool shortpaths, bool linuxpaths)
		{
			string outp = parameters;
			DataLocation iwadloc;
			string p_wp = "", p_wf = "";
			string p_ap = "", p_apq = "";
			string p_l1 = "", p_l2 = "";
			string p_nm = "";
			string f = tempwad;
			
			// Make short path if needed
			if(shortpaths) f = General.GetShortFilePath(f);
			else if (linuxpaths) f = General.GetLinuxFilePath(f);
			// Find the first IWAD file
			if(General.Map.Data.FindFirstIWAD(out iwadloc))
			{
				// %WP and %WF result in IWAD file
				p_wp = iwadloc.location;
				p_wf = Path.GetFileName(p_wp);
				if(shortpaths)
				{
					p_wp = General.GetShortFilePath(p_wp);
					p_wf = General.GetShortFilePath(p_wf);
				}
				else if (linuxpaths)
				{
					p_wp = General.GetLinuxFilePath(p_wp);
					p_wf = General.GetLinuxFilePath(p_wf);
				}
			}
			
			// Make a list of all data locations, including map location
			DataLocationList locations = DataLocationList.Combined(General.Map.ConfigSettings.Resources, General.Map.Options.Resources);

			//mxd. General.Map.FilePathName will be empty when a newly created map was not saved yet.
			if(!string.IsNullOrEmpty(General.Map.FilePathName))
			{
				DataLocation maplocation = new DataLocation(DataLocation.RESOURCE_WAD, General.Map.FilePathName, false, false, false, null);
				locations.Remove(maplocation); //If maplocation was already added as a resource, make sure it's singular and is last in the list
				locations.Add(maplocation); 
			}

			// Go for all data locations
			foreach(DataLocation dl in locations)
			{
				// Location not the IWAD file?
				if((dl.location != iwadloc.location))
				{
					// Location not included?
					if(!dl.notfortesting)
					{
						// Add to string of files
						if(shortpaths)
						{
							p_ap += General.GetShortFilePath(dl.location) + " ";
							p_apq += "\"" + General.GetShortFilePath(dl.location) + "\" ";
						}
						else if (linuxpaths)
						{
							p_ap += General.GetLinuxFilePath(dl.location) + " ";
							p_apq += "\"" + General.GetLinuxFilePath(dl.location) + "\" ";
						}
						else
						{
							p_ap += dl.location + " ";
							p_apq += "\"" + dl.location + "\" ";
						}
					}
				}
			}

			// Trim last space from resource file locations
			p_ap = p_ap.TrimEnd(' ');
			p_apq = p_apq.TrimEnd(' ');

			// Try finding the L1 and L2 numbers from the map name
			string numstr = "";
			bool first = true;
			foreach(char c in General.Map.Options.CurrentName)
			{
				// Character is a number?
				if(Configuration.NUMBERS.IndexOf(c) > -1)
				{
					// Include it
					numstr += c;
				}
				else
				{
					// Store the number if we found one
					if(numstr.Length > 0)
					{
						int num;
						int.TryParse(numstr, out num);
						if(first) p_l1 = num.ToString(); else p_l2 = num.ToString();
						numstr = "";
						first = false;
					}
				}
			}
			
			// Store the number if we found one
			if(numstr.Length > 0)
			{
				int num;
				int.TryParse(numstr, out num);
				if(first) p_l1 = num.ToString(); else p_l2 = num.ToString();
			}

			// No monsters?
			if(!General.Settings.TestMonsters) p_nm = "-nomonsters";
			
			// Make sure all our placeholders are in uppercase
			outp = outp.Replace("%f", "%F");
			outp = outp.Replace("%wp", "%WP");
			outp = outp.Replace("%wf", "%WF");
			outp = outp.Replace("%wP", "%WP");
			outp = outp.Replace("%wF", "%WF");
			outp = outp.Replace("%Wp", "%WP");
			outp = outp.Replace("%Wf", "%WF");
			outp = outp.Replace("%l1", "%L1");
			outp = outp.Replace("%l2", "%L2");
			outp = outp.Replace("%l", "%L");
			outp = outp.Replace("%ap", "%AP");
			outp = outp.Replace("%aP", "%AP");
			outp = outp.Replace("%Ap", "%AP");
			outp = outp.Replace("%s", "%S");
			outp = outp.Replace("%nM", "%NM");
			outp = outp.Replace("%Nm", "%NM");
			outp = outp.Replace("%nm", "%NM");
			
			// Replace placeholders with actual values
			outp = outp.Replace("%F", f);
			outp = outp.Replace("%WP", p_wp);
			outp = outp.Replace("%WF", p_wf);
			outp = outp.Replace("%L1", p_l1);
			outp = outp.Replace("%L2", p_l2);
			outp = outp.Replace("%L", General.Map.Options.CurrentName);
			outp = outp.Replace("\"%AP\"", p_apq);
			outp = outp.Replace("%AP", p_ap);
			outp = outp.Replace("%S", skill.ToString());
			outp = outp.Replace("%NM", p_nm);
			
			// Return result
			return outp;
		}

		#endregion

		#region ================== Test

		// This saves the map to a temporary file and launches a test
		[BeginAction("testmap")]
		public void Test()
		{
			TestAtSkill(General.Map.ConfigSettings.TestSkill, false);
		}

		//mxd
		[BeginAction("testmapfromview")]
		public void TestFromView() 
		{
			TestAtSkill(General.Map.ConfigSettings.TestSkill, true);
		}
		
		// This saves the map to a temporary file and launches a test with the given skill
		public void TestAtSkill(int skill) { TestAtSkill(skill, false); }
		public void TestAtSkill(int skill, bool testfromcurrentposition)
		{
			if(!General.Editing.Mode.OnMapTestBegin(testfromcurrentposition)) return; //mxd
			
			Cursor oldcursor = Cursor.Current;

			// Check if configuration is OK
			if(string.IsNullOrEmpty(General.Map.ConfigSettings.TestProgram) || !File.Exists(General.Map.ConfigSettings.TestProgram))
			{
				//mxd. Let's be more precise
				string message;
				if(General.Map.ConfigSettings.TestProgram == "")
					message = "Your test program is not set for the current game configuration";
				else
					message = "Current test program has invalid path";
				
				// Show message
				Cursor.Current = Cursors.Default;
				DialogResult result = General.ShowWarningMessage(message + ". Would you like to set up your test program now?", MessageBoxButtons.YesNo);
				if(result == DialogResult.Yes)
				{
					// Show game configuration on the right page
					General.MainWindow.ShowConfigurationPage(2);
				}
				return;
			}

			// No custom parameters?
			if(!General.Map.ConfigSettings.CustomParameters)
			{
				// Set parameters to the default ones
				General.Map.ConfigSettings.TestParameters = General.Map.Config.TestParameters;
				General.Map.ConfigSettings.TestShortPaths = General.Map.Config.TestShortPaths;
				General.Map.ConfigSettings.TestLinuxPaths = General.Map.Config.TestLinuxPaths;
			}
			
			// Remove temporary file
			if(File.Exists(tempwad) && !processes.ContainsValue(tempwad))
			{
				try { File.Delete(tempwad); }
				catch { }
			}
			
			// Save map to temporary file
			Cursor.Current = Cursors.WaitCursor;
			bool rebake = false, autorebake = false;
			General.Plugins.OnMapSaveBegin(SavePurpose.Testing);
			if(General.Map.SaveMap(tempwad, SavePurpose.Testing))
			{
				bool canceled = false;

				// No compiler errors?
				if (General.Map.Errors.Count == 0)
				{
					// The map had a bake this save could not write
					StaleLightmapAction stalenotice = StaleLightmapNotice(General.Map.SavedWithLightmap, General.Map.LightmapStale, General.Map.LightmapStaleAsked || retestingafterbake, General.Map.AutoBakesOnTest());
					if (stalenotice == StaleLightmapAction.Rebake)
					{
						// Marked first, so a failed or cancelled auto-bake tests without it and only warns
						General.Map.MarkLightmapStaleAsked();
						General.WriteLogLine("Stale lightmap on Test: auto-bake");
						rebake = autorebake = canceled = true;
						Cursor.Current = oldcursor;
					}
					else if (stalenotice == StaleLightmapAction.Ask)
					{
						General.Map.MarkLightmapStaleAsked();
						MessageBoxButtons buttons;
						MessageBoxDefaultButton defaultbutton;
						string question = StaleLightmapQuestion(General.Map.LightmapBakeUnavailable() == null, out buttons, out defaultbutton);
						DialogResult answer = General.ShowWarningMessage(question, buttons, defaultbutton);
						if (answer == DialogResult.Yes)
						{
							General.WriteLogLine("Stale lightmap on Test: rebake");
							rebake = true;
							canceled = true;
						}
						else if (answer == DialogResult.No || answer == DialogResult.OK)
						{
							General.WriteLogLine("Stale lightmap on Test: test without");
						}
						else
						{
							General.WriteLogLine("Stale lightmap on Test: cancel");
							General.MainWindow.DisplayStatus(StatusType.Warning, "Testing was canceled.");
							canceled = true;
						}
						if (canceled) Cursor.Current = oldcursor;
					}

					// Check if there's a pre command to run, and try to execute it
					if (!canceled && !string.IsNullOrWhiteSpace(General.Map.Options.TestPreCommand.Commands))
					{
						if (!General.Map.ExecuteExternalCommand(General.Map.Options.TestPreCommand, tempwad))
						{
							General.WriteLogLine("Testing was canceled when executing the testing pre command.");

							// Reset status
							General.MainWindow.DisplayStatus(StatusType.Warning, "Testing was canceled.");
							canceled = true;
						}
					}

					if (!canceled)
					{
						// Make arguments
						string args = ConvertParameters(General.Map.ConfigSettings.TestParameters, skill, General.Map.ConfigSettings.TestShortPaths, General.Map.ConfigSettings.TestLinuxPaths);

						// Add additional parameters
						if (!string.IsNullOrWhiteSpace(General.Map.ConfigSettings.TestAdditionalParameters))
							args += " " + General.Map.ConfigSettings.TestAdditionalParameters;

						// A baked lightmap only loads when the engine is told to
						args = AddLightmapParameter(args, General.Map.SavedWithLightmap, General.Map.Config.LightmapTestParameter);

						// Setup process info
						ProcessStartInfo processinfo = new ProcessStartInfo();
						processinfo.Arguments = args;
						processinfo.FileName = General.Map.ConfigSettings.TestProgram;
						processinfo.CreateNoWindow = false;
						processinfo.ErrorDialog = false;
						processinfo.UseShellExecute = true;
						processinfo.WindowStyle = ProcessWindowStyle.Normal;
						processinfo.WorkingDirectory = Path.GetDirectoryName(processinfo.FileName);

						// Output info
						General.WriteLogLine("Running test program: " + processinfo.FileName);
						General.WriteLogLine("Program parameters:  " + processinfo.Arguments);
						if (stalenotice != StaleLightmapAction.None)
						{
							General.WriteLogLine(STALE_LIGHTMAP_WARNING);
							General.MainWindow.DisplayStatus(StatusType.Warning, STALE_LIGHTMAP_WARNING);
						}
						else General.MainWindow.DisplayStatus(StatusType.Info, "Launching " + processinfo.FileName + "...");

						try
						{
							// Start the program
							Process process = Process.Start(processinfo);
							process.EnableRaisingEvents = true; //mxd
							process.Exited += ProcessOnExited; //mxd
							processes.Add(process, tempwad); //mxd
							Cursor.Current = oldcursor; //mxd
						}
						catch (Exception e)
						{
							string additionaltext = string.Empty;

							if (e is System.ComponentModel.Win32Exception w32e)
							{
								additionaltext = string.Format(additionalexceptiontext.TryGetValue(w32e.NativeErrorCode, out var tmp) ? ("\n\n" + tmp) : string.Empty, General.Map.ConfigSettings.TestProgram);
							}

							// Unable to start the program
							General.ShowErrorMessage("Unable to start the test program, " + e.GetType().Name + ": " + e.Message + additionaltext, MessageBoxButtons.OK);
						}

						// Check if there's a post command to run, and try to execute it
						// TODO: currently modifying the commands is disabled since it'd have to be executed after the test program ends, which is not
						// the case in the current situation, since this code here is reached immediately after launching the test program
						/*
						if (!string.IsNullOrWhiteSpace(General.Map.Options.TestPostCommand.Commands))
						{
							if (!General.Map.ExecuteExternalCommand(General.Map.Options.TestPostCommand, tempwad))
							{
								General.WriteLogLine("Failed to execute the test post command successfully.");
							}
						}
						*/
					}
				}
				else
				{
					General.MainWindow.DisplayStatus(StatusType.Warning, "Unable to test the map due to script errors.");
				}
			}
			General.Plugins.OnMapSaveEnd(SavePurpose.Testing);
			General.Editing.Mode.OnMapTestEnd(testfromcurrentposition); //mxd

			// Only once the aborted test has unwound, so a moved player start is back before the bake copies the map
			// An auto-bake tests either way: after a failure the stale episode only warns, so F9 still launches
			if(rebake && (General.Map.ShowLightmapBakeForm(true, autorebake) || autorebake))
			{
				retestingafterbake = autorebake;
				try { TestAtSkill(skill, testfromcurrentposition); }
				finally { retestingafterbake = false; }
			}
		}

		//mxd
		private void TestingFinished(Process process) 
		{
			// Done
			TimeSpan deltatime = TimeSpan.FromTicks(process.ExitTime.Ticks - process.StartTime.Ticks);
			General.WriteLogLine("Testing with \"" + process.StartInfo.FileName + "\" has finished.");
			General.WriteLogLine("Run time: " + deltatime.TotalSeconds.ToString("###########0.00") + " seconds");

			//mxd. Remove from active processes list
			string closedtempfile = processes[process];
			processes.Remove(process);

			//mxd. Still have running engines?..
			if(processes.Count > 0)
			{
				// Remove temp file
				if(File.Exists(closedtempfile))
				{
					try { File.Delete(closedtempfile); }
					catch { }
				}
				return; 
			}
			
			General.MainWindow.DisplayReady();

			if(General.Map != null)
			{
				// Device reset may be needed...
				if(General.Editing.Mode is ClassicMode)
				{
					General.MainWindow.RedrawDisplay();
				}
			}

			General.MainWindow.FocusDisplay();
		}

		//mxd
		private void ProcessOnExited(object sender, EventArgs e)
		{
			General.MainWindow.Invoke(new EngineExitedCallback(TestingFinished), new[] { sender });
		}

		/// <summary>
		/// Initializes the temporary file used for testing.
		/// </summary>
		/// <param name="manager">The MapManager instance.</param>
		private void InitializeTempFile(MapManager manager)
		{
			// Make new empty temp file
			tempwad = General.MakeTempFilename(manager.TempPath, "wad");

			// General.GetShortFilePath, which is used when the "use short paths and file name" option is set, uses
			// a Win32 function that returns an empty string when the file doesn't exist, so make sure it exists.
			File.Create(tempwad).Dispose();
		}


		#endregion
	}
}
