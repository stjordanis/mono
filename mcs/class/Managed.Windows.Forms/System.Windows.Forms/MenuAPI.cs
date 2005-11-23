// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (c) 2004-2005 Novell, Inc.
//
// Authors:
//	Jordi Mas i Hernandez, jordi@ximian.com
//	Mike Kestner  <mkestner@novell.com>
//

// NOT COMPLETE

using System.Drawing;

namespace System.Windows.Forms {

	/*
		When writing this code the Wine project was of great help to
		understand the logic behind some Win32 issues. Thanks to them. Jordi,
	*/
	internal class MenuTracker {

		bool active;
		public Menu CurrentMenu;
		public Menu TopMenu;
		GrabControl grab_control;

		class GrabControl : Control {

			MenuTracker tracker;

			public GrabControl (MenuTracker owner) : base ()
			{
				tracker = owner;
			}

			protected override void OnMouseDown (MouseEventArgs args)
			{
				tracker.OnClick (new MouseEventArgs (args.Button, args.Clicks, Control.MousePosition.X, Control.MousePosition.Y, args.Delta));
			}

			protected override void OnMouseMove (MouseEventArgs args)
			{
				tracker.OnMotion (new MouseEventArgs (args.Button, args.Clicks, Control.MousePosition.X, Control.MousePosition.Y, args.Delta));
			}
		}

	    	public MenuTracker (Menu top_menu)
		{
			TopMenu = CurrentMenu = top_menu;
			grab_control = new GrabControl (this);
			if (top_menu is ContextMenu) {
				(top_menu as ContextMenu).SourceControl.FindForm ().Controls.AddImplicit (grab_control);
				active = true;
				grab_control.Capture = true;
			} else
				top_menu.Wnd.FindForm ().Controls.AddImplicit (grab_control);
		}

		public void Dispose ()
		{
			grab_control.Parent.Controls.RemoveImplicit (grab_control);
		}

		Point ScreenToMenu (Menu menu, Point pnt)		
		{
			int x = pnt.X;
			int y = pnt.Y;
			XplatUI.ScreenToMenu (menu.Wnd.window.Handle, ref x, ref y);
			return new Point (x, y);
		}	

		void Deactivate ()
		{
			active = false;
			grab_control.Capture = false;
			if (TopMenu is ContextMenu) {
				PopUpWindow puw = TopMenu.Wnd as PopUpWindow;
				puw.HideWindow ();
			} else {
				DeselectItem (TopMenu.SelectedItem);
				(TopMenu as MainMenu).Draw ();
			}
		}

		MenuItem FindItemByCoords (Menu menu, Point pt)
		{
			if (menu is MainMenu)
				pt = ScreenToMenu (menu, pt);
			else
				pt = menu.Wnd.PointToClient (pt);
			foreach (MenuItem item in menu.MenuItems) {
				Rectangle rect = item.bounds;
				if (rect.Contains (pt))
					return item;
			}

			return null;
		}

		MenuItem GetItemAtXY (int x, int y)
		{
			Point pnt = new Point (x, y);
			MenuItem item = FindItemByCoords (TopMenu, pnt);
			if (item == null && TopMenu.SelectedItem != null)
				item = FindSubItemByCoord (TopMenu.SelectedItem, Control.MousePosition);
			return item;
		}

		public void OnClick (MouseEventArgs args)
		{
			MenuItem item = GetItemAtXY (args.X, args.Y);

			if (item == null) {
				Deactivate ();
				return;
			}

			SelectItem (item.Parent, item, true);
			item.PerformClick ();			
			if (item.IsPopup) {
				active = true;
				grab_control.Capture = true;
			} else if (item.Parent is MainMenu)
				active = false;
			else
				Deactivate ();
		}

		public void OnMotion (MouseEventArgs args)
		{
			MenuItem item = GetItemAtXY (args.X, args.Y);

			if (CurrentMenu.SelectedItem == item)
				return;

			grab_control.Capture = active || item != null;

            Rectangle top_bounds = new Rectangle(0, 0, TopMenu.Rect.Width, TopMenu.Rect.Height);
			if (item == null && (!active || top_bounds.Contains (ScreenToMenu (TopMenu, new Point (args.X, args.Y)))))
				DeselectItem (TopMenu.SelectedItem);
			else
				MoveSelection (item);
		}

		void MoveSelection (MenuItem item)
		{
			if (item == null) {
				if (CurrentMenu.SelectedItem.IsPopup)
					return;
				MenuItem old_item = CurrentMenu.SelectedItem;
				if (CurrentMenu != TopMenu)
					CurrentMenu = CurrentMenu.parent_menu;
				DeselectItem (old_item);
			} else {
				if (item.Parent != CurrentMenu.SelectedItem)
					DeselectItem (CurrentMenu.SelectedItem);
				CurrentMenu = item.Parent;
				SelectItem (CurrentMenu, item, active);
			}
		}

