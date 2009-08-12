﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

// This form displays a large amount of rows on the DataGridView control. For information on how to 
// keep good performance with large datasets, see Best Practices for Scaling the Windows Forms 
// DataGridView Control:
//  * http://msdn.microsoft.com/en-us/library/ha5xt0d9.aspx
//
// The main points to take away from that article are:
//  * Don't access grid Row objects, because it could cause them to become unshared. 
//    Prefer grid methods such as GetCellState, etc.
//  * Don't access Cell objects either. Especially don't add tooltips or context menus 
//    to individual cells. Use the Cell*Needed events.
//  * Use full-row or full-column selection modes, rather than per-cell.
//  * Apply styles to the row templates or use the events; don't set cell styles.
//  * Try not to make rows become unshared.
// Note: Mono 2.4 doesn't support shared rows. This is probably causing much of the performance loss.
namespace Szotar.WindowsForms.Forms {
	public partial class LookupForm : Form {
		public IBilingualDictionary Dictionary { get; private set; }

		SearchMode searchMode, displayedSearchMode;
		IList<SearchResult> results;

		ListBuilder listBuilder;

		CultureInfo sourceCulture, targetCulture;
		DisposableComponent listFontComponent;
		bool ctrlHeld = false;
		Font defaultGridFont;

		class LookupFormFileIsInUse : FileIsInUse {
			LookupForm form;

			public LookupFormFileIsInUse(LookupForm form, string path)
				: base(path)
			{
				this.form = form;
				base.CanClose = true;
				base.WindowHandle = form.Handle;
			}

			// Who knows what thread this will be invoked on?
			public override void CloseFile() {
				form.Invoke(new Action(delegate {
					form.Close();
				}));
			}
		}

		public LookupForm(IBilingualDictionary dictionary)
			: this()
		{
			components.Add(new DisposableComponent(new LookupFormFileIsInUse(this, dictionary.Path)));
			components.Add(new DisposableComponent(dictionary));

			Dictionary = dictionary;

			var mru = GuiConfiguration.RecentDictionaries ?? new MruList<DictionaryInfo>(10);
			mru.Update(dictionary.Info);
			GuiConfiguration.RecentDictionaries = mru;

			Font listFont = GuiConfiguration.GetListFont();
			if (listFont != null) {
				components.Add(listFontComponent = new DisposableComponent(listFont));
				defaultGridFont = grid.Font;
				grid.Font = listFont;
			}

			// TODO: This really needs testing. It could make things completely unusable...
			// I can't even remember if they're used...
			try {
				if (dictionary.FirstLanguageCode != null && dictionary.SecondLanguage != null) {
					sourceCulture = new CultureInfo(dictionary.FirstLanguageCode);
					targetCulture = new CultureInfo(dictionary.SecondLanguageCode);
				}
			} catch (ArgumentException) {
				// One of the cultures wasn't supported. In that case, set both cultures to null,
				// because it isn't worth having only one.
				sourceCulture = targetCulture = null;
			}

			InitialiseView();

			mainMenu.Renderer = contextMenu.Renderer = toolStripPanel.Renderer = new ToolStripAeroRenderer(ToolbarTheme.CommunicationsToolbar);
		}

		/// <summary>Open the given dictionary, possibly loading from a file, 
		/// in a new LookupForm window.</summary>
		public LookupForm(DictionaryInfo dictionaryInfo)
			: this(dictionaryInfo.GetFullInstance()) 
		{ }

		/// <summary>Load the dictionary from the given path into a new LookupForm window.</summary>
		public LookupForm(string dictionaryPath)
			: this(new SimpleDictionary(dictionaryPath)) 
		{ }

		private LookupForm() {
			InitializeComponent();
			RegisterSettingsChangedEventHandlers();

			Icon = Properties.Resources.DictionaryIcon;
		}

		private void RemoveEventHandlers() {
			UnregisterSettingsChangedEventHandlers();
		}

