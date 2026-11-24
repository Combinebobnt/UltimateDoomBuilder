using System.Drawing;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.Controls
{
	public partial class StatisticsControl : UserControl
	{
		public StatisticsControl() 
		{
			InitializeComponent();
			this.Visible = false;
		}

		public void UpdateStatistics() 
		{
			// Update statistics
			verticescount.Text = General.Map.Map.Vertices.Count.ToString();
			linedefscount.Text = General.Map.Map.Linedefs.Count.ToString();
			sidedefscount.Text = General.Map.Map.Sidedefs.Count.ToString();
			sectorscount.Text = General.Map.Map.Sectors.Count.ToString();
			thingscount.Text = General.Map.Map.Things.Count.ToString();

			// Exceeding them limits?
			verticescount.ForeColor = (General.Map.Map.Vertices.Count > General.Map.FormatInterface.MaxVertices ? Windows.UITheme.Pick(Color.Red, Windows.UITheme.DarkWarningText) : SystemColors.GrayText);
			linedefscount.ForeColor = (General.Map.Map.Linedefs.Count > General.Map.FormatInterface.MaxLinedefs ? Windows.UITheme.Pick(Color.Red, Windows.UITheme.DarkWarningText) : SystemColors.GrayText);
			sidedefscount.ForeColor = (General.Map.Map.Sidedefs.Count > General.Map.FormatInterface.MaxSidedefs ? Windows.UITheme.Pick(Color.Red, Windows.UITheme.DarkWarningText) : SystemColors.GrayText);
			sectorscount.ForeColor = (General.Map.Map.Sectors.Count > General.Map.FormatInterface.MaxSectors ? Windows.UITheme.Pick(Color.Red, Windows.UITheme.DarkWarningText) : SystemColors.GrayText);
			thingscount.ForeColor = (General.Map.Map.Things.Count > General.Map.FormatInterface.MaxThings ? Windows.UITheme.Pick(Color.Red, Windows.UITheme.DarkWarningText) : SystemColors.GrayText);

			verticeslabel.ForeColor = verticescount.ForeColor;
			linedefslabel.ForeColor = linedefscount.ForeColor;
			sidedefslabel.ForeColor = sidedefscount.ForeColor;
			sectorslabel.ForeColor = sectorscount.ForeColor;
			thingslabel.ForeColor = thingscount.ForeColor;
		}
	}
}
