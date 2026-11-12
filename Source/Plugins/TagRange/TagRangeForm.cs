using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.Windows;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.Map;

namespace CodeImp.DoomBuilder.TagRange
{
	public partial class TagRangeForm : DelayedForm
	{
		private UniversalType selectiontype;
		private readonly List<MapElement> selection = new List<MapElement>();
		private int[] targets = new int[0];
		private HashSet<int> usedtags;
		private bool settingup = true;

		private static int storedstep = 1;
		private static bool storedrelative;
		private static int storedorder;
		private static int storedcorner;
		private static string storedstride = "";
		private static int storedtarget;
		private static int storedfilter;

		public int SelectionCount { get { return selection.Count; } }
		private int Target { get { return applyto.SelectedIndex >= 0 && applyto.SelectedIndex < targets.Length ? targets[applyto.SelectedIndex] : -1; } }
		private bool IsArgument { get { return Target > 0; } }
		
		// Constructor
		public TagRangeForm()
		{
			InitializeComponent();
		}
		
		// This sets up the form
		public void Setup()
		{
			General.Map.Map.ClearAllMarks(false);
			string name = "elements";
			switch(General.Editing.Mode.GetType().Name)
			{
				case "SectorsMode":
					General.Map.Map.MarkSelectedSectors(true, true);
					selectiontype = UniversalType.SectorTag;
					foreach(Sector element in General.Map.Map.GetSelectedSectors(true)) selection.Add(element);
					name = "sectors";
					break;
				case "LinedefsMode":
					General.Map.Map.MarkSelectedLinedefs(true, true);
					selectiontype = UniversalType.LinedefTag;
					foreach(Linedef element in General.Map.Map.GetSelectedLinedefs(true)) selection.Add(element);
					name = "linedefs";
					break;
				case "ThingsMode":
					General.Map.Map.MarkSelectedThings(true, true);
					selectiontype = UniversalType.ThingTag;
					foreach(Thing element in General.Map.Map.GetSelectedThings(true)) selection.Add(element);
					name = "things";
					break;
			}
			Text = "Create range for " + SelectionCount + " " + name;

			Dictionary<int, bool> tags = new Dictionary<int, bool>();
			General.Map.Map.ForAllTags(TagHandler, false, tags);
			usedtags = new HashSet<int>(tags.Keys);
			rangestart.Text = General.Map.Map.GetNewTag().ToString();
			rangestep.Text = storedstep.ToString();
			relativemode.Checked = storedrelative;
			order.SelectedIndex = storedorder;
			startcorner.SelectedIndex = storedcorner;
			rowstride.Text = storedstride;
			bool sectorselection = selectiontype == UniversalType.SectorTag;
			bool hastag = selectiontype == UniversalType.LinedefTag ? General.Map.FormatInterface.HasLinedefTag : General.Map.FormatInterface.HasThingTag;
			targets = GridOrder.GetTargets(sectorselection, hastag, General.Map.FormatInterface.HasActionArgs);
			applyto.Items.Clear();
			foreach(int target in targets) applyto.Items.Add(target == 0 ? "Tag" : "Argument " + target);
			int remembered = Array.IndexOf(targets, sectorselection ? 0 : storedtarget);
			applyto.SelectedIndex = remembered >= 0 ? remembered : (targets.Length > 0 ? 0 : -1);
			linefilter.SelectedIndex = selectiontype == UniversalType.LinedefTag ? storedfilter : 0;
			applyto.Visible = applytolabel.Visible = selectiontype != UniversalType.SectorTag;
			linefilter.Visible = linefilterlabel.Visible = selectiontype == UniversalType.LinedefTag;
			settingup = false;
			UpdateChanges();
		}
		
		// Handler for finding a new tag
		private static void TagHandler(MapElement element, bool actionargument, UniversalType type, ref int value, Dictionary<int, bool> usedtags)
		{
			if(value > 0) usedtags[value] = true; //mxd
		}
		
