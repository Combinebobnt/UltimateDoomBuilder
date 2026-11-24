#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Windows;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	// Per-type thing counts: the Map Statistics window's Things tab
	public partial class ThingTypesStatsPanel : UserControl
	{
		private sealed class CategoryItem
		{
			public readonly string Text;
			public readonly HashSet<ThingCategory> Members;

			public CategoryItem(string text, HashSet<ThingCategory> members)
			{
				Text = text;
				Members = members;
			}

			public override string ToString() { return Text; }
		}

		public event EventHandler MapEdited;
		private bool filling;

		public ThingTypesStatsPanel()
		{
			InitializeComponent();
			General.ApplyDataGridViewFix(dataGridView);
		}

		public void Setup()
		{
			if(categories.Items.Count == 0) FillCategories();
			CategoryItem filter = categories.SelectedItem as CategoryItem;

			Dictionary<int, int> counts = new Dictionary<int, int>();
			Dictionary<int, ThingTypeInfo> infos = new Dictionary<int, ThingTypeInfo>();

			foreach(ThingTypeInfo ti in General.Map.Data.ThingTypes)
			{
				counts[ti.Index] = 0;
				infos[ti.Index] = ti;
			}

			foreach(Thing t in General.Map.Map.Things)
			{
				counts.TryGetValue(t.Type, out int c);
				counts[t.Type] = c + 1;
			}

			dataGridView.Rows.Clear();
			foreach(KeyValuePair<int, int> group in counts)
			{
				if(hideUnused.Checked && group.Value == 0) continue;
				infos.TryGetValue(group.Key, out ThingTypeInfo ti);
				if(filter != null && filter.Members != null && (ti == null || !filter.Members.Contains(ti.Category))) continue;

				DataGridViewRow row = new DataGridViewRow();
				row.Cells.Add(new DataGridViewTextBoxCell { Value = group.Key });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = (ti != null) ? ti.Title : "Unknown thing" });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = (ti != null) ? ti.ClassName : "-" });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = (ti != null && ti.Category != null) ? ti.Category.Title : "-" });
				row.Cells.Add(new DataGridViewTextBoxCell { Value = group.Value });
				dataGridView.Rows.Add(row);
			}

			dataGridView.Sort(ThingType, ListSortDirection.Ascending);
		}

		private void FillCategories()
		{
			filling = true;
			categories.Items.Add(new CategoryItem("All", null));
			foreach(ThingCategory c in General.Map.Data.ThingCategories) AddCategory(c, 0);
			categories.SelectedIndex = 0;
			filling = false;
		}

		// Children are indented and a category's filter includes its subcategories
		private HashSet<ThingCategory> AddCategory(ThingCategory c, int depth)
		{
			HashSet<ThingCategory> members = new HashSet<ThingCategory> { c };
			categories.Items.Add(new CategoryItem(new string(' ', depth * 3) + c.Title, members));
			foreach(ThingCategory child in c.Children) members.UnionWith(AddCategory(child, depth + 1));
			return members;
		}

		private static List<Thing> GetThingsByType(int type)
		{
			List<Thing> list = new List<Thing>();
			foreach(Thing t in General.Map.Map.Things)
				if(t.Type == type) list.Add(t);
			return list;
		}

		private void dataGridView_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
		{
			if(e.RowIndex == -1) return;
			List<Thing> list = GetThingsByType((int)dataGridView.Rows[e.RowIndex].Cells[0].Value);
			if(list.Count == 0) return;

			if(e.Button == MouseButtons.Left)
			{
				MapStatisticsForm.SelectThings(list);
			}
			else if(e.Button == MouseButtons.Right)
			{
				General.MainWindow.ShowEditThings(list);
				General.Map.Map.Update();
				Setup();
				MapEdited?.Invoke(this, EventArgs.Empty);
			}
		}

		private void hideUnused_CheckedChanged(object sender, EventArgs e)
		{
			Setup();
		}

		private void categories_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(!filling) Setup();
		}
	}
}