		// Call this after the dictionaries are initialised.
		private void InitialiseView() {
			// Updates the mode switching button.
			SearchMode = SearchMode;

			AdjustGridRowHeight();

			searchBox.RealTextChanged += new EventHandler(searchBox_RealTextChanged);
			
			// Show custom tooltips that don't get in the way of the mouse and don't disappear so quickly.
			grid.MouseMove += new MouseEventHandler(grid_MouseMove);
			grid.MouseLeave += new EventHandler(grid_MouseLeave);
			grid.ShowCellToolTips = false;

			UpdateResults();

			// By now, the columns should have been created.
			grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
			grid.Columns[0].FillWeight = GuiConfiguration.LookupFormColumn1FillWeight;
			grid.Columns[1].Resizable = DataGridViewTriState.False; 
			grid.CellFormatting += new DataGridViewCellFormattingEventHandler(grid_CellFormatting);
			grid.ColumnWidthChanged += new DataGridViewColumnEventHandler(grid_ColumnWidthChanged);

			ignoreAccentsCheck.Checked = ignoreAccentsMenuItem.Checked = GuiConfiguration.IgnoreAccents;
			ignoreAccentsCheck.Click += new EventHandler(ignoreAccentsCheck_Click);
			ignoreCaseCheck.Checked = ignoreCaseMenuItem.Checked = GuiConfiguration.IgnoreCase;
			ignoreCaseCheck.Click += new EventHandler(ignoreCaseCheck_Click);

			this.Shown += new EventHandler(LookupForm_Shown);
			this.Closed += new EventHandler(LookupForm_Closed);
			this.InputLanguageChanged += new InputLanguageChangedEventHandler(LookupForm_InputLanguageChanged);
			this.KeyDown += (s, e) => { if (e.KeyCode == Keys.ControlKey) ctrlHeld = true; };
			this.KeyUp += (s, e) => { if(e.KeyCode == Keys.ControlKey) ctrlHeld = false; };

			fileMenu.DropDownOpening += new EventHandler(fileMenu_DropDownOpening);
		}

		void LookupForm_Shown(object sender, EventArgs e) {
			this.PerformLayout();
			searchBox.Focus();
		}

		#region Appearance
		/// <summary>Colours a cell differently based on how well the result matched the search term
		/// and whether the cell is on an alternating row.</summary>
		void grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e) {
			MatchType matchType = results != null && e.RowIndex < results.Count ? results[e.RowIndex].MatchType : MatchType.NormalMatch;

			switch (matchType) {
				case MatchType.PerfectMatch:
					e.CellStyle.BackColor = Color.DarkGoldenrod;
					e.CellStyle.ForeColor = Color.White;
					break;
				case MatchType.StartMatch:
					e.CellStyle.BackColor = (e.RowIndex % 2 == 0) ? Color.LightGoldenrodYellow : Color.PaleGoldenrod;
					break;
				default:
					e.CellStyle.BackColor = (e.RowIndex % 2 == 0) ? Color.White : Color.WhiteSmoke;
					break;
			}
		}

		void AdjustGridRowHeight() {
			using (Graphics g = grid.CreateGraphics()) {
				float inches = grid.Font.SizeInPoints / 72;
				const double lineHeight = 1.9;
				int pixels = (int)(Math.Round(lineHeight * inches * g.DpiY));
				grid.RowTemplate.Height = pixels;
			}

			// Forces the grid to re-apply its template settings -- there must be a better way.
			UpdateResults();
		}
		#endregion

		#region Settings Bindings
		private void RegisterSettingsChangedEventHandlers() {
			Configuration.Default.SettingChanged += new EventHandler<SettingChangedEventArgs>(SettingChanging);
		}

		private void UnregisterSettingsChangedEventHandlers() {
			Configuration.Default.SettingChanged -= new EventHandler<SettingChangedEventArgs>(SettingChanging);
		}

		//Update UI state.
		void SettingChanging(object sender, SettingChangedEventArgs e) {
			if (e.SettingName == "IgnoreAccents" || e.SettingName == "IgnoreCase") {
				// This shouldn't fire the CheckedChanged/SettingChanged events in an infinite loop.
				if (e.SettingName == "IgnoreAccents")
					ignoreAccentsMenuItem.Checked = ignoreAccentsCheck.Checked = GuiConfiguration.IgnoreAccents;
				else if (e.SettingName == "IgnoreCase")
					ignoreCaseMenuItem.Checked = ignoreCaseCheck.Checked = GuiConfiguration.IgnoreCase;

				// We might want to skip this if nothing was actually changed.
				UpdateResults();
			} else if (e.SettingName == "ListFontName" || e.SettingName == "ListFontSize") {
				// Note: this is slightly inefficient -- if both are set at once it redisplays twice.
				Font disposeOf = null;
				if (listFontComponent != null) {
					components.Remove(listFontComponent);
					disposeOf = grid.Font;
				}
				Font font = GuiConfiguration.GetListFont();
				if (font != null) {
					if (defaultGridFont == null)
						defaultGridFont = grid.Font;
					listFontComponent = new DisposableComponent(font);
					grid.Font = font;
				} else {
					grid.Font = defaultGridFont;
				}
				if (disposeOf != null)
					disposeOf.Dispose();
				AdjustGridRowHeight();
			}
		}

