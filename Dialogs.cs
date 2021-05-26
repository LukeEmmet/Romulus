using System;
using System.Collections.Generic;
using System.Text;
using Terminal.Gui;


namespace Romulus
{

	public class TextDialogResponse
	{
		public enum Buttons
		{
			Ok,
			Cancel
		}

		public Buttons ButtonPressed;
		public string Text;
	}

	public static class Dialogs
	{

		public static void MsgBoxOK(string title, string message)
		{
			var label = new Label()
			{
				X = 1,
				Y = 1,
				Width = Dim.Fill(),
				Height = 4
			};
			label.Text = message;

			var ok = new Button(24, 7, "Ok");
			ok.HotKey = Key.Enter;
			ok.Clicked += () => { Application.RequestStop(); };

			var dialog = new Dialog(title, 60, 11, ok);

			dialog.Add(label);

			Application.Run(dialog);
		}


		//single line text input, pressing returns is same as OK
		public static TextDialogResponse SingleLineInputBox(string title, string prompt, string initialValue)
		{
			bool okpressed = false;

			var label = new Label()
			{
				X = 1,
				Y = 1,
				Width = Dim.Fill(),
				Height = 2
			};
			label.Text = prompt;

			var entry = new TextField()
			{
				X = 1,
				Y = 4,
				Width = Dim.Fill() - 1,
				ColorScheme = Colors.TopLevel,
				Height = 1
			};

			entry.Text = initialValue;
			entry.KeyDown += (View.KeyEventEventArgs keyEvent) =>
			{
				if (keyEvent.KeyEvent.Key == Key.Enter)
				{
					Application.RequestStop();
					okpressed = true;
				}
			};

			var ok = new Button(18, 6, "Ok");
			ok.HotKey = Key.Enter;

			var cancel = new Button(25, 6, "Cancel");
			cancel.HotKey = Key.Esc;

			ok.Clicked += () => { Application.RequestStop(); okpressed = true; };
			cancel.Clicked += () => { Application.RequestStop(); };

			var dialog = new Dialog(title, 60, 10, ok, cancel);

			dialog.Add(label);
			dialog.Add(entry);

			entry.SetFocus();

			Application.Run(dialog);

			if (okpressed)
			{
				return new TextDialogResponse()
				{
					ButtonPressed = TextDialogResponse.Buttons.Ok,
					Text = entry.Text.ToString()
				};
			}
			else
			{
				return new TextDialogResponse()
				{
					ButtonPressed = TextDialogResponse.Buttons.Cancel,
					Text = ""
				};
			}
		}

		private static void ConditionalFocusTo(View.KeyEventEventArgs e, View target, Key handleKey)
        {
			if (e.KeyEvent.Key == handleKey)
            {
				e.Handled = true;  //to ensure no further processing of the event
				target.SetFocus();
			}
        }

		//multiline text input with word wrap
		public static TextDialogResponse MultilineInputBox(string title, string prompt, string initialValue)
		{
			bool okpressed = false;

			CheckBox checkboxWrap;
			Button ok;
			Button cancel;
			TextView entry = new TextView();
			Label label;

			label = new Label()
			{
				X = 1,
				Y = 1,
				Width = Dim.Fill(),
				Height = 1,
				Text = prompt
			};

			 checkboxWrap = new CheckBox("_Wrap text")
			{
				X = 1,
				Y = 3,
				TabIndex = 0
			};

			checkboxWrap.KeyPress += (View.KeyEventEventArgs e) =>
			{
				ConditionalFocusTo(e, entry, Key.Tab);
			};

			var wrapHint = new Label()
			{
				X = 14,
				Y = 3,
				Width = Dim.Fill(),
				Text = ""
			};

			ok = new Button("Ok")
			{
				HotKey = Key.Enter,
				TabIndex = 2
			};
			ok.KeyPress += (View.KeyEventEventArgs e) =>
			{
				ConditionalFocusTo(e, entry, Key.BackTab);
			};


			cancel = new Button("Cancel")
			{
				HotKey = Key.Esc,
				TabIndex = 3
			};
			entry = new TextView()
			{
				X = 1,
				Y = 4,
				Width = Dim.Fill() - 2,
				Height = Dim.Fill() - 2,
				ColorScheme = Colors.TopLevel,      //green text on black with a visible cursor in the editor, unlinke some other styles
				Text = initialValue,
				WordWrap = false,       //seems to be a bug in the textformatter, we must set this only after setting the text
				TabIndex = 1
			};

			//in theory these should allow tabbing from multiline text edit to next control on the dialog, but not working
			//entry.AllowsTab = false;
			//entry.AllowsReturn = true;
			//instead we handle tab presses explicitly ourselves
			entry.KeyPress += (View.KeyEventEventArgs e) =>
			{
				ConditionalFocusTo(e, ok, Key.Tab);
				ConditionalFocusTo(e, checkboxWrap, Key.BackTab);
			};




			ok.Clicked += () => { Application.RequestStop(); okpressed = true; };
			cancel.Clicked += () => { Application.RequestStop(); };
			checkboxWrap.Toggled += (bool previous) =>
			{
				entry.WordWrap = !previous;

				if (entry.WordWrap)
				{
					wrapHint.Text = "n.b using backspace to delete lines is quirky, use DEL instead or resize window";
				}
				else
				{
					wrapHint.Text = " ";
				}

			};

			var dialog = new Dialog(title, ok, cancel)
			{
				Height = Dim.Fill() - 10,
				Width = Dim.Fill() - 10,
				LayoutStyle = LayoutStyle.Computed
			};




			dialog.Add(label);
			dialog.Add(checkboxWrap);
			dialog.Add(wrapHint);
			dialog.Add(entry);

			entry.SetFocus();

			Application.Run(dialog);

			if (okpressed)
			{
				return new TextDialogResponse()
				{
					ButtonPressed = TextDialogResponse.Buttons.Ok,
					Text = entry.Text.ToString()
				};
			}
			else
			{
				return new TextDialogResponse()
				{
					ButtonPressed = TextDialogResponse.Buttons.Cancel,
					Text = ""
				};
			}
		}


	}
}