		static public bool TrackPopupMenu (Menu menu, Point pnt)
		{
			if (menu.MenuItems.Count <= 0)	// No submenus to track
				return true;				

			menu.Wnd = new PopUpWindow (menu);
			MenuTracker tracker = new MenuTracker (menu);

			menu.Wnd.Location =  menu.Wnd.PointToClient (pnt);

			((PopUpWindow)menu.Wnd).ShowWindow ();

			bool no_quit = true;

			while ((menu.Wnd != null) && menu.Wnd.Visible && no_quit) {
				MSG msg = new MSG ();
				no_quit = XplatUI.GetMessage(ref msg, IntPtr.Zero, 0, 0);
				XplatUI.TranslateMessage(ref msg);
				XplatUI.DispatchMessage(ref msg);				
			}

			if (!no_quit)
				XplatUI.Exit();

			tracker.Dispose ();

			if (menu.Wnd != null) {
				menu.Wnd.Dispose ();
				menu.Wnd = null;
			}

			return true;
		}
	
		void DeselectItem (MenuItem item)
		{			
			if (item == null)
				return;				
			
			item.Status = item.Status &~ DrawItemState.Selected;

			if (item.IsPopup)
				HideSubPopups (item);

			Menu menu = item.Parent;
			if (menu is MainMenu)
				(menu as MainMenu).Draw ();
			else if (menu.Wnd != null)
				menu.Wnd.Invalidate (item.bounds);
		}

		void SelectItem (Menu menu, MenuItem item, bool execute)
		{
			MenuItem prev_item = menu.SelectedItem;
			
			if (prev_item != item) {
				DeselectItem (prev_item);
				if (CurrentMenu != menu)
					CurrentMenu = menu;
				item.Status |= DrawItemState.Selected;			
				if (menu is MainMenu)
					(menu as MainMenu).Draw ();
				else
					menu.Wnd.Invalidate (item.bounds);
				item.PerformSelect ();					
			}
			
			if (execute)
				ExecFocusedItem (menu, item);
		}


		//	Used when the user executes the action of an item (press enter, shortcut)
		//	or a sub-popup menu has to be shown
		void ExecFocusedItem (Menu menu, MenuItem item)
		{
			if (!item.Enabled)
			 	return;			 
			 	
			if (item.IsPopup) {				
				ShowSubPopup (menu, item);
			} else {
				// Execute function
			}
		}

		// Create a popup window and show it or only show it if it is already created
		void ShowSubPopup (Menu menu, MenuItem item)
		{
			if (item.Enabled == false)
				return;

			if (item.Wnd != null)
				item.Wnd.Dispose ();
				
			PopUpWindow puw = new PopUpWindow (item);
			
			Point pnt;
			if (menu is MainMenu)
				pnt = new Point (item.X, item.Y + 1);
			else
				pnt = new Point (item.X + item.Width, item.Y + 1);
			pnt = menu.Wnd.PointToScreen (pnt);
			puw.Location = pnt;
			item.Wnd = puw;

			puw.ShowWindow ();
		}

		static public void HideSubPopups (Menu menu)
		{
			foreach (MenuItem item in menu.MenuItems)
				if (item.IsPopup)
					HideSubPopups (item);

			if (menu.Wnd == null)
				return;

			PopUpWindow puw = menu.Wnd as PopUpWindow;
			puw.Hide ();
		}

		MenuItem FindSubItemByCoord (Menu menu, Point pnt)
		{		
			foreach (MenuItem item in menu.MenuItems) {

				if (item.IsPopup && item.Wnd != null && item.Wnd.Visible && item == menu.SelectedItem) {
					MenuItem result = FindSubItemByCoord (item, pnt);
					if (result != null)
						return result;
				}
					
				if (menu.Wnd == null)
					continue;

				Rectangle rect = item.bounds;
				Point pnt_client = menu.Wnd.PointToScreen (new Point (item.X, item.Y));
				rect.X = pnt_client.X;
				rect.Y = pnt_client.Y;
				
				if (rect.Contains (pnt) == true)
					return item;
			}			
			
			return null;
		}

		static MenuItem FindItemByKey (Menu menu, IntPtr key)
		{
			char key_char = (char ) (key.ToInt32() & 0xff);
			key_char = Char.ToUpper (key_char);

			foreach (MenuItem item in menu.MenuItems) {
				if (item.Mnemonic == '\0')
					continue;

				if (item.Mnemonic == key_char)
					return item;
			}

			return null;
		}

		enum ItemNavigation {
			First,
			Last,
			Next,
			Previous,
		}