		void grid_ColumnWidthChanged(object sender, DataGridViewColumnEventArgs e) {
			GuiConfiguration.LookupFormColumn1FillWeight = grid.Columns[0].FillWeight;
		}
		#endregion

		#region Search code
		private IDictionarySection GetSectionBySearchMode(SearchMode mode) {
			return mode == SearchMode.Forward ? Dictionary.ForwardsSection : Dictionary.ReverseSection;
		}

		private void UpdateResults() {
			ISearchDataSource dict = this.GetSectionBySearchMode(this.SearchMode);
			SearchMode finalSearchMode = this.SearchMode;

			string search = searchBox.RealText.Trim().Normalize();

			results = new List<SearchResult>();
			int foundAt = -1;
			bool hadPerfect = false;
			int i = 0;

			// Look for the first result containing the search term at the start.
			foreach (SearchResult result in dict.Search(search, ignoreAccentsCheck.Checked, ignoreCaseCheck.Checked)) {
				if (result.MatchType != MatchType.NormalMatch && foundAt < 0 && !hadPerfect)
					foundAt = i;
				if (result.MatchType == MatchType.PerfectMatch && !hadPerfect) {
					foundAt = i;
					hadPerfect = true;
				}

				results.Add(result);
				i++;
			}

			if (results.Count == 0) {
				finalSearchMode = SearchMode == SearchMode.Backward ? SearchMode.Forward : SearchMode.Backward;
				dict = this.GetSectionBySearchMode(finalSearchMode);
				foreach (SearchResult result in dict.Search(search, ignoreAccentsCheck.Checked, ignoreCaseCheck.Checked)) {
					if (result.MatchType != MatchType.NormalMatch && foundAt < 0 && !hadPerfect)
						foundAt = i;
					if (result.MatchType == MatchType.PerfectMatch && !hadPerfect) {
						foundAt = i;
						hadPerfect = true;
					}

					results.Add(result);
					i++;
				}
				if (results.Count == 0)
					finalSearchMode = SearchMode;
			}

			grid.VirtualMode = true;
			grid.DataSource = results;

			if (foundAt >= 0) {
				grid.FirstDisplayedScrollingRowIndex = foundAt;
				grid.FirstDisplayedScrollingColumnIndex = 0;
			}

			displayedSearchMode = finalSearchMode;
			UpdateButtonNames();

			grid.ClearSelection();
			grid.PerformLayout();

			Text = string.Format(CultureInfo.CurrentUICulture, "{0} - {1} results", Dictionary.Name, results.Count);
		}

		private void SwitchMode() {
			SearchMode = SearchMode == SearchMode.Forward ? SearchMode.Backward : SearchMode.Forward;
		}

		private void FocusSearchField() {
			searchBox.Focus();
			searchBox.SelectAll();
		}
		#endregion

		#region ToolTip
		// Keep a cached version of the most recent tooltip: this should speed things up a little.
		ToolTip infoTip;
		int infoTipRow;
		string infoTipText;
		Point currentInfoTipMouseLocation;

		string GetInfoTipTitle(int rowIndex) {
			if (rowIndex < 0 || rowIndex > results.Count)
				return null;
			return results[rowIndex].Phrase;
		}

		string GetInfoTipText(int rowIndex) {
			if (rowIndex < 0 || rowIndex > results.Count)
				return null;

			// Use the cached version in the current tooltip if possible.
			if (infoTipRow == rowIndex)
				return infoTipText;

			SearchMode dsm = DisplayedSearchMode;
			Entry entry = results[rowIndex].Entry;
			if (entry.Translations == null)
				GetSectionBySearchMode(DisplayedSearchMode).GetFullEntry(entry);

			ISearchDataSource otherSide = GetSectionBySearchMode(dsm == SearchMode.Forward ? SearchMode.Backward : SearchMode.Forward);
			StringBuilder sb = new StringBuilder();
			foreach (Translation term in entry.Translations) {
				if (sb.Length > 0)
					sb.Append(",");
				sb.Append(term.Value);
			}
			string search = sb.ToString();
			sb.Length = 0;

			foreach (SearchResult sr in otherSide.Search(search, false, false)) {
				if (sr.MatchType == MatchType.PerfectMatch) {
					sb.AppendLine(SanitizeToolTipLine(sr.Phrase + " -> " + sr.Translation));
				}
			}

			if (sb.Length > 0)
				return sb.ToString();
			return null;
		}
		
