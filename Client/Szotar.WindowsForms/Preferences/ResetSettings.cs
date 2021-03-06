﻿using System;

namespace Szotar.WindowsForms.Preferences {
	[PreferencePage("Reset Settings", Importance = -10, Parent = typeof(Categories.Advanced))]
	public partial class ResetSettings : PreferencePage {
		public ResetSettings() {
			InitializeComponent();
		}

		private void resetButton_Click(object sender, EventArgs e) {
			// TODO: Confirm that this will reset the collection *now* rather than on commit
			Owner.ClearCommitList();
			Configuration.Default.Reset();
		}
	}

	[PreferencePage("Advanced Settings", Importance = -15, Parent = typeof(Categories.Advanced))]
	public class AdvancedSettings : PreferencePage {
		// TODO: commit properly
		public override void Commit() {

		}
	}
}