		// This creates a range
		private GridOrder.RangeResult CreateRange(out GridOrder.Result grid, out List<MapElement> elements)
		{
			elements = new List<MapElement>(selection.Count);
			List<Vector2D> anchors = new List<Vector2D>(selection.Count);
			List<int> initialvalues = new List<int>(selection.Count);
			foreach(MapElement element in selection)
			{
				Sector sector = element as Sector;
				Linedef line = element as Linedef;
				Thing thing = element as Thing;
				if(line != null && !GridOrder.MatchesLine(line.Start.Position, line.End.Position, (GridOrder.LineFilter)linefilter.SelectedIndex)) continue;
				elements.Add(element);
				if(sector != null)
				{
					anchors.Add(new Vector2D(sector.BBox.X + sector.BBox.Width * 0.5, sector.BBox.Y + sector.BBox.Height * 0.5));
					initialvalues.Add(sector.Tag);
				}
				else if(line != null)
				{
					anchors.Add(line.Line.GetCoordinatesAt(0.5));
					initialvalues.Add(IsArgument ? line.Args[Target - 1] : line.Tag);
				}
				else if(thing != null)
				{
					anchors.Add(new Vector2D(thing.Position.x, thing.Position.y));
					initialvalues.Add(IsArgument ? thing.Args[Target - 1] : thing.Tag);
				}
			}
			grid = GridOrder.Sort(anchors, (GridOrder.Order)order.SelectedIndex, (GridOrder.Corner)startcorner.SelectedIndex);
			int start, step, stride;
			bool hasstride = !string.IsNullOrWhiteSpace(rowstride.Text);
			if(!TryValue(rangestart.Text, 0, out start) || !TryValue(rangestep.Text, 1, out step) || !TryValue(rowstride.Text, 0, out stride))
				return new GridOrder.RangeResult(0) { OutOfRange = true };
			int minimum, maximum;
			GridOrder.GetValueLimits(IsArgument, General.Map.HEXEN, General.Map.FormatInterface.MinTag, General.Map.FormatInterface.MaxTag, out minimum, out maximum);
			return GridOrder.CreateRange(grid, initialvalues, start, step, hasstride ? (int?)stride : null,
				relativemode.Checked, skipdoubletags.Checked, IsArgument ? null : usedtags, minimum, maximum);
		}

		private static bool TryValue(string text, int fallback, out int value)
		{
			value = fallback;
			return string.IsNullOrWhiteSpace(text) || int.TryParse(text, out value);
		}
		
		// This updates the calculated range
		private void UpdateChanges()
		{
			if(settingup) return;
			startcorner.Enabled = order.SelectedIndex != (int)GridOrder.Order.Selection;
			skipdoubletags.Enabled = !IsArgument && string.IsNullOrWhiteSpace(rowstride.Text);
			rangestart.AllowNegative = relativemode.Checked || (IsArgument && !General.Map.HEXEN);
			label1.Text = IsArgument ? "Start value:" : "Start Tag:";
			label4.Text = IsArgument ? "End value:" : "End Tag:";
			relativemode.Text = IsArgument ? "Relative to existing arguments" : "Relative to existing tags";
			GridOrder.Result grid;
			List<MapElement> elements;
			GridOrder.RangeResult range = CreateRange(out grid, out elements);
			outoftagswarning.Text = "The range exceeds the allowed values or an input is not a valid integer."
				+ (IsArgument && General.Map.HEXEN ? " Hexen arguments must be 0..255." : "");
			outoftagswarning.Visible = range.OutOfRange;
			okbutton.Enabled = Target >= 0 && !range.OutOfRange && elements.Count > 0;
			doubletagwarning.Visible = range.TagsUsed && !range.OutOfRange;
			skipdoubletags.Visible = !IsArgument && !range.OutOfRange && (range.TagsUsed || skipdoubletags.Checked);
			skipdoubletags.BringToFront();
			endtaglabel.Text = range.Values.Count > 0 ? range.Values[range.Values.Count - 1].ToString() : "-";
			summarylabel.Text = grid.RowCount + " rows x " + grid.ColumnCount + " cols";
			if(range.Values.Count > 0)
			{
				summarylabel.Text += ", first " + range.Values[0] + ", last " + range.Values[range.Values.Count - 1];
				int first = -1, last = -1;
				for(int i = 0; i < range.Values.Count; i++)
				{
					if(grid.Entries[i].Row != 0) continue;
					if(first < 0) first = i;
					last = i;
				}
				if(first >= 0) summarylabel.Text += "\nrow 0: " + range.Values[first] + ".." + range.Values[last];
			}
			else if(elements.Count == 0) summarylabel.Text += "\nNo matching elements.";
			if(range.OutOfRange) summarylabel.Text += " (incomplete)";
			summarylabel.Text += "\n" + (selection.Count - elements.Count) + " skipped";
			if(grid.Mode != GridOrder.Order.Selection) summarylabel.Text += "; coordinate ranks (missing rows/cols compress).";
		}
		