		/// <summary>Removes some common annoyances with tooltips (really wide tooltips).</summary>
		/// <remarks>Currently obsolete (tooltip size is limited, text wraps).</remarks>
		string SanitizeToolTipLine(string line) {
			return line;

			const int maxWidth = 200;
			if (line.Length < maxWidth)
				return line;

			var sb = new StringBuilder();
			while (line.Length > maxWidth) {
				if(sb.Length > 0)
					sb.Append("\t");

				//Find first space before maxWidth characters.
				int i = line.LastIndexOf(" ", maxWidth);
				if(i == -1) {
					//No spaces before maxWidth?! As a last resort, increase the width a little.
					i = line.LastIndexOf(" ", maxWidth + 20);
					if(i == -1) {
						sb.AppendLine(line);
						return sb.ToString();
					}
				}

				sb.AppendLine(line.Substring(0, i));
				line = line.Substring(i + 1).Trim();
			}

			sb.AppendLine(line);

			return sb.ToString();
		}

		void grid_MouseMove(object sender, MouseEventArgs e) {
			if(e.Button != MouseButtons.None)
				return;

			if (e.Location == currentInfoTipMouseLocation)
				return;
			currentInfoTipMouseLocation = e.Location;

			var hitTest = grid.HitTest(e.X, e.Y);
			if(hitTest.Type != DataGridViewHitTestType.Cell && hitTest.Type != DataGridViewHitTestType.RowHeader) {
				if(infoTip != null && infoTip.Active)
					infoTip.Hide(grid);
				infoTipText = null;
				infoTipRow = -1;
				return;
			}

			if (hitTest.RowIndex == infoTipRow)
				return;

			string text = GetInfoTipText(hitTest.RowIndex);

			if (text == null) {
				if(infoTip != null)
					infoTip.Hide(grid);
				return;
			}

			infoTipRow = hitTest.RowIndex;
			infoTipText = text;

			if (infoTip == null) {
				infoTip = new ToolTip(components);
				infoTip.StripAmpersands = false;
				infoTip.UseAnimation = false;
				infoTip.Popup += (s, e3) => { e3.ToolTipSize = new Size(Math.Min(e3.ToolTipSize.Width, grid.Width), e3.ToolTipSize.Height);  };
			}

			infoTip.ToolTipTitle = GetInfoTipTitle(hitTest.RowIndex);

			int offset = grid.GetRowDisplayRectangle(hitTest.RowIndex, true).Height;

			//This usually happens due to bugs and import errors. Either way, it's bad.
			if (text.Length > 5000) {
				infoTip.Hide(grid);
				return;
			}

			infoTip.Show(text, grid, e.X + offset, e.Y + offset);
		}

		void grid_MouseLeave(object sender, EventArgs e) {
			if (infoTip != null && infoTip.Active)
				infoTip.Hide(grid);
		}
		#endregion

		#region Properties
		[Browsable(true)]
		[EditorBrowsable(EditorBrowsableState.Always)]
		[Description("Specifies the initial searching mode of the form.")]
		[DefaultValue(typeof(SearchMode), "SearchMode.Forward")]
		[Category("Search")]
		public SearchMode SearchMode {
			get { return searchMode; }
			set {
				if (searchMode == value)
					return;
				searchMode = value;
				if (SearchMode == SearchMode.Forward)
					switchMode.Text = forwards.Text;
				else
					switchMode.Text = backwards.Text;
				UpdateResults();
			}
		}

		public SearchMode DisplayedSearchMode {
			get { return displayedSearchMode; }
			set { displayedSearchMode = value; }
		}
		#endregion

		#region Layout code
		private void UpdateGridPosition() {
			grid.Height = ClientSize.Height - grid.Top;
			grid.Width = ClientSize.Width;
		}
		#endregion

		#region Misc Control Events
		/// <summary>Updates the current search results to reflect the new search terms.</summary>
		private void searchBox_RealTextChanged(object sender, EventArgs e) {
			UpdateResults();
		}

		/// <summary>Switches the current search mode from Forward to Backward (and vice versa), 
		/// and focuses the search field.</summary>
		private void switchMode_Click(object sender, EventArgs e) {
			SwitchMode();
			FocusSearchField();
		}

		private class DraggableRowSet {
			IList<TranslationPair> rows;

			public DraggableRowSet(List<SearchResult> rows) {
				this.rows = rows.ConvertAll(x => new TranslationPair(x.Phrase, x.Translation));
			}

