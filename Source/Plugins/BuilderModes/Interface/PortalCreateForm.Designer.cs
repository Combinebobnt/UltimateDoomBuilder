namespace CodeImp.DoomBuilder.BuilderModes.Interface
{
	partial class PortalCreateForm
	{
		private System.ComponentModel.IContainer components = null;
		private System.Windows.Forms.Label ends;
		private System.Windows.Forms.Label kindlabel;
		private System.Windows.Forms.ComboBox kind;
		private System.Windows.Forms.Label typelabel;
		private System.Windows.Forms.ComboBox type;
		private System.Windows.Forms.Label planelabel;
		private System.Windows.Forms.ComboBox plane;
		private System.Windows.Forms.Label opacitylabel;
		private System.Windows.Forms.NumericUpDown opacity;
		private System.Windows.Forms.Button swap;
		private System.Windows.Forms.TextBox result;
		private System.Windows.Forms.Button apply;
		private System.Windows.Forms.Button cancel;
		private System.Windows.Forms.ToolTip tooltip;

		protected override void Dispose(bool disposing)
		{
			if(disposing && (components != null)) components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.ends = new System.Windows.Forms.Label();
			this.kindlabel = new System.Windows.Forms.Label();
			this.kind = new System.Windows.Forms.ComboBox();
			this.typelabel = new System.Windows.Forms.Label();
			this.type = new System.Windows.Forms.ComboBox();
			this.planelabel = new System.Windows.Forms.Label();
			this.plane = new System.Windows.Forms.ComboBox();
			this.opacitylabel = new System.Windows.Forms.Label();
			this.opacity = new System.Windows.Forms.NumericUpDown();
			this.swap = new System.Windows.Forms.Button();
			this.result = new System.Windows.Forms.TextBox();
			this.apply = new System.Windows.Forms.Button();
			this.cancel = new System.Windows.Forms.Button();
			this.tooltip = new System.Windows.Forms.ToolTip(this.components);
			((System.ComponentModel.ISupportInitialize)(this.opacity)).BeginInit();
			this.SuspendLayout();
			//
			// ends
			//
			this.ends.Location = new System.Drawing.Point(12, 9);
			this.ends.Name = "ends";
			this.ends.Size = new System.Drawing.Size(456, 36);
			this.ends.TabIndex = 0;
			//
			// kindlabel
			//
			this.kindlabel.AutoSize = true;
			this.kindlabel.Location = new System.Drawing.Point(12, 56);
			this.kindlabel.Name = "kindlabel";
			this.kindlabel.TabIndex = 1;
			this.kindlabel.Text = "Kind:";
			//
			// kind
			//
			this.kind.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.kind.Location = new System.Drawing.Point(100, 52);
			this.kind.Name = "kind";
			this.kind.Size = new System.Drawing.Size(368, 21);
			this.kind.TabIndex = 2;
			this.kind.SelectedIndexChanged += new System.EventHandler(this.kind_SelectedIndexChanged);
			//
			// typelabel
			//
			this.typelabel.AutoSize = true;
			this.typelabel.Location = new System.Drawing.Point(12, 87);
			this.typelabel.Name = "typelabel";
			this.typelabel.TabIndex = 3;
			this.typelabel.Text = "Type:";
			//
			// type
			//
			this.type.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.type.Location = new System.Drawing.Point(100, 83);
			this.type.Name = "type";
			this.type.Size = new System.Drawing.Size(160, 21);
			this.type.TabIndex = 4;
			this.type.SelectedIndexChanged += new System.EventHandler(this.type_SelectedIndexChanged);
			//
			// planelabel
			//
			this.planelabel.AutoSize = true;
			this.planelabel.Location = new System.Drawing.Point(12, 118);
			this.planelabel.Name = "planelabel";
			this.planelabel.TabIndex = 5;
			this.planelabel.Text = "Plane:";
			//
			// plane
			//
			this.plane.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.plane.Location = new System.Drawing.Point(100, 114);
			this.plane.Name = "plane";
			this.plane.Size = new System.Drawing.Size(160, 21);
			this.plane.TabIndex = 6;
			this.plane.SelectedIndexChanged += new System.EventHandler(this.option_Changed);
			//
			// opacitylabel
			//
			this.opacitylabel.AutoSize = true;
			this.opacitylabel.Location = new System.Drawing.Point(12, 149);
			this.opacitylabel.Name = "opacitylabel";
			this.opacitylabel.TabIndex = 7;
			this.opacitylabel.Text = "Opacity:";
			//
			// opacity
			//
			this.opacity.Location = new System.Drawing.Point(100, 146);
			this.opacity.Maximum = new decimal(new int[] { 255, 0, 0, 0 });
			this.opacity.Name = "opacity";
			this.opacity.Size = new System.Drawing.Size(70, 20);
			this.opacity.TabIndex = 8;
			this.opacity.ValueChanged += new System.EventHandler(this.option_Changed);
			this.tooltip.SetToolTip(this.opacity, "Opacity of the floor or ceiling drawn over the portal, 0 to 255");
			//
			// swap
			//
			this.swap.Location = new System.Drawing.Point(300, 82);
			this.swap.Name = "swap";
			this.swap.Size = new System.Drawing.Size(168, 25);
			this.swap.TabIndex = 9;
			this.swap.Text = "Swap Ends";
			this.swap.UseVisualStyleBackColor = true;
			this.swap.Click += new System.EventHandler(this.swap_Click);
			this.tooltip.SetToolTip(this.swap, "Exchange the two ends' roles");
			//
			// result
			//
			this.result.Location = new System.Drawing.Point(12, 178);
			this.result.Multiline = true;
			this.result.Name = "result";
			this.result.ReadOnly = true;
			this.result.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this.result.Size = new System.Drawing.Size(456, 176);
			this.result.TabIndex = 10;
			//
			// apply
			//
			this.apply.Location = new System.Drawing.Point(262, 364);
			this.apply.Name = "apply";
			this.apply.Size = new System.Drawing.Size(100, 25);
			this.apply.TabIndex = 11;
			this.apply.Text = "Create";
			this.apply.UseVisualStyleBackColor = true;
			this.apply.Click += new System.EventHandler(this.apply_Click);
			//
			// cancel
			//
			this.cancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.cancel.Location = new System.Drawing.Point(368, 364);
			this.cancel.Name = "cancel";
			this.cancel.Size = new System.Drawing.Size(100, 25);
			this.cancel.TabIndex = 12;
			this.cancel.Text = "Cancel";
			this.cancel.UseVisualStyleBackColor = true;
			this.cancel.Click += new System.EventHandler(this.cancel_Click);
			//
			// PortalCreateForm
			//
			this.AcceptButton = this.apply;
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.CancelButton = this.cancel;
			this.ClientSize = new System.Drawing.Size(480, 400);
			this.Controls.Add(this.ends);
			this.Controls.Add(this.kindlabel);
			this.Controls.Add(this.kind);
			this.Controls.Add(this.typelabel);
			this.Controls.Add(this.type);
			this.Controls.Add(this.planelabel);
			this.Controls.Add(this.plane);
			this.Controls.Add(this.opacitylabel);
			this.Controls.Add(this.opacity);
			this.Controls.Add(this.swap);
			this.Controls.Add(this.result);
			this.Controls.Add(this.apply);
			this.Controls.Add(this.cancel);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "PortalCreateForm";
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Create Portal";
			((System.ComponentModel.ISupportInitialize)(this.opacity)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}
	}
}