		// Range start changes
		private void rangestart_WhenTextChanged(object sender, EventArgs e)
		{
			UpdateChanges();
		}
		
		// OK clicked
		private void okbutton_Click(object sender, EventArgs e)
		{
			GridOrder.Result grid;
			List<MapElement> elements;
			GridOrder.RangeResult range = CreateRange(out grid, out elements);
			if(Target < 0 || range.OutOfRange || elements.Count == 0)
			{
				General.ShowWarningMessage("The range cannot be created. Check the values and line filter.", MessageBoxButtons.OK);
				return;
			}

			string name = selectiontype == UniversalType.SectorTag ? "sector" : selectiontype == UniversalType.LinedefTag ? "linedef" : "thing";
			General.Map.UndoRedo.CreateUndo("Set " + elements.Count + " " + name + (IsArgument ? " argument " + Target : " tags"));
			for(int i = 0; i < grid.Entries.Length; i++)
			{
				MapElement element = elements[grid.Entries[i].Index];
				if(element is Sector) ((Sector)element).Tag = range.Values[i];
				else if(element is Linedef)
				{
					Linedef line = (Linedef)element;
					if(IsArgument)
					{
						line.Fields.BeforeFieldsChange();
						line.Args[Target - 1] = range.Values[i];
					}
					else line.Tag = range.Values[i];
				}
				else if(element is Thing)
				{
					Thing thing = (Thing)element;
					if(IsArgument)
					{
						thing.Fields.BeforeFieldsChange();
						thing.Args[Target - 1] = range.Values[i];
					}
					else thing.Tag = range.Values[i];
					thing.UpdateConfiguration();
				}
			}
			General.Map.IsChanged = true;
			storedstep = rangestep.GetResult(1);
			storedrelative = relativemode.Checked;
			storedorder = order.SelectedIndex;
			storedcorner = startcorner.SelectedIndex;
			storedstride = rowstride.Text;
			if(selectiontype != UniversalType.SectorTag) storedtarget = Target;
			if(selectiontype == UniversalType.LinedefTag) storedfilter = linefilter.SelectedIndex;
			DialogResult = DialogResult.OK;
			Close();
		}
		
		// Cancel clicked
		private void cancelbutton_Click(object sender, EventArgs e)
		{
			this.Close();
		}
		
		// Skip over used tags changed
		private void skipdoubletags_CheckedChanged(object sender, EventArgs e)
		{
			UpdateChanges();
		}

		//mxd
		private void rangestep_WhenTextChanged(object sender, EventArgs e) 
		{
			UpdateChanges();
		}

		//mxd
		private void relativemode_CheckedChanged(object sender, EventArgs e) 
		{
			UpdateChanges();
		}

		private void options_Changed(object sender, EventArgs e) { UpdateChanges(); }
	}
}