		static MenuItem GetNextItem (Menu menu, ItemNavigation navigation)
		{
			int pos = 0;
			bool selectable_items = false;
			MenuItem item;

			// Check if there is at least a selectable item
			for (int i = 0; i < menu.MenuItems.Count; i++) {
				item = menu.MenuItems [i];
				if (item.Separator == false && item.Visible == true) {
					selectable_items = true;
					break;
				}
			}

			if (selectable_items == false)
				return null;

			switch (navigation) {
			case ItemNavigation.First:

				/* First item that is not separator and it is visible*/
				for (pos = 0; pos < menu.MenuItems.Count; pos++) {
					item = menu.MenuItems [pos];
					if (item.Separator == false && item.Visible == true)
						break;
				}

				break;

			case ItemNavigation.Last: // Not used
				break;

			case ItemNavigation.Next:

				if (menu.SelectedItem != null)
					pos = menu.SelectedItem.Index;

				/* Next item that is not separator and it is visible*/
				for (pos++; pos < menu.MenuItems.Count; pos++) {
					item = menu.MenuItems [pos];
					if (item.Separator == false && item.Visible == true)
						break;
				}

				if (pos >= menu.MenuItems.Count) { /* Jump at the start of the menu */
					pos = 0;
					/* Next item that is not separator and it is visible*/
					for (; pos < menu.MenuItems.Count; pos++) {
						item = menu.MenuItems [pos];
						if (item.Separator == false && item.Visible == true)
							break;
					}
				}
				break;

			case ItemNavigation.Previous:

				if (menu.SelectedItem != null)
					pos = menu.SelectedItem.Index;

				/* Previous item that is not separator and it is visible*/
				for (pos--; pos >= 0; pos--) {
					item = menu.MenuItems [pos];
					if (item.Separator == false && item.Visible == true)
						break;
				}

				if (pos < 0 ) { /* Jump at the end of the menu*/
					pos = menu.MenuItems.Count - 1;
					/* Previous item that is not separator and it is visible*/
					for (; pos >= 0; pos--) {
						item = menu.MenuItems [pos];
						if (item.Separator == false && item.Visible == true)
							break;
					}
				}

				break;

			default:
				break;
			}

			return menu.MenuItems [pos];
		}

		public bool ProcessKeys (Menu menu, ref Message msg, Keys keyData)
		{
			MenuItem item;

			switch (keyData) {
			case Keys.Up:
				item = GetNextItem (menu, ItemNavigation.Previous);
				if (item != null)
					SelectItem (menu, item, false);
				break;

			case Keys.Down:
				item = GetNextItem (menu, ItemNavigation.Next);
				if (item != null)
					SelectItem (menu, item, false);
				break;

			/* Menubar selects and opens next. Popups next or open*/
			case Keys.Right:

				// Try to Expand popup first
				if (menu.SelectedItem.IsPopup)
					ShowSubPopup (menu, menu.SelectedItem);
				else if (menu.parent_menu is MainMenu) {
					MenuItem select_item = GetNextItem (menu.parent_menu, ItemNavigation.Next);
					SelectItem (menu.parent_menu, select_item, true);
				}
				break;

			case Keys.Left:

				// Try to Collapse popup first
				if (menu.SelectedItem.IsPopup) {

				} else if (menu.parent_menu is MainMenu) {
					MenuItem select_item = GetNextItem (menu.parent_menu, ItemNavigation.Previous);
					SelectItem (menu.parent_menu, select_item, true);
				}
				break;
				
			case Keys.Return:
				ExecFocusedItem (menu, menu.SelectedItem);
				break;

			default:
				break;
			}

			/* Try if it is a menu hot key */
			item = FindItemByKey (menu, msg.WParam);

			if (item != null) {
				SelectItem (menu, item, false);
				return true;
			}

			return false;
		}
	}

	internal class PopUpWindow : Control
	{
		private Menu menu;

		public PopUpWindow (Menu menu): base ()
		{
			this.menu = menu;
			Paint += new PaintEventHandler (OnPaintPUW);
			SetStyle (ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
			SetStyle (ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
			is_visible = false;
		}

		protected override CreateParams CreateParams
		{
			get {
				CreateParams cp = base.CreateParams;
				cp.Caption = "Menu PopUp";
				cp.Style = unchecked ((int)(WindowStyles.WS_POPUP));
				cp.ExStyle |= (int)(WindowStyles.WS_EX_TOOLWINDOW | WindowStyles.WS_EX_TOPMOST);
				return cp;
			}
		}

		public void ShowWindow ()
		{
			Show ();
			RefreshItems ();
			Refresh ();
		}
		
		private void OnPaintPUW (Object o, PaintEventArgs args)
		{
			ThemeEngine.Current.DrawPopupMenu (args.Graphics, menu, args.ClipRectangle, ClientRectangle);
		}
		
		public void HideWindow ()
		{
			MenuTracker.HideSubPopups (menu);
    			Hide ();
		}

#if false
		protected override bool ProcessCmdKey (ref Message msg, Keys keyData)
		{	
			return MenuTracker.ProcessKeys (menu, ref msg, keyData, tracker);
		}
#endif

		protected override void CreateHandle ()
		{
			base.CreateHandle ();
			RefreshItems ();			
		}		
		
		// Called when the number of items has changed
		internal void RefreshItems ()
		{
			ThemeEngine.Current.CalcPopupMenuSize (DeviceContext, menu);

			if ((Location.X + menu.Rect.Width) > SystemInformation.WorkingArea.Width) {
				Location = new Point (Location.X - menu.Rect.Width, Location.Y);
			}
			if ((Location.Y + menu.Rect.Height) > SystemInformation.WorkingArea.Height) {
				Location = new Point (Location.X, Location.Y - menu.Rect.Height);
			}

			Width = menu.Rect.Width;
			Height = menu.Rect.Height;			
		}
	}
}