			public override string ToString() {
				var sb = new System.Text.StringBuilder();
				foreach (TranslationPair row in rows) {
					sb.AppendLine(string.Format("{0} -- {1}", row.Phrase, row.Translation));
				}

				return sb.ToString();
			}
		}

		private void grid_MouseDown(object sender, MouseEventArgs e) {
			return;
			if(e.Button == MouseButtons.Left) {
				var hit = grid.HitTest(e.X, e.Y);

				var indices = new List<int>();
                
				if (hit.RowIndex >= 0) {
					// TODO: This code isn't run anyway at the moment, but it should be:
					// grid.Rows.GetRowState(hit.RowIndex) & DataGridViewElementStates.Selected = DataGridViewElementStates.Selected
					if (grid.Rows[hit.RowIndex].Selected)
						foreach (DataGridViewRow row in grid.SelectedRows)
							indices.Add(row.Index);
					else if (ctrlHeld)
						indices.Add(hit.RowIndex);
				} else {
					return;
				}

				if(indices.Count > 0) {
					indices.Sort();
					var rowset = new DraggableRowSet(indices.ConvertAll(i => results[i]));

					DataObject data = new DataObject(rowset);
					data.SetText(rowset.ToString());
					grid.DoDragDrop(data, DragDropEffects.Copy);
				}
			}
		}

		/// <summary>Performs a reverse-lookup of the cell that was double-clicked.</summary>
		/// <remarks>This is somewhat redundant now that the tooltips do this too, but
		/// the tooltips are quite limited, and it is harder to interact with them.</remarks>
		private void grid_CellMouseDoubleClick(object sender, DataGridViewCellEventArgs e) {
			if(results != null && e.RowIndex >= 0) {
				SearchResult sr = results[e.RowIndex];

				var sb = new StringBuilder();
				var otherSide = GetSectionBySearchMode(DisplayedSearchMode == SearchMode.Backward ? SearchMode.Forward : SearchMode.Backward);
				
				foreach (Translation t in sr.Entry.Translations) {
					sb.Append(t.Value);
					sb.Append(": ");
					int n = 0;
					foreach(var rsr in otherSide.Search(t.Value, false, false)) {
						if (rsr.MatchType == MatchType.PerfectMatch) {
							foreach (var tr in rsr.Entry.Translations) {
								if (n++ > 0)
									sb.Append(", ");
								sb.Append(tr.Value);
							}
						}
					}
					sb.AppendLine();
				}

				MessageBox.Show(sb.ToString());
			}
		}
		#endregion

