namespace CodeImp.DoomBuilder.BuilderModes.Interface
{
	partial class ExtrudeLinedefsOptionsPanel
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if(disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.toolstrip = new System.Windows.Forms.ToolStrip();
			this.distancelabel = new System.Windows.Forms.ToolStripLabel();
			this.distance = new CodeImp.DoomBuilder.Controls.ToolStripNumericUpDown();
			this.flip = new System.Windows.Forms.ToolStripButton();
			this.separator1 = new System.Windows.Forms.ToolStripSeparator();
			this.floorlabel = new System.Windows.Forms.ToolStripLabel();
			this.floordelta = new CodeImp.DoomBuilder.Controls.ToolStripNumericUpDown();
			this.ceillabel = new System.Windows.Forms.ToolStripLabel();
			this.ceildelta = new CodeImp.DoomBuilder.Controls.ToolStripNumericUpDown();
			this.reset = new System.Windows.Forms.ToolStripButton();
			this.separator2 = new System.Windows.Forms.ToolStripSeparator();
			this.apply = new System.Windows.Forms.ToolStripButton();
			this.cancel = new System.Windows.Forms.ToolStripButton();
			this.toolstrip.SuspendLayout();
			this.SuspendLayout();
			//
			// toolstrip
			//
			this.toolstrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.distancelabel,
            this.distance,
            this.flip,
            this.separator1,
            this.floorlabel,
            this.floordelta,
            this.ceillabel,
            this.ceildelta,
            this.reset,
            this.separator2,
            this.cancel,
            this.apply});
			this.toolstrip.Location = new System.Drawing.Point(0, 0);
			this.toolstrip.Name = "toolstrip";
			this.toolstrip.Size = new System.Drawing.Size(760, 25);
			this.toolstrip.TabIndex = 0;
			this.toolstrip.Text = "toolStrip1";
			//
			// distancelabel
			//
			this.distancelabel.Name = "distancelabel";
			this.distancelabel.Size = new System.Drawing.Size(55, 22);
			this.distancelabel.Text = "Distance:";
			//
			// distance
			//
			this.distance.AutoSize = false;
			this.distance.Increment = new decimal(new int[] {
            8,
            0,
            0,
            0});
			this.distance.Margin = new System.Windows.Forms.Padding(3, 0, 6, 0);
			this.distance.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
			this.distance.Minimum = new decimal(new int[] {
            10000,
            0,
            0,
            -2147483648});
			this.distance.Name = "distance";
			this.distance.Size = new System.Drawing.Size(56, 25);
			this.distance.Text = "64";
			this.distance.Value = new decimal(new int[] {
            64,
            0,
            0,
            0});
			this.distance.ValueChanged += new System.EventHandler(this.OnUIValuesChanged);
			//
			// flip
			//
			this.flip.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.flip.Image = global::CodeImp.DoomBuilder.BuilderModes.Properties.Resources.Flip;
			this.flip.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.flip.Name = "flip";
			this.flip.Size = new System.Drawing.Size(23, 22);
			this.flip.Text = "Flip Direction";
			this.flip.Click += new System.EventHandler(this.flip_Click);
			//
			// separator1
			//
			this.separator1.Name = "separator1";
			this.separator1.Size = new System.Drawing.Size(6, 25);
			//
			// floorlabel
			//
			this.floorlabel.Name = "floorlabel";
			this.floorlabel.Size = new System.Drawing.Size(40, 22);
			this.floorlabel.Text = "Floor:";
			//
			// floordelta
			//
			this.floordelta.AutoSize = false;
			this.floordelta.Increment = new decimal(new int[] {
            8,
            0,
            0,
            0});
			this.floordelta.Margin = new System.Windows.Forms.Padding(3, 0, 6, 0);
			this.floordelta.Maximum = new decimal(new int[] {
            32767,
            0,
            0,
            0});
			this.floordelta.Minimum = new decimal(new int[] {
            32768,
            0,
            0,
            -2147483648});
			this.floordelta.Name = "floordelta";
			this.floordelta.Size = new System.Drawing.Size(56, 25);
			this.floordelta.Text = "0";
			this.floordelta.ToolTipText = "Added to the new sectors' floor height";
			this.floordelta.ValueChanged += new System.EventHandler(this.OnUIValuesChanged);
			//
			// ceillabel
			//
			this.ceillabel.Name = "ceillabel";
			this.ceillabel.Size = new System.Drawing.Size(51, 22);
			this.ceillabel.Text = "Ceiling:";
			//
			// ceildelta
			//
			this.ceildelta.AutoSize = false;
			this.ceildelta.Increment = new decimal(new int[] {
            8,
            0,
            0,
            0});
			this.ceildelta.Margin = new System.Windows.Forms.Padding(3, 0, 6, 0);
			this.ceildelta.Maximum = new decimal(new int[] {
            32767,
            0,
            0,
            0});
			this.ceildelta.Minimum = new decimal(new int[] {
            32768,
            0,
            0,
            -2147483648});
			this.ceildelta.Name = "ceildelta";
			this.ceildelta.Size = new System.Drawing.Size(56, 25);
			this.ceildelta.Text = "0";
			this.ceildelta.ToolTipText = "Added to the new sectors' ceiling height";
			this.ceildelta.ValueChanged += new System.EventHandler(this.OnUIValuesChanged);
			//
			// reset
			//
			this.reset.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.reset.Image = global::CodeImp.DoomBuilder.BuilderModes.Properties.Resources.Reset;
			this.reset.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.reset.Margin = new System.Windows.Forms.Padding(0, 1, 3, 2);
			this.reset.Name = "reset";
			this.reset.Size = new System.Drawing.Size(23, 22);
			this.reset.Text = "Reset";
			this.reset.Click += new System.EventHandler(this.reset_Click);
			//
			// separator2
			//
			this.separator2.Name = "separator2";
			this.separator2.Size = new System.Drawing.Size(6, 25);
			//
			// apply
			//
			this.apply.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.apply.Image = global::CodeImp.DoomBuilder.BuilderModes.Properties.Resources.Check;
			this.apply.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.apply.Margin = new System.Windows.Forms.Padding(6, 1, 0, 2);
			this.apply.Name = "apply";
			this.apply.Size = new System.Drawing.Size(23, 22);
			this.apply.Text = "Apply";
			this.apply.Click += new System.EventHandler(this.apply_Click);
			//
			// cancel
			//
			this.cancel.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.cancel.Image = global::CodeImp.DoomBuilder.BuilderModes.Properties.Resources.Close;
			this.cancel.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.cancel.Name = "cancel";
			this.cancel.Size = new System.Drawing.Size(23, 22);
			this.cancel.Text = "Cancel";
			this.cancel.Click += new System.EventHandler(this.cancel_Click);
			//
			// ExtrudeLinedefsOptionsPanel
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.Controls.Add(this.toolstrip);
			this.Name = "ExtrudeLinedefsOptionsPanel";
			this.Size = new System.Drawing.Size(760, 60);
			this.toolstrip.ResumeLayout(false);
			this.toolstrip.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.ToolStrip toolstrip;
		private System.Windows.Forms.ToolStripLabel distancelabel;
		private CodeImp.DoomBuilder.Controls.ToolStripNumericUpDown distance;
		private System.Windows.Forms.ToolStripButton flip;
		private System.Windows.Forms.ToolStripSeparator separator1;
		private System.Windows.Forms.ToolStripLabel floorlabel;
		private CodeImp.DoomBuilder.Controls.ToolStripNumericUpDown floordelta;
		private System.Windows.Forms.ToolStripLabel ceillabel;
		private CodeImp.DoomBuilder.Controls.ToolStripNumericUpDown ceildelta;
		private System.Windows.Forms.ToolStripButton reset;
		private System.Windows.Forms.ToolStripSeparator separator2;
		private System.Windows.Forms.ToolStripButton apply;
		private System.Windows.Forms.ToolStripButton cancel;
	}
}
