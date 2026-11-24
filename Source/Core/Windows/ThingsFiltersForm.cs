
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
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Map;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	internal partial class ThingsFiltersForm : DelayedForm
	{
		#region ================== Structs

		private class ThingCategoryData //mxd
		{
			public ThingCategory Category;
			public string FullName;

			public ThingCategoryData(ThingCategory category, string fullname)
			{
				Category = category;
				FullName = fullname;
			}

			public override string ToString() 
			{
				return FullName;
			}
		}

		// A combo box entry carrying a value under a display title
		private class RuleItem
		{
			public readonly object Value;
			public readonly string Title;

			public RuleItem(object value, string title)
			{
				Value = value;
				Title = title;
			}

			public override string ToString()
			{
				return Title;
			}
		}

		#endregion

		#region ================== Variables

		private bool settingup;
		private bool settingrule;

		#endregion
		
		#region ================== Constructor

		// Constructor
		public ThingsFiltersForm()
		{
			settingup = true;
			
			// Initialize
			InitializeComponent();
			
			// Fill types list
			List<ThingTypeInfo> thingtypes = new List<ThingTypeInfo>(General.Map.Data.ThingTypes);
			INumberedTitle[] typeitems = new INumberedTitle[thingtypes.Count];
			for(int i = 0; i < thingtypes.Count; i++) typeitems[i] = thingtypes[i];
			filtertype.AddInfo(typeitems);
			
			// Fill the categories combobox
			filtercategory.Items.Add("(any category)");

			//mxd. Add ThingCategories with subcategories
			AddThingFilterCategories(General.Map.Data.ThingCategories, string.Empty);
			foreach(object c in filtercategory.Items)
				if(c is ThingCategoryData) rulevaluecategory.Items.Add(c);

			// Rule subjects
			foreach(ThingsFilterRuleSubject s in ThingsFilterRule.AllSubjects)
				rulesubject.Items.Add(new RuleItem(s, ThingsFilterRule.SubjectTitle(s)));

			// Fill actions list
			filteraction.GeneralizedCategories = General.Map.Config.GenActionCategories;
			filteraction.AddInfo(General.Map.Config.SortedLinedefActions.ToArray());
			
			// Initialize custom fields editor
			if(General.Map.FormatInterface.HasCustomFields) //mxd
			{
				fieldslist.ListNoFixedFields();
				fieldslist.Setup("thing");
			}

			// Fill checkboxes list
			foreach(KeyValuePair<string, string> flag in General.Map.Config.ThingFlags)
			{
				CheckBox box = filterfields.Add(flag.Value, flag.Key);
				box.ThreeState = true;
				box.CheckStateChanged += filterfield_Check;
			}
			
			// Fill list of filters
			foreach(ThingsFilter f in General.Map.ConfigSettings.ThingsFilters)
			{
				// Make a copy (we don't want to modify the filters until OK is clicked)
				ThingsFilter nf = new ThingsFilter(f);
				
				// Make item in list
				ListViewItem item = new ListViewItem(nf.Name);
				item.Tag = nf;
				item.ImageIndex = (nf.IsValid() ? -1 : 0); //mxd
				listfilters.Items.Add(item);
				
				// Select item if this is the current filter
				if(General.Map.ThingsFilter == f) item.Selected = true;
			}
			
			// Sort the list
			listfilters.Sort();
			ValidateAllFilters();

			// Map format specific fields
			filterzheight.Visible = General.Map.FormatInterface.HasThingHeight;
			labelzheight.Visible = General.Map.FormatInterface.HasThingHeight;
			argumentspanel.Visible = General.Map.FormatInterface.HasActionArgs;
			labeltag.Visible = General.Map.FormatInterface.HasThingTag;
			filtertag.Visible = General.Map.FormatInterface.HasThingTag;
			if(!General.Map.FormatInterface.HasCustomFields)
				tabs.TabPages.Remove(tabcustom);
			if(!General.Map.FormatInterface.HasThingAction && !General.Map.FormatInterface.HasThingTag)
				tabs.TabPages.Remove(tabaction);
			
			// Done
			settingup = false;
		}

		//mxd. This recursively adds ThingCategories
		private void AddThingFilterCategories(List<ThingCategory> list, string fullname) 
		{
			foreach(ThingCategory cat in list)
			{
				string catname = (string.IsNullOrEmpty(fullname) ? cat.Title : fullname + " / " + cat.Title);
				filtercategory.Items.Add(new ThingCategoryData(cat, catname));
				AddThingFilterCategories(cat.Children, catname);
			}
		}

		//mxd
		private void ValidateFilter(ListViewItem item, ThingsFilter f)
		{
			int imageindex = (f.IsValid() && !f.HasInvalidRules(DialogContext()) ? -1 : 0);
			if(item.ImageIndex != imageindex)
			{
				item.ImageIndex = imageindex;
				listfilters.Invalidate();
			}
		}

		// Rule validity depends on other filters' names and rules
		private void ValidateAllFilters()
		{
			foreach(ListViewItem item in listfilters.Items) ValidateFilter(item, (ThingsFilter)item.Tag);
		}

		// Resolves "Matches filter" rules against the filters being edited
		private ThingsFilterContext DialogContext()
		{
			return ThingsFilterContext.FromMap(DialogFilters());
		}

		private List<ThingsFilter> DialogFilters()
		{
			List<ThingsFilter> filters = new List<ThingsFilter>(listfilters.Items.Count);
			foreach(ListViewItem item in listfilters.Items) filters.Add((ThingsFilter)item.Tag);
			return filters;
		}

		private HashSet<string> FilterNames(ThingsFilter except)
		{
			HashSet<string> names = new HashSet<string>();
			foreach(ThingsFilter f in DialogFilters())
				if(f != except) names.Add(f.Name);
			return names;
		}

		#endregion

		#region ================== Management

		// OK clicked
		private void apply_Click(object sender, EventArgs e)
		{
			//mxd. Store current filter name
			string currentfiltername = string.Empty;
			if(!(General.Map.ThingsFilter is NullThingsFilter)) currentfiltername = General.Map.ThingsFilter.Name;
			
			// Clear all filters and add the new ones
			General.Map.ConfigSettings.ThingsFilters.Clear();
			ThingsFilter currentfilter = new NullThingsFilter(); //mxd
			foreach(ListViewItem item in listfilters.Items)
			{
				ThingsFilter f = item.Tag as ThingsFilter; //mxd
				if(!string.IsNullOrEmpty(currentfiltername) && f.Name == currentfiltername) //mxd
					currentfilter = f; 
				General.Map.ConfigSettings.ThingsFilters.Add(f);
			}
				
			
			// Update stuff
			General.Map.ChangeThingFilter(currentfilter);
			General.MainWindow.UpdateThingsFilters();
			General.Map.ConfigSettings.Changed = true;

			// Log problems made in the dialog
			foreach(ThingsFilter f in General.Map.ConfigSettings.ThingsFilters) f.Validate();

			// Close
			this.DialogResult = DialogResult.OK;
			this.Close();
		}

		// Cancel clicked
		private void cancel_Click(object sender, EventArgs e)
		{
			// Close
			this.DialogResult = DialogResult.Cancel;
			this.Close();
		}

		// New Filter clicked
		private void addfilter_Click(object sender, EventArgs e)
		{
			ThingsFilter newf = new ThingsFilter();
			newf.Name = ThingsFilter.UniqueName(newf.Name, FilterNames(null));

			// Make item in list and select it
			ListViewItem item = new ListViewItem(newf.Name);
			item.Tag = newf;
			listfilters.Items.Add(item);
			item.Selected = true;
			ValidateAllFilters();

			// Focus on the name field
			//filtername.Focus();
			//filtername.SelectAll();
		}

		// Delete Selected clicked
		private void deletefilter_Click(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Ask first when other filters' rules use this one
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				List<string> users = ReferencingFilters(f);
				if(users.Count > 0)
				{
					string text = "These filters have a \"Matches filter\" rule using \"" + f.Name + "\":\n\n" + string.Join("\n", users.ToArray())
						+ "\n\nDelete it anyway? Those rules will stop matching until you change them.";
					if(MessageBox.Show(this, text, "Delete Things Filter", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
				}

				// Remove item
				listfilters.Items.Remove(listfilters.SelectedItems[0]);
				ValidateAllFilters();
			}
		}

		// Names of the other filters with a "Matches filter" rule that resolves to this one
		private List<string> ReferencingFilters(ThingsFilter target)
		{
			List<string> users = new List<string>();
			ThingsFilterContext ctx = DialogContext();
			foreach(ThingsFilter f in DialogFilters())
			{
				if(f == target) continue;
				foreach(ThingsFilterRule r in f.Rules)
				{
					if(r.Subject == ThingsFilterRuleSubject.MatchesFilter && ctx.Resolve(r.Key) == target)
					{
						users.Add(f.Name);
						break;
					}
				}
			}
			return users;
		}
		
		// Item selected
		private void listfilters_SelectedIndexChanged(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;

				// Enable settings
				settingup = true;
				deletefilter.Enabled = true;
				filtergroup.Enabled = true;
				
				// Show name
				filtername.Text = f.Name;

				//mxd. Set display mode
				filtermode.SelectedIndex = (int)f.DisplayMode;

				//mxd. Invert?
				invert.Checked = f.Invert;
				
				// Properties
				foreach(object c in filtercategory.Items)
				{
					ThingCategoryData tc = (c as ThingCategoryData); //mxd
					if((tc != null) && (tc.Category.Name == f.CategoryName)) filtercategory.SelectedItem = tc;
				}
				if(filtercategory.SelectedIndex == -1) filtercategory.SelectedIndex = 0;
				includesubcategories.Checked = f.IncludeSubcategories;
				includesubcategories.Enabled = (filtercategory.SelectedIndex > 0);

				if(f.ThingType > -1)
					filtertype.Value = f.ThingType;
				else
					filtertype.Empty = true;
				
				if(f.ThingAngle > -1)
					filterangle.Text = f.ThingAngle.ToString();
				else
					filterangle.Text = "";
					
				if(f.ThingZHeight > int.MinValue)
					filterzheight.Text = f.ThingZHeight.ToString();
				else
					filterzheight.Text = "";
				
				// Action
				if(f.ThingAction > -1)
					filteraction.Value = f.ThingAction;
				else
					filteraction.Empty = true;
				
				if(f.ThingArgs[0] > -1) arg0.SetValue(f.ThingArgs[0]); else arg0.ClearValue();
				if(f.ThingArgs[1] > -1) arg1.SetValue(f.ThingArgs[1]); else arg1.ClearValue();
				if(f.ThingArgs[2] > -1) arg2.SetValue(f.ThingArgs[2]); else arg2.ClearValue();
				if(f.ThingArgs[3] > -1) arg3.SetValue(f.ThingArgs[3]); else arg3.ClearValue();
				if(f.ThingArgs[4] > -1) arg4.SetValue(f.ThingArgs[4]); else arg4.ClearValue();
				
				if(f.ThingTag > -1)
					filtertag.Text = f.ThingTag.ToString();
				else
					filtertag.Text = "";
				
				// Flags
				foreach(CheckBox b in filterfields.Checkboxes)
				{
					// Field name forbidden?
					if(f.ForbiddenFields.Contains(b.Tag.ToString()))
					{
						b.CheckState = CheckState.Unchecked;
					}
					// Field name required?
					else if(f.RequiredFields.Contains(b.Tag.ToString()))
					{
						b.CheckState = CheckState.Checked;
					}
					else
					{
						b.CheckState = CheckState.Indeterminate;
					}
				}
				
				// Custom fields
				if(General.Map.FormatInterface.HasCustomFields) //mxd 
				{ 
					fieldslist.ClearFields();
					fieldslist.Setup("thing");
					fieldslist.SetValues(f.ThingCustomFields, true);
				}

				ShowRules(f);

				// Done
				settingup = false;
			}
			else
			{
				// Disable filter settings
				deletefilter.Enabled = false;
				filtergroup.Enabled = false;
				filtername.Text = "";
				filtercategory.SelectedIndex = -1;
				foreach(CheckBox c in filterfields.Checkboxes) c.CheckState = CheckState.Indeterminate;
				ShowRules(null);
			}
		}

		// Help
		private void ThingsFiltersForm_HelpRequested(object sender, HelpEventArgs hlpevent)
		{
			General.ShowHelp("w_thingsfilters.html");
			hlpevent.Handled = true;
		}

		#endregion	

		#region ================== Filter Settings

		// Category changed
		private void filtercategory_SelectedIndexChanged(object sender, EventArgs e)
		{
			// Anything selected?
			if(!settingup && listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				
				// Category selected
				if((filtercategory.SelectedIndex > -1) && (filtercategory.SelectedItem is ThingCategoryData))
				{
					// Set new category name
					f.CategoryName = ((ThingCategoryData)filtercategory.SelectedItem).Category.Name; //mxd
				}
				else
				{
					// Unset category name
					f.CategoryName = "";
				}

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
			includesubcategories.Enabled = (filtercategory.SelectedIndex > 0);
		}

		private void includesubcategories_CheckedChanged(object sender, EventArgs e)
		{
			if(!settingup && listfilters.SelectedItems.Count > 0)
				((ThingsFilter)listfilters.SelectedItems[0].Tag).IncludeSubcategories = includesubcategories.Checked;
		}
		
		// Rename filter
		private void filtername_Validating(object sender, CancelEventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;

				// Name changed?
				if(string.IsNullOrEmpty(filtername.Text)) filtername.Text = ThingsFilter.DEFAULT_NAME; //mxd
				if(f.Name != filtername.Text)
				{
					// Names must be unique, so "Matches filter" rules can find them
					string oldname = f.Name;
					HashSet<string> others = FilterNames(f);
					filtername.Text = ThingsFilter.UniqueName(filtername.Text, others);

					// Update name
					f.Name = filtername.Text;
					listfilters.SelectedItems[0].Text = f.Name;
					listfilters.Sort();

					// Rules that used the old name follow the rename, unless another filter still has it
					if(!others.Contains(oldname))
					{
						foreach(ThingsFilter other in DialogFilters())
							foreach(ThingsFilterRule r in other.Rules)
								if(r.Subject == ThingsFilterRuleSubject.MatchesFilter && r.Key == oldname) r.Key = f.Name;
					}
					ValidateAllFilters();
					ShowRules(f);
				}
			}
		}

		//mxd
		private void filtername_Enter(object sender, EventArgs e)
		{
			if(filtername.Text == ThingsFilter.DEFAULT_NAME) filtername.Text = string.Empty;
		}

		// Field clicked
		private void filterfield_Check(object sender, EventArgs e)
		{
			// Get the checkbox
			CheckBox box = (sender as CheckBox);
			
			// Not setting up?
			if(!settingup)
			{
				// Anything selected?
				if(listfilters.SelectedItems.Count > 0)
				{
					// Get selected filter
					ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
					
					switch(box.CheckState)
					{
						// New state is required?
						case CheckState.Checked:
							f.ForbiddenFields.Remove(box.Tag.ToString());
							if(!f.RequiredFields.Contains(box.Tag.ToString())) f.RequiredFields.Add(box.Tag.ToString());
							break;
						// New state is forbidden?
						case CheckState.Unchecked:
							f.RequiredFields.Remove(box.Tag.ToString());
							if(!f.ForbiddenFields.Contains(box.Tag.ToString())) f.ForbiddenFields.Add(box.Tag.ToString());
							break;
						default:
							f.ForbiddenFields.Remove(box.Tag.ToString());
							f.RequiredFields.Remove(box.Tag.ToString());
							break;
					}

					ValidateFilter(listfilters.SelectedItems[0], f); //mxd
				}
			}
		}
		
		private void filtertype_ValueChanges(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				if(filtertype.Empty)
					f.ThingType = -1;
				else
					f.ThingType = filtertype.GetValue();

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
		}
		
		private void browsetype_Click(object sender, EventArgs e)
		{
			filtertype.Value = ThingBrowserForm.BrowseThing(this, filtertype.Value);
		}
		
		private void filterangle_WhenTextChanged(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				f.ThingAngle = filterangle.GetResult(-1);

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
		}
		
		private void browseangle_Click(object sender, EventArgs e)
		{
			AngleForm af = new AngleForm();
			af.Setup(filterangle.GetResult(-1));
			if(af.ShowDialog() == DialogResult.OK)
				filterangle.Text = af.Value.ToString();
		}
		
		private void filterzheight_WhenTextChanged(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				f.ThingZHeight = filterzheight.GetResult(int.MinValue);

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
		}

		private void filteraction_ValueChanges(object sender, EventArgs e)
		{
			int showaction = 0;

			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				if(filteraction.Empty)
					f.ThingAction = -1;
				else
					f.ThingAction = filteraction.GetValue();

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
			
			// Only when line type is known, otherwise use the thing arguments
			if(General.Map.Config.LinedefActions.ContainsKey(filteraction.Value)) showaction = filteraction.Value;
			ArgumentInfo[] arginfo = General.Map.Config.LinedefActions[showaction].Args;
			
			// Change the argument descriptions
			arg0label.Text = arginfo[0].Title + ":";
			arg1label.Text = arginfo[1].Title + ":";
			arg2label.Text = arginfo[2].Title + ":";
			arg3label.Text = arginfo[3].Title + ":";
			arg4label.Text = arginfo[4].Title + ":";
			arg0label.Enabled = arginfo[0].Used;
			arg1label.Enabled = arginfo[1].Used;
			arg2label.Enabled = arginfo[2].Used;
			arg3label.Enabled = arginfo[3].Used;
			arg4label.Enabled = arginfo[4].Used;
			arg0.ForeColor = (arg0label.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
			arg1.ForeColor = (arg1label.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
			arg2.ForeColor = (arg2label.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
			arg3.ForeColor = (arg3label.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
			arg4.ForeColor = (arg4label.Enabled ? SystemColors.WindowText : SystemColors.GrayText);
			arg0.Setup(arginfo[0]);
			arg1.Setup(arginfo[1]);
			arg2.Setup(arginfo[2]);
			arg3.Setup(arginfo[3]);
			arg4.Setup(arginfo[4]);

			// Argument rules are labelled with the action's argument names
			if(!settingup) RefreshRuleRows();
		}
		
		private void browseaction_Click(object sender, EventArgs e)
		{
			filteraction.Value = ActionBrowserForm.BrowseAction(this, filteraction.Value);
		}
		
		private void filtertag_WhenTextChanged(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				f.ThingTag = filtertag.GetResult(-1);

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
		}

		private void arg_Validated(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				
				int index;
				int.TryParse((sender as Control).Tag.ToString(), out index);
				ArgumentBox filterarg = (sender as ArgumentBox);
				f.ThingArgs[index] = filterarg.GetResult(-1);

				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
		}
		
		private void fieldslist_Validated(object sender, EventArgs e)
		{
			// Anything selected?
			if(listfilters.SelectedItems.Count > 0)
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				fieldslist.Apply(f.ThingCustomFields);

				//mxd. Validate filter
				ValidateFilter(listfilters.SelectedItems[0], f); //mxd
			}
		}

		//mxd
		private void invert_CheckedChanged(object sender, EventArgs e) 
		{
			// Anything selected?
			if(!settingup && listfilters.SelectedItems.Count > 0) 
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				f.Invert = invert.Checked;
			}
		}

		//mxd
		private void filtermode_SelectedIndexChanged(object sender, EventArgs e)
		{
			// Anything selected?
			if(!settingup && listfilters.SelectedItems.Count > 0 && filtermode.SelectedIndex != -1) 
			{
				// Get selected filter
				ThingsFilter f = listfilters.SelectedItems[0].Tag as ThingsFilter;
				f.DisplayMode = (ThingsFilterDisplayMode)filtermode.SelectedIndex;
			}
		}

		#endregion

		#region ================== Rules

		private ThingsFilter SelectedFilter { get { return (listfilters.SelectedItems.Count > 0 ? listfilters.SelectedItems[0].Tag as ThingsFilter : null); } }

		private ThingsFilterRule SelectedRule
		{
			get
			{
				ThingsFilter f = SelectedFilter;
				int i = SelectedRuleIndex;
				return (f != null && i >= 0 && i < f.Rules.Count ? f.Rules[i] : null);
			}
		}

		private int SelectedRuleIndex { get { return (rulesgrid.SelectedRows.Count > 0 ? rulesgrid.SelectedRows[0].Index : -1); } }

		// Fills the grid from a filter (or clears it) and selects the given row
		private void ShowRules(ThingsFilter f, int select = 0)
		{
			settingrule = true;
			rulesgrid.Rows.Clear();
			if(f != null)
			{
				rulematchmode.SelectedIndex = (int)f.MatchMode;
				foreach(ThingsFilterRule r in f.Rules) rulesgrid.Rows.Add();
				RefreshRuleRows();
			}
			else
			{
				rulematchmode.SelectedIndex = -1;
			}
			rulesgrid.ClearSelection();
			if(f != null && f.Rules.Count > 0)
			{
				select = Math.Max(0, Math.Min(select, f.Rules.Count - 1));
				rulesgrid.Rows[select].Selected = true;
				rulesgrid.CurrentCell = rulesgrid.Rows[select].Cells[0];
			}
			settingrule = false;
			LoadRuleEditor();
		}

		private void RefreshRuleRows()
		{
			ThingsFilter f = SelectedFilter;
			if(f == null) return;
			ThingsFilterContext ctx = DialogContext();
			for(int i = 0; i < f.Rules.Count && i < rulesgrid.Rows.Count; i++)
			{
				ThingsFilterRule r = f.Rules[i];
				DataGridViewRow row = rulesgrid.Rows[i];
				row.Cells[0].Value = ThingsFilterRule.SubjectTitle(r.Subject);
				row.Cells[1].Value = KeyTitle(f, r);
				row.Cells[2].Value = ThingsFilterRule.OperatorTitle(r.Subject, r.Operator);
				row.Cells[3].Value = ValueTitle(r);

				string error = f.RuleError(r, ctx);
				string tip = "";
				Color color = SystemColors.WindowText;
				if(!ctx.IsAvailable(r))
				{
					color = SystemColors.GrayText;
					tip = "Not available in this map format; this rule is skipped";
				}
				else if(error != null)
				{
					color = Color.Red;
					tip = error;
				}
				row.DefaultCellStyle.ForeColor = color;
				row.DefaultCellStyle.SelectionForeColor = (color == SystemColors.WindowText ? SystemColors.HighlightText : color);
				foreach(DataGridViewCell cell in row.Cells) cell.ToolTipText = tip;
			}
		}

		private static string KeyTitle(ThingsFilter f, ThingsFilterRule r)
		{
			switch(r.Subject)
			{
				case ThingsFilterRuleSubject.Flag:
					string title;
					return (General.Map.Config.ThingFlags.TryGetValue(r.Key, out title) ? title : r.Key);
				case ThingsFilterRuleSubject.Arg:
					int index;
					if(int.TryParse(r.Key, out index)) return ArgTitle(f, index);
					return r.Key;
				case ThingsFilterRuleSubject.Field:
				case ThingsFilterRuleSubject.MatchesFilter:
					return r.Key;
				default:
					return "";
			}
		}

		// "Arg 2", or the filter's action's name for that argument
		private static string ArgTitle(ThingsFilter f, int index)
		{
			string title = "Arg " + index;
			if(index >= 0 && index < Thing.NUM_ARGS && f.ThingAction > 0 && General.Map.Config.LinedefActions.ContainsKey(f.ThingAction))
			{
				ArgumentInfo info = General.Map.Config.LinedefActions[f.ThingAction].Args[index];
				if(info.Used) title += ": " + info.Title;
			}
			return title;
		}

		private string ValueTitle(ThingsFilterRule r)
		{
			if(!ThingsFilterRule.UsesValue(r.Subject, r.Operator)) return "";
			if(r.Subject == ThingsFilterRuleSubject.Category)
			{
				ThingCategoryData data = FindCategory(r.Value);
				string title = (data != null ? data.FullName : r.Value);
				return (r.IncludeSubcategories ? title + " (and subcategories)" : title);
			}
			return r.Value;
		}

		private ThingCategoryData FindCategory(string name)
		{
			foreach(object o in rulevaluecategory.Items)
			{
				ThingCategoryData data = o as ThingCategoryData;
				if(data != null && string.Equals(data.Category.Name, name, StringComparison.OrdinalIgnoreCase)) return data;
			}
			return null;
		}

		// Shows the selected rule in the edit row
		private void LoadRuleEditor()
		{
			ThingsFilter f = SelectedFilter;
			ThingsFilterRule r = SelectedRule;
			settingrule = true;

			addrule.Enabled = (f != null);
			rulematchmode.Enabled = (f != null);
			removerule.Enabled = (r != null);
			moveruleup.Enabled = (r != null && SelectedRuleIndex > 0);
			moveruledown.Enabled = (r != null && SelectedRuleIndex < f.Rules.Count - 1);
			rulesubject.Enabled = rulekeylist.Enabled = rulekeytext.Enabled = ruleoperator.Enabled = (r != null);
			rulevaluetext.Enabled = rulevaluecategory.Enabled = rulesubcategories.Enabled = (r != null);

			if(r == null)
			{
				rulesubject.SelectedIndex = -1;
				rulekeylist.Items.Clear();
				rulekeylist.Visible = true;
				rulekeytext.Visible = false;
				ruleoperator.Items.Clear();
				rulevaluetext.Text = "";
				rulevaluetext.Visible = true;
				rulevaluecategory.Visible = false;
				rulesubcategories.Visible = false;
				ruleerror.Text = "";
				settingrule = false;
				return;
			}

			rulesubject.SelectedIndex = Array.IndexOf(ThingsFilterRule.AllSubjects, r.Subject);

			// Key editor
			bool keytext = (r.Subject == ThingsFilterRuleSubject.Field);
			rulekeytext.Visible = keytext;
			rulekeylist.Visible = !keytext;
			rulekeylist.Items.Clear();
			if(keytext)
			{
				rulekeytext.Text = r.Key;
			}
			else
			{
				foreach(RuleItem item in KeyItems(f, r.Subject)) rulekeylist.Items.Add(item);
				if(r.Key.Length > 0 && FindItem(rulekeylist, r.Key) == -1)
					rulekeylist.Items.Add(new RuleItem(r.Key, r.Key + (r.Subject == ThingsFilterRuleSubject.MatchesFilter ? " (missing)" : " (unknown)")));
				rulekeylist.SelectedIndex = FindItem(rulekeylist, r.Key);
				rulekeylist.Enabled = ThingsFilterRule.UsesKey(r.Subject);
			}

			// Operators
			ruleoperator.Items.Clear();
			foreach(ThingsFilterRuleOperator o in ThingsFilterRule.OperatorsFor(r.Subject))
				ruleoperator.Items.Add(new RuleItem(o, ThingsFilterRule.OperatorTitle(r.Subject, o)));
			ruleoperator.SelectedIndex = FindItem(ruleoperator, r.Operator);

			// Value editor
			bool category = (r.Subject == ThingsFilterRuleSubject.Category);
			rulevaluecategory.Visible = category;
			rulesubcategories.Visible = category;
			rulevaluetext.Visible = !category;
			if(category)
			{
				ThingCategoryData data = FindCategory(r.Value);
				rulevaluecategory.SelectedItem = data;
				if(data == null) rulevaluecategory.SelectedIndex = -1;
				rulesubcategories.Checked = r.IncludeSubcategories;
			}
			else
			{
				if(rulevaluetext.Text != r.Value) rulevaluetext.Text = r.Value;
				rulevaluetext.Enabled = ThingsFilterRule.UsesValue(r.Subject, r.Operator);
			}

			string error = f.RuleError(r, DialogContext());
			ruleerror.Text = (error ?? "");
			settingrule = false;
		}

		private List<RuleItem> KeyItems(ThingsFilter f, ThingsFilterRuleSubject subject)
		{
			List<RuleItem> items = new List<RuleItem>();
			switch(subject)
			{
				case ThingsFilterRuleSubject.Flag:
					foreach(KeyValuePair<string, string> flag in General.Map.Config.ThingFlags)
						items.Add(new RuleItem(flag.Key, flag.Value));
					break;
				case ThingsFilterRuleSubject.Arg:
					for(int i = 0; i < Thing.NUM_ARGS; i++)
						items.Add(new RuleItem(i.ToString(), ArgTitle(f, i)));
					break;
				case ThingsFilterRuleSubject.MatchesFilter:
					HashSet<string> seen = new HashSet<string>();
					foreach(ThingsFilter other in DialogFilters())
						if(other != f && seen.Add(other.Name)) items.Add(new RuleItem(other.Name, other.Name));
					break;
			}
			return items;
		}

		private static int FindItem(ComboBox box, object value)
		{
			for(int i = 0; i < box.Items.Count; i++)
			{
				RuleItem item = box.Items[i] as RuleItem;
				if(item != null && Equals(item.Value, value)) return i;
			}
			return -1;
		}

		// After any rule edit
		private void RuleChanged()
		{
			RefreshRuleRows();
			ThingsFilter f = SelectedFilter;
			ThingsFilterRule r = SelectedRule;
			if(f != null && r != null)
			{
				string error = f.RuleError(r, DialogContext());
				ruleerror.Text = (error ?? "");
			}
			ValidateAllFilters();
		}

		private void rulesgrid_SelectionChanged(object sender, EventArgs e)
		{
			if(!settingrule) LoadRuleEditor();
		}

		private void rulematchmode_SelectedIndexChanged(object sender, EventArgs e)
		{
			ThingsFilter f = SelectedFilter;
			if(settingrule || f == null || rulematchmode.SelectedIndex < 0) return;
			f.MatchMode = (ThingsFilterMatchMode)rulematchmode.SelectedIndex;
		}

		private void addrule_Click(object sender, EventArgs e)
		{
			ThingsFilter f = SelectedFilter;
			if(f == null) return;
			f.Rules.Add(new ThingsFilterRule(ThingsFilterRuleSubject.Type, ThingsFilterRuleOperator.IsOneOf, "", ""));
			ShowRules(f, f.Rules.Count - 1);
			ValidateAllFilters();
			rulevaluetext.Focus();
		}

		private void removerule_Click(object sender, EventArgs e)
		{
			ThingsFilter f = SelectedFilter;
			int i = SelectedRuleIndex;
			if(f == null || i < 0 || i >= f.Rules.Count) return;
			f.Rules.RemoveAt(i);
			ShowRules(f, i);
			ValidateAllFilters();
		}

		private void moveruleup_Click(object sender, EventArgs e)
		{
			MoveRule(-1);
		}

		private void moveruledown_Click(object sender, EventArgs e)
		{
			MoveRule(1);
		}

		private void MoveRule(int delta)
		{
			ThingsFilter f = SelectedFilter;
			int i = SelectedRuleIndex;
			int j = i + delta;
			if(f == null || i < 0 || j < 0 || j >= f.Rules.Count) return;
			ThingsFilterRule r = f.Rules[i];
			f.Rules[i] = f.Rules[j];
			f.Rules[j] = r;
			ShowRules(f, j);
		}

		private void rulesubject_SelectedIndexChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			RuleItem item = rulesubject.SelectedItem as RuleItem;
			if(settingrule || r == null || item == null) return;
			ThingsFilterRuleSubject s = (ThingsFilterRuleSubject)item.Value;
			if(s == r.Subject) return;

			// A new subject starts from its first operator and a fresh key
			r.Subject = s;
			ThingsFilterRuleOperator[] ops = ThingsFilterRule.OperatorsFor(s);
			if(Array.IndexOf(ops, r.Operator) < 0) r.Operator = ops[0];
			r.Key = (s == ThingsFilterRuleSubject.Arg ? "0" : "");
			if(!ThingsFilterRule.UsesValue(s, r.Operator) || s == ThingsFilterRuleSubject.Category) r.Value = "";
			LoadRuleEditor();
			RuleChanged();
		}

		private void rulekeylist_SelectedIndexChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			RuleItem item = rulekeylist.SelectedItem as RuleItem;
			if(settingrule || r == null || item == null) return;
			r.Key = (string)item.Value;
			RuleChanged();
		}

		private void rulekeytext_TextChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			if(settingrule || r == null) return;
			r.Key = rulekeytext.Text.Trim();
			RuleChanged();
		}

		private void ruleoperator_SelectedIndexChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			RuleItem item = ruleoperator.SelectedItem as RuleItem;
			if(settingrule || r == null || item == null) return;
			r.Operator = (ThingsFilterRuleOperator)item.Value;
			rulevaluetext.Enabled = ThingsFilterRule.UsesValue(r.Subject, r.Operator);
			RuleChanged();
		}

		private void rulevaluetext_TextChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			if(settingrule || r == null) return;
			r.Value = rulevaluetext.Text;
			RuleChanged();
		}

		private void rulevaluecategory_SelectedIndexChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			ThingCategoryData data = rulevaluecategory.SelectedItem as ThingCategoryData;
			if(settingrule || r == null || data == null) return;
			r.Value = data.Category.Name;
			RuleChanged();
		}

		private void rulesubcategories_CheckedChanged(object sender, EventArgs e)
		{
			ThingsFilterRule r = SelectedRule;
			if(settingrule || r == null) return;
			r.IncludeSubcategories = rulesubcategories.Checked;
			RuleChanged();
		}

		#endregion
	}
}