		#region Form events
		/// <summary>
		/// Some keyboard shortcuts to invoke the GC. Quite useful for testing how much memory is really
		/// in use. Also clears the search when Escape is pressed.
		/// </summary>
		private void LookupForm_KeyDown(object sender, KeyEventArgs e) {
			if (e.KeyCode == Keys.F9)
				GC.Collect(GC.MaxGeneration);
			else if (e.KeyCode == Keys.F8)
				GC.Collect(1);
			else if (e.KeyCode == Keys.F7)
				GC.Collect(0);
			else if (e.KeyCode == Keys.Escape)
				searchBox.Text = string.Empty;
			else if (e.KeyCode == Keys.F10) {
				int shared = 0;
				for (int i = 0; i < grid.Rows.Count; ++i)
					if (grid.Rows.SharedRow(i).Index == -1)
						shared++;
				MessageBox.Show(string.Format("Shared rows: {0} of {1}", shared, grid.Rows.Count), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		/// <summary>
		/// The ListBuilder associated with this form was closed, so remove the reference to it.
		/// </summary>
		void listBuilder_Closed(object sender, EventArgs e) {
			((Form)sender).Closed -= new EventHandler(listBuilder_Closed);
			listBuilder = null;
		}

		void LookupForm_Closed(object sender, EventArgs e) {
			if (listBuilder != null)
				listBuilder.Close();
			
			GuiConfiguration.Save();

			RemoveEventHandlers();

			//This is done as a hint to the GC, because forms which are open for a long time 
			//might not trigger a gen2 collection when closing. 
			//We want to reclaim the memory in case there are other windows open.
			//GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized);
			GC.Collect(GC.MaxGeneration); //force it
		}

		void LookupForm_InputLanguageChanged(object sender, InputLanguageChangedEventArgs e) {
			if (targetCulture != null && targetCulture.Equals(e.InputLanguage.Culture)) {
				SearchMode = SearchMode.Backward;
			} else if (sourceCulture != null && sourceCulture.Equals(e.InputLanguage.Culture)) {
				SearchMode = SearchMode.Forward;
			}
		}
		#endregion

		#region Grid methods
		public int SelectedRowCount() {
			// The SelectedRows and SelectedColumns collections can be inefficient [BPSWFDC].
			return grid.Rows.GetRowCount(DataGridViewElementStates.Selected);
		}
		#endregion

		#region Menu Events
		#region File Menu
		/// <summary>Shows the start page. Attempt to find an existing start page,
		/// or create one if none exists.</summary>
		private void showStartPage_Click(object sender, EventArgs e) {
			StartPage.ShowStartPage(null);
		}

		/// <summary>Exit the entire program.</summary>
		/// <remarks>TODO: This has a misleading name, it should be "Exit Szótár" or something.</remarks>
		private void exitToolStripMenuItem_Click(object sender, EventArgs e) {
			Application.Exit();
		}

		/// <summary>Populates the list of recent dictionaries when the File menu opens.</summary>
		/// <remarks>The items it adds are tagged with "MRU", so that they can be removed again later.</remarks>
		void fileMenu_DropDownOpening(object sender, EventArgs e) {
			//Remove existing entries
			var items = fileMenu.DropDownItems;
			for (int i = 0; i < items.Count; ) {
				if ((string)(items[i].Tag) == "MRU")
					items.RemoveAt(i);
				else
					i++;
			}

			//Add new entries
			var mru = GuiConfiguration.RecentDictionaries.Entries;
			var index = items.IndexOf(exitMenuItem);
			if (index == -1)
				index = items.Count;

			int count = 0;
			for (int i = 0; i < mru.Count; ++i) {
				var info = mru[i];
				if (info.Path != this.Dictionary.Path && System.IO.File.Exists(info.Path)) {
					var item = new ToolStripMenuItem(
						mru[i].Name, 
						null, 
						delegate { OpenDictionary(info); }
						);

					item.Tag = "MRU";
					items.Insert(index, item);
					count++;
					index++;
				}
			}

			if (count > 0) {
				var item = new ToolStripSeparator();
				item.Tag = "MRU";
				items.Insert(index, item);
			}
		}
		#endregion

		#region Search Menu
		private void forwards_Click(object sender, EventArgs e) {
			SearchMode = SearchMode.Forward;
			FocusSearchField();
		}

		private void backwards_Click(object sender, EventArgs e) {
			SearchMode = SearchMode.Backward;
			FocusSearchField();
		}

		private void switchModeMenuItem_Click(object sender, EventArgs e) {
			SwitchMode();
			FocusSearchField();
		}

		private void focusSearchField_Click(object sender, EventArgs e) {
			FocusSearchField();
		}

		private void clearSearchToolStripMenuItem_Click(object sender, EventArgs e) {
			searchBox.Text = String.Empty;
		}

		private void ignoreAccentsMenuItem_Click(object sender, EventArgs e) {
			GuiConfiguration.IgnoreAccents = ignoreAccentsMenuItem.Checked;
		}

		private void ignoreCaseMenuItem_Click(object sender, EventArgs e) {
			GuiConfiguration.IgnoreCase = ignoreCaseMenuItem.Checked;
		}

		void ignoreCaseCheck_Click(object sender, EventArgs e) {
			GuiConfiguration.IgnoreCase = ignoreCaseCheck.Checked;
		}

		void ignoreAccentsCheck_Click(object sender, EventArgs e) {
			GuiConfiguration.IgnoreAccents = ignoreAccentsCheck.Checked;
		}

		/// <summary>Navigates to the next exact match in the search results.</summary>
		/// <remarks>Would it perhaps be better to wrap in the case where no more are found?</remarks>
		private void nextPerfectMatch_Click(object sender, EventArgs e) {
			if (results != null) {
				int index = grid.FirstDisplayedScrollingRowIndex;
				while (++index < results.Count) {
					if (results[index].MatchType == MatchType.PerfectMatch) {
						grid.FirstDisplayedScrollingRowIndex = index;
						return;
					}
				}
			}

			System.Media.SystemSounds.Beep.Play();
		}

		/// <summary>Navigates to the previous exact match in the results.</summary>
		/// <seealso cref="nextPerfectMatch_Click"/>
		private void previousPerfectMatch_Click(object sender, EventArgs e) {
			if (results != null) {
				int index = grid.FirstDisplayedScrollingRowIndex;
				while (--index >= 0) {
					if (results[index].MatchType == MatchType.PerfectMatch) {
						grid.FirstDisplayedScrollingRowIndex = index;
						return;
					}
				}
			}

			System.Media.SystemSounds.Beep.Play();
		}
		#endregion

		#region List Menu
		private void newList_Click(object sender, EventArgs e) {
			new ListBuilder().Show();
		}

		private void openList_Click(object sender, EventArgs e) {
			StartPage.ShowStartPage(StartPageTab.WordLists);
		}

		private void importList_Click(object sender, EventArgs e) {
			new Forms.ImportForm().Show();
		}

		/// <summary>
		/// Populates the list of recent Lists. Adds menu items which call OpenRecentFile on click.
		/// </summary>
		private void recentLists_DropDownOpening(object sender, EventArgs e) {
			recentLists.DropDownItems.Clear();

			var recent = new RecentListStore();
			foreach (ListInfo li in recent.GetLists()) {
				var handler = new EventHandler(this.OpenRecentFile);
				var item = new ToolStripMenuItem(li.Name, null, handler);
				item.Tag = li;
				recentLists.DropDownItems.Add(item);
				//recentListsToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem(s, null, this.OpenRecentFile));
			}

			if (recentLists.DropDownItems.Count == 0) {
				var emptyItem = new ToolStripMenuItem(Resources.LookupForm.NoLists);
				emptyItem.Enabled = false;
				recentLists.DropDownItems.Add(emptyItem);
			}
		}

		private void OpenRecentFile(object sender, EventArgs e) {
			ListInfo info = ((sender as ToolStripMenuItem).Tag as ListInfo);
			if(info.ID.HasValue)
				ListBuilder.Open(info.ID.Value);
		}
		#endregion

		#region Dictionary Menu
		private void editInformationToolStripMenuItem_Click(object sender, EventArgs e) {
			new DictionaryInfoEditor(Dictionary, true).ShowDialog();
			UpdateButtonNames();
		}

		private void UpdateButtonNames() {
			//Set the menu items' Text to match the names of the dictionary sections.
			//The mode switch button is based on these.
			forwards.Text = Dictionary.FirstLanguage + "-" + Dictionary.SecondLanguage;
			backwards.Text = Dictionary.SecondLanguage + "-" + Dictionary.FirstLanguage;
			switchMode.Text = SearchMode == SearchMode.Forward ? forwards.Text : backwards.Text;
			grid.Columns[0].HeaderText = displayedSearchMode == SearchMode.Forward ? Dictionary.FirstLanguage : Dictionary.SecondLanguage;
			grid.Columns[1].HeaderText = displayedSearchMode == SearchMode.Forward ? Dictionary.SecondLanguage : Dictionary.FirstLanguage;
		}

		private void importDictionary_Click(object sender, EventArgs e) {
			new Forms.DictionaryImport().Show();
		}
		#endregion

		#region Tools Menu
		private void dictsFolder_Click(object sender, EventArgs e) {
			DataStore.UserDataStore.EnsureDirectoryExists(Configuration.DictionariesFolderName);
			string path = System.IO.Path.Combine(DataStore.UserDataStore.Path, Configuration.DictionariesFolderName);
			System.Diagnostics.Process.Start(path);
		}

		private void charMap_Click(object sender, EventArgs e) {
			System.Diagnostics.Process.Start("charmap.exe");
		}

        private void debugLog_Click(object sender, EventArgs e) {
            LogViewerForm.Open();
        }

		private void options_Click(object sender, EventArgs e) {
			new Forms.Preferences().ShowDialog();
		}
		#endregion

		#region Context Menu
		private void contextMenu_Opening(object sender, CancelEventArgs _) {
			var open = new List<long>();

			addTo.DropDownItems.Clear();

			foreach (Form f in Application.OpenForms) {
				var lb = f as ListBuilder;
				if (lb != null) {
					open.Add(lb.WordList.ID.Value);
					var item = new ToolStripMenuItem(lb.WordList.Name, null, new EventHandler((s, e) => AddToExistingList(lb.WordList.ID.Value)));
					addTo.DropDownItems.Add(item);
				}
			}

			// Clone it, if it exists, or make a new one.
			var recent = Configuration.RecentLists != null ? new List<ListInfo>(Configuration.RecentLists) : new List<ListInfo>();
			recent.RemoveAll(r => open.IndexOf(r.ID.Value) > -1);

			if (recent.Count > 0 && open.Count > 0)
				addTo.DropDownItems.Add(new ToolStripSeparator());

			foreach (var info in recent) {
				var info_ = info; //HACK Copy for closure
				var item = new ToolStripMenuItem(info.Name, null, new EventHandler((s, e) => AddToExistingList(info_.ID.Value)));
				addTo.DropDownItems.Add(item);
			}

			addTo.Visible = addTo.DropDownItems.Count > 0;
		}

		private void AddToExistingList(long listID) {
			AddEntries(ListBuilder.Open(listID));
		}

		IEnumerable<int> GetSelectedIndices() {
			// Enumerate selected rows in index order. (It's faster not to use SelectedRows.)
			for (int index = grid.Rows.GetFirstRow(DataGridViewElementStates.Selected);
				index >= 0;
				index = grid.Rows.GetNextRow(index, DataGridViewElementStates.Selected)) {
				yield return index;
			}
		}

		IEnumerable<SearchResult> GetSelectedResults() {
			for (int index = grid.Rows.GetFirstRow(DataGridViewElementStates.Selected);
				index >= 0;
				index = grid.Rows.GetNextRow(index, DataGridViewElementStates.Selected)) {
				yield return results[index];
			}
		}

		IEnumerable<TranslationPair> GetSelectedTranslationPairs() {
			for (int index = grid.Rows.GetFirstRow(DataGridViewElementStates.Selected);
				index >= 0;
				index = grid.Rows.GetNextRow(index, DataGridViewElementStates.Selected)) {
				yield return new TranslationPair(results[index].Phrase, results[index].Translation);
			}
		}

		private void AddEntries(ListBuilder lb) {
			var entries = new List<TranslationPair>();
			foreach (DataGridViewRow row in grid.SelectedRows) {
				var entry = results[row.Index];
				entries.Add(new TranslationPair(entry.Phrase, entry.Translation));
			}

			// TODO: Maybe pass this method an IEnumerable instead.
			lb.AddEntries(GetSelectedTranslationPairs());
		}

		private void addToList_Click(object sender, EventArgs e) {
			var lb = new ListBuilder();
			AddEntries(lb);
			lb.Show();
		}

		// TODO: This may be better copying as CSV. 
		// TODO: It also needs to support copying in some format that ListBuilder can paste.
		// TODO: Dragging wouldn't hurt either.
		private void copy_Click(object sender, EventArgs e) {
			int rowCount = grid.Rows.GetRowCount(DataGridViewElementStates.Selected);
			if (rowCount <= 0)
				return;

			var sb = new System.Text.StringBuilder(rowCount * 16);

			foreach(SearchResult result in GetSelectedResults()) {
				sb.Append(result.Phrase).Append(" -- ").Append(result.Translation).AppendLine();
			}

			Clipboard.SetText(sb.ToString());
		}

		/// <summary>
		/// Perform a reverse lookup on the first item in the selection. Reverse lookup in this case
		/// refers to the opposite direction to the currently displayed direction (for whatever reason
		/// that may be).
		/// </summary>
		private void reverseLookupToolStripMenuItem_Click(object sender, EventArgs e) {
			int index = grid.Rows.GetFirstRow(DataGridViewElementStates.Selected);
			if (index >= 0) {
				searchMode = DisplayedSearchMode == SearchMode.Forward ? SearchMode.Backward : SearchMode.Forward;
				searchBox.Text = results[index].Translation;
			}
		}
		#endregion
		#endregion

		/// <summary>Finds an existing LookupForm instance for a dictionary.</summary>
		private static LookupForm FindExisting(string path) {
			// Look for an existing form using this dictionary before opening it again.
			foreach (Form form in Application.OpenForms) {
				LookupForm lookupForm = form as LookupForm;
				if (lookupForm != null && lookupForm.Dictionary.Path == path)
					return lookupForm;
			}

			return null;
		}

		public static void OpenDictionary(DictionaryInfo dict) {
			if (dict == null)
				return;

			var existing = FindExisting(dict.Path);
			if(existing != null) {
				existing.BringToFront();
				return;
			}

			try {
				existing = new LookupForm(dict);
				existing.Show();
			} catch (System.IO.IOException e) {
				Errors.CouldNotLoadDictionary(dict, e);
			} catch (DictionaryLoadException e) {
				Errors.CouldNotLoadDictionary(dict, e);
			}
		}

		public static LookupForm OpenDictionary(string path) {
			var existing = FindExisting(path);
			if(existing != null) {
				existing.BringToFront();
				return existing;
			}

			existing = new LookupForm(path);
			existing.Show();
			return existing;
		}
	}

	public enum SearchMode {
		Forward,
		Backward
	}
}