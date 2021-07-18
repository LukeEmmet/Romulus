using Terminal.Gui;
using SmolNetSharp.Protocols;
using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using Microsoft.Extensions.CommandLineUtils;

namespace Romulus
{
    class Program
    {
        private static MenuItem _homeMenu;
        private static MenuBarItem _bookmarksMenu;
        private static MenuBarItem[] _bookmarkItems;
        private static MenuBarItem[] _structureMenuItems;
        private static MenuBarItem _structureMenu;
        private static Uri _homeUri;
        private static Toplevel _top;
        private static string _aboutFolder;
        private static Uri _initialUri;
        private static Stack<CachedPage> _history;
        private static ListView _lineView;
        private static Uri _currentUri;
        private static Window _win;
        private static MenuBar _menu;
        private static int _charWrap;

        //private static bool insecure;

        static void Main(string[] args)
        {
            CommandLineApplication commandLineApplication = new CommandLineApplication(throwOnUnexpectedArg: false);

            //CommandOption cert = commandLineApplication.Option(
            //  "-c | --cert <path>", "path to pfx certificate",
            //  CommandOptionType.SingleValue);


            //CommandOption insecureFlag = commandLineApplication.Option(
            //  "-i | --insecure", "connect without checking server cert",
            //  CommandOptionType.NoValue);


            //commandLineApplication.HelpOption("-? | -h | --help");

            _charWrap = 75;     //default - can provide UI for ths
            CommandOption charWrap = commandLineApplication.Option(
                "-w | --charWrap", "wrap content at this column",
                CommandOptionType.SingleValue);


            commandLineApplication.ExtendedHelpText = "Romulus <url>";
            string startUrl = "";

            commandLineApplication.OnExecute(() =>
            {
                _homeUri = new Uri("about:home");
                _initialUri = _homeUri;
                
                _charWrap = charWrap.HasValue() ? int.Parse(charWrap.Value()) : 75;

                if (commandLineApplication.RemainingArguments.Count > 0)
                {
                    startUrl = commandLineApplication.RemainingArguments[0].ToString();     //use the first one
                    if (TextIsUri(startUrl))
                    {
                        var candidateUri = new Uri(startUrl);
                        if ((candidateUri.Scheme == "gemini") || (candidateUri.Scheme == "about"))
                        {
                            //these are the only valid starup URls
                            _initialUri = candidateUri;
                        }
                    }
                }

                //insecure = (bool)insecureFlag.HasValue();

                Application.Init();

                _top = Application.Top;

                _history = new Stack<CachedPage>();
                _aboutFolder = AppDomain.CurrentDomain.BaseDirectory;

                _currentUri = null;

                _bookmarkItems = new MenuBarItem[] { };
                _bookmarksMenu = new MenuBarItem(_bookmarkItems);
                _bookmarksMenu.Title = "Book_marks";

                _structureMenuItems = new MenuBarItem[] { };
                _structureMenu = new MenuBarItem(_structureMenuItems);
                _structureMenu.Title = "_Structure";

                _homeMenu = new MenuItem("_Home", "", () => { LoadHandledLink(_homeUri); });
                _homeMenu.Shortcut = Key.AltMask & Key.H;


                // Creates the top-level window to show
                _win = new Window("Romulus Gemini Application")
                {
                    X = 0,
                    Y = 1, // Leave one row for the toplevel menu
                    ColorScheme = Colors.Menu,      //to blend with menu
                    // By using Dim.Fill(), it will automatically resize without manual intervention
                    Width = Dim.Fill(),
                    Height = Dim.Fill()
                };

                _top.Add(_win);

                _lineView = new ListView()
                {
                    X = 2,
                    Y = 1,
                    Height = Dim.Fill() - 1,
                    Width = Dim.Fill() - 1,
                    ColorScheme = Colors.Menu,      //to blend with window and menu background
                };

                _lineView.OpenSelectedItem += (ListViewItemEventArgs e) =>
                {
                    HandleActivate(e.Value, _currentUri);
                };

                _lineView.KeyPress += (View.KeyEventEventArgs e) =>
                {
                    if (e.KeyEvent.Key == Key.Tab)
                    {
                        JumpLink(1);
                        e.Handled = true;
                    }
                    else if (e.KeyEvent.Key == Key.BackTab)
                    {
                        JumpLink(-1);
                        e.Handled = true;
                    }
                };

                // Creates a menubar, the item "New" has a help menu.
                _menu = new MenuBar(new MenuBarItem[] {
                    new MenuBarItem ("_File", new MenuItem [] {
                        new MenuItem("_Open URL", "", () => {OpenUserChosenUri(); }),
                        new MenuItem("_Reload", "", () => {Reload(); }),
                        _homeMenu,
                        new MenuItem ("_Quit", "", () => { if (Quit ()) _top.Running = false; })
                    }),
                    _bookmarksMenu,
                    _structureMenu,
                    new MenuBarItem("_Back", "", () => {GoBack(); }),
                });


                LoadBookmarks();
                _top.Add(_menu);

                // Add some controls, 
                _win.Add(
                     // The ones with a computed layout system,
                     _lineView
                );

                _top.Ready += () =>
                {
                    LoadHandledLink(_initialUri);
                };

                Application.Run();

                return 0;
            });
            commandLineApplication.Execute(args);
        }

        static bool Quit()
        {
            var n = MessageBox.Query(50, 7, "Quit Romulus", "Are you sure you want to quit Romulus?", "Yes", "No");
            return n == 0;
        }

        static void JumpLink(int direction)
        {
            var gemLines = (List<GeminiLine>)_lineView.Source.ToList();
            var selected = _lineView.SelectedItem;
            int curIndex;

            if (direction > 0)
            {
                if (selected == gemLines.Count - 1)
                {
                    return;
                } else
                {
                    curIndex = selected + 1;	//start from next item when scanning forwards
                }
            } else
            {
                if (selected == 0)
                {
                    return;
                } else
                {
                    curIndex = selected - 1;	//start from previous item when scanning backwards
                }
            }

            while ((curIndex >= 0) && (curIndex < _lineView.Source.Count))
            {
                var gemLine = (GeminiLine)gemLines[curIndex]; 
                if (gemLine.LineType == "=>")
                {
                    EnsureVisibleLine(curIndex);
                    return;		//we are done
                }

                if (direction > 0)
                {
                    curIndex++;		//forwards
                } else
                {
                    curIndex--;		//backwards
                }
            }
        }

        //would be nice if this was an intrinsic method for the listview, but it is missing 
        static void EnsureVisibleLine(int lineIndex)
        {
            int topH, winH, listH, menuH;
            
            if (
                ((View)_top).GetCurrentHeight(out topH)
                )
            {
                ((View)_menu).GetCurrentHeight(out menuH);
                ((View)_win).GetCurrentHeight(out winH);
                ((View)_lineView).GetCurrentHeight(out listH);

                //calculate the inner height of the listview based on how we understand the 
                //overall layout, so some assumptions here about window design etc
                int winInnerH = (topH - menuH) - 2;		//2 for window border
                int listInnerH = winInnerH + listH;		//accomodate line view margin

                if (
                    (lineIndex >= _lineView.TopItem) &&
                    (lineIndex <= _lineView.TopItem + listInnerH))
                {
                    //its in view so just highlight it
                } else
                {
                    //scroll to show the item
                    _lineView.TopItem = lineIndex;
                }

                _lineView.SelectedItem = lineIndex;

                //seems to be a bug maybe in terminal.gui that the window is not repainted after the selection
                //so we force a repaint and focus
                _lineView.SetFocus();
                _win.Redraw(_win.Bounds);

            }

        }
        static void BuildStructureMenu(List<GeminiLine> displayLines)
        {
            var bms = new List<MenuItem>();
            var lineNum = 0;
            var accelerators = new List<string>();
            
            foreach (var line in displayLines)
            {
                var n = lineNum;
                if ((line.LineType == "#") || (line.LineType == "##") || (line.LineType == "###"))
                {
                    var m = new MenuItem();
                    var menuPrefix = "";

                    var accel = line.Line.Substring(0, 1);
                    if (!accelerators.Contains(accel))
                    {
                        menuPrefix = "_";
                        accelerators.Add(accel);
                    }
                    bms.Add(new MenuItem()
                    {
                        Title = line.LineType.Replace("#", " ").Substring(1) +
                            menuPrefix + line.Line,

                        Action = () =>
                        {
                            EnsureVisibleLine(n);
                        },
                    }); 
                }

                lineNum++;
            }

            _structureMenu.Children = bms.ToArray();
        }

        static void GoBack()
        {
            if (_history.Count > 1)
            {
                //take off the item on the top of the history (current page)
                //then go to the one behind it from the cache
                _history.Pop();
                var cached = _history.Peek();
                RenderGemini(cached.uri.AbsoluteUri, cached.content, _lineView);
                _lineView.SelectedItem = cached.selected;
                _lineView.ScrollDown(cached.top);
                SetAsCurrent(cached.uri);
            }
        }

        static void SetAsCurrent(Uri uri)
        {
            _currentUri = uri;
            _win.Title = "Romulus: " + _currentUri.Authority + _currentUri.PathAndQuery;

        }

        static void Reload()
        {
            if (_currentUri != null)
            {
                _history.Pop();      //remove current it will be the same as reloaded Uri, so we only get one history entry
                LoadGeminiLink(_currentUri);
            }
        }

        private static void OpenUserChosenUri()
        {
            var userResponse = Dialogs.SingleLineInputBox("Gemini URL", "Enter the Gemini URL to load:", "");
            if ((userResponse.ButtonPressed == TextDialogResponse.Buttons.Ok) && 
                (userResponse.Text != ""))
            {
                var targetUrl = userResponse.Text;
                if (!TextIsUri(targetUrl))
                {
                    if(!targetUrl.StartsWith("gemini://"))     //user may omit scheme and just give domain etc.
                    {
                        targetUrl = "gemini://" + targetUrl;
                    }
                }


                var uri = new Uri(targetUrl);
                LoadHandledLink(uri);
            }
        }

        static bool TextIsUri(string text)
        {
            Uri outUri;

            return  Uri.TryCreate(text, UriKind.Absolute, out outUri);
            
        }

        static string ReadAboutSchemeFile(Uri uri)
        {
            var file = Path.Combine(_aboutFolder, uri.AbsolutePath + ".gmi");        //about:foo is loaded from foo.gmi
            
            if (File.Exists(file))
            {
                return File.ReadAllText(file);
            } else
            {
                return "No such resource: " + uri.AbsoluteUri;
            }
        }

        static void WriteAboutSchemeFile(Uri uri, string content)
        {
            var file = Path.Combine(_aboutFolder, uri.AbsolutePath + ".gmi");        //about:foo is loaded from foo.gmi
            File.WriteAllText(file, content);
        }

        static void LoadAboutLink(Uri uri)
        {
            //special treatment for about: scheme
            if (uri.Scheme == "about")
            {
                var result = ReadAboutSchemeFile(uri);
                RenderGemini(uri.AbsoluteUri, result, _lineView);
                _history.Push(new CachedPage(uri, result, 0, 0));
                SetAsCurrent(uri);
                return;
            }
        }

        static void LoadHandledLink(Uri uri)
        {
            if (uri.Scheme == "gemini")
            {
                LoadGeminiLink(uri);
            }
            else if (uri.Scheme == "about")
            {
                LoadAboutLink(uri);
            }
            else
            {
                //not valid as a page to display
                Dialogs.MsgBoxOK("Loading link", "Not a valid link to display: " + uri.AbsoluteUri);
            }
        }

        static void LoadGeminiLink(Uri uri)
        {
            string result;
            bool retrieved = false;
            GeminiResponse resp;

            resp = new GeminiResponse();

            try
            {
                resp = (GeminiResponse)Gemini.Fetch(uri);
                retrieved = true;
            }
            catch (Exception e)
            {
                //the native gui.cs Messagebox does not resize to show enough content
                //so we use our own that is better
                Dialogs.MsgBoxOK("Gemini error", uri.AbsoluteUri + "\n\n" + e.Message);
            }

            if (retrieved)
            {
                if (resp.codeMajor == '2')
                {
                    //examine the first component of the media type up to any semi colon
                    switch (resp.mime.Split(';')[0].Trim())
                    {
                        case "text/gemini":
                        case "text/plain":
                        case "text/html":
                            {
                                string body = Encoding.UTF8.GetString(resp.bytes.ToArray());

                                result = (body);

                                if (!resp.mime.StartsWith("text/gemini"))
                                {
                                    //display as preformatted text
                                    result = "```\n" + result + "\n```\n";
                                }

                                break;
                            }

                        default: // report the mime type only for now
                            result = ("Some " + resp.mime + " content was received, but cannot currently be displayed.");
                            break;
                    }

                    //render the content and add to history
                    SetAsCurrent(resp.uri);		//remember the final URI, since it may have been redirected.

                    if (_history.Count > 0)
                    {
                        _history.Peek().top = _lineView.TopItem;
                        _history.Peek().selected = _lineView.SelectedItem;        //remember the line offset of the current page
                    }
                    _history.Push(new CachedPage(resp.uri, result, 0, 0));

                    RenderGemini(resp.uri.AbsoluteUri, result, _lineView);
                }
                else if (resp.codeMajor == '1')
                {
                    //input requested from server
                    var userResponse = Dialogs.SingleLineInputBox("Input request from: " + uri.Authority, resp.meta, "");

                    if ((userResponse.ButtonPressed == TextDialogResponse.Buttons.Ok) && (userResponse.Text != ""))
                    {
                        var ub = new UriBuilder(uri);
                        ub.Query = userResponse.Text;

                        LoadGeminiLink(ub.Uri);
                    }
                }
                else if ((resp.codeMajor == '5') && (resp.codeMinor == '1'))
                {
                    //not found
                    Dialogs.MsgBoxOK("Not found", "The resource was not found on the server: \n\n" + resp.uri.AbsoluteUri);

                } else
                {
                    Dialogs.MsgBoxOK("Gemini server response", uri.AbsoluteUri + "\n\n" + "Status: " + resp.codeMajor + resp.codeMinor + ": " + resp.meta);
                }
            }
        }

        static void SubmitNimigem(Uri nimigemUri, byte[] payload, string mime)
        {
            var resp = (NimigemResponse)Nimigem.Fetch(nimigemUri, payload, mime);

            if ((resp.codeMajor == '2') && (resp.codeMinor == '5'))
            {
                //success status 25 - fetch the Gemini target
                LoadGeminiLink(new Uri(resp.meta));
            }
            else
            {
                //everything else is a problem for now
                Dialogs.MsgBoxOK("Error", "Could not send content. Server Message was: " + resp.meta);
            }
        }

        static void HandleNimigemActivate(Uri uri)
        {
            //gather the previous nimigem lines and let the user edit the text

            var preformattedLines = new List<GeminiLine>();
            var exitedPreformatted = false;
            var enteredPreformat = false;
            var foundNimigemEdit = false;

            var currentLine = _lineView.SelectedItem;
            while (currentLine >= 0)
            {
                var gemLine = (GeminiLine)_lineView.Source.ToList()[currentLine];
                if (gemLine.LineType == "```+")
                {
                    enteredPreformat = true;
                    foundNimigemEdit = true;        //strictly speaking this will ignore all nimigem areas that are empty...
                }

                if (enteredPreformat & gemLine.LineType != "```+")
                {
                    exitedPreformatted = true;
                }

                if (!exitedPreformatted && enteredPreformat && gemLine.LineType == "```+")
                {
                    //we found a line in the first preceeding preformatted area
                    preformattedLines.Add(gemLine);

                }

                currentLine--;
            }

            var sb = new StringBuilder();
            preformattedLines.Reverse();

            for (int n = 0; n < preformattedLines.Count; n++)
            {
                var editLine = preformattedLines[n];

                //nimigem spec requires any required preformatted markers inside 
                //editable preformatted areas to be escaped with zero width space
                if (editLine.Line.StartsWith('\u200b'.ToString() + "```"))
                {
                    sb.Append(editLine.Line.Substring(1));      //trim leading zero width space used to escape preformatted markers
                }
                else
                {
                    sb.Append(editLine.Line);
                }

                //append newline to all except the last one
                if (n < preformattedLines.Count - 1)
                {
                    sb.Append("\n");
                }
            }

            if (foundNimigemEdit)
            {
                var userEdit = Dialogs.MultilineInputBox("Nimigem edit", "Edit the text to be sent to: " + uri.AbsoluteUri, sb.ToString());

                if (userEdit.ButtonPressed == TextDialogResponse.Buttons.Ok)
                {
                    try
                    {
                        //send as plain text, utf8
                        SubmitNimigem(uri, Encoding.UTF8.GetBytes(userEdit.Text), "text/plain");
                    }
                    catch (Exception e)
                    {
                        Dialogs.MsgBoxOK("Nimigem error", "Nimigem error: " + e.Message);
                    }

                }
            }
            else
            {
                //No preceding Nimigem editable preformatted area was found.
                //send an empty post to the end point
                try
                {
                    SubmitNimigem(uri, Encoding.UTF8.GetBytes(""), "text/plain");
                }
                catch (Exception e)
                {
                    Dialogs.MsgBoxOK("Nimigem error", "Nimigem error: " + e.Message);
                }
            }
        }

        static bool IsHandledScheme(string link) {
            var result = false;
            Uri outUri;
            if (Uri.TryCreate(link, UriKind.Absolute, out outUri)) {
                
                if (
                    outUri.Scheme == "gemini" ||
                    outUri.Scheme == "nimigem" ||
                    outUri.Scheme == "about" ||
                    outUri.Scheme == "http" ||
                    outUri.Scheme == "https" 
                    
                ) {
                    return true;
                }
           }

            return result;
        }
        static void HandleActivate(Object item, Uri currentUri)
        {
            var line = (GeminiLine)item;
            if (line.LineType == "=>")
            {
                var link = line.Link;

                if (TextIsUri(link) && IsHandledScheme(link))
                {
                    //is a full URL 
                    var uri = new Uri(link);

                    if (uri.Scheme == "gemini")
                    {
                        LoadGeminiLink(uri);
                    }
                    else if (uri.Scheme == "about")
                    {
                        LoadAboutLink(uri);
                    }
                    else if (uri.Scheme == "http" || uri.Scheme == "https")
                    {
                        //launch in the system web browser
                        OpenBrowser(uri.AbsoluteUri);
                    }
                    else if (uri.Scheme == "nimigem")
                    {
                        HandleNimigemActivate(uri);
                    }
                    else
                    {
                        //nothing else handled at the moment
                        Dialogs.MsgBoxOK("Opening link: " + uri.Scheme, uri.Scheme.ToUpper() + " links are not currently handled.");
                    }
                }
                else
                {
                    //is a relative path, build it relative to current
                    var assembledUri = new Uri(currentUri, link);
                    LoadGeminiLink(assembledUri);
                }
            }
        }

        static bool PrettifyWithExtraLine(string sourceline, string lineType, string lastLine, string lastLogicalType, bool preformat)
        {

            //if current or previous actual line is empty, dont add one
            if (sourceline.Trim() == "") { return false; }
            if (lastLine.Trim() == "") { return false; }

            //dont add break when we go into preformat, and
            //dont add break when we close the preformat
            if (preformat && lineType != "```") { return false; }
            if (!preformat && lineType == "```") { return false; }

            //all other contiguous changes of type warrant a new line
            if (lastLogicalType != lineType)
            {
                return true;
            }
            return false;
        }

        static string GetLineType(string sourceline)
        {
            var lineType = "p";

            if (sourceline.StartsWith("###"))
            {
                lineType = "###";
            }
            else if (sourceline.StartsWith("##"))
            {
                lineType = "##";
            }
            else if (sourceline.StartsWith("#"))
            {
                lineType = "#";
            }
            else if (sourceline.StartsWith("=>"))
            {
                lineType = "=>";
            }
            else if (sourceline.StartsWith(">"))
            {
                lineType = ">";
            }
            else if (sourceline.StartsWith("* "))
            {
                lineType = "*";
            }
            else if (sourceline.StartsWith("```"))
            {
                lineType = "```";
            }

            if (sourceline == "")
            {
                lineType = "";
            }

            return lineType;
        }

        static void RenderGemini(string Url, string rawContent, ListView lineView)
        {
            var displayLines = new List<GeminiLine>();

            int lineWidth;
            var useContent = rawContent;
            useContent = useContent.Replace("\r\n", "\n");   //normalise line endings

            var sourcelines = useContent.Split('\n');

            var intColwrap = _charWrap > 30 ? _charWrap : 30;	//min wrap is 30, otherwise at user request

            //we wrap content by fixed size at the moment
            int paraWrap = intColwrap - 2;        //default wrap for paragraphs, which are indented by 2
            int otherWrap = intColwrap - 5;       //for other line types we wrap smaller to accomodate deeper indent by extra 3

            lineWidth = paraWrap;			//default
            bool preformat = false;
            bool isNimigem = false;

            string lastLine = "";
            string lineType = "";
            string lastLogicalType = "";

            displayLines.Add(new GeminiLine("", ""));       //add a blank line at the top for UI reasons - will be the default selected line

            foreach (var sourceline in sourcelines)
            {
                lineType = GetLineType(sourceline);

                if (lineType == "```") { 
                    preformat = !preformat;
                    isNimigem = sourceline.StartsWith("```✏️");			//first character is pencil edit emoji
                }

                if (PrettifyWithExtraLine(sourceline, lineType, lastLine, lastLogicalType, preformat))
                {
                    displayLines.Add(new GeminiLine("", ""));
                }
                if (lineType != "")
                {
                    lastLogicalType = lineType;
                }
                lastLine = sourceline;

                if (lineType != "```")  //dont render toggle lines		
                {
                    if (preformat)
                    {
                        if (isNimigem)
                        {
                            lineType = "```+";
                        }
                        displayLines.Add(new GeminiLine(Utils.TabsToSpaces(sourceline), lineType, "", false, true));
                    }
                    else
                    {
                        lineWidth = otherWrap;		//default assume indented, or a heading

                        var linkTarget = "";
                        var display = sourceline;
                        if (lineType == "=>")
                        {
                            var linkParts = Utils.ParseGeminiLink(sourceline);
                            linkTarget = linkParts[0];
                            display = linkParts[1];
                        }
                        else
                        {
                            if (lineType == "" || lineType == "p")
                            {
                                display = sourceline;
                                lineWidth = paraWrap;		//not indented
                            }
                            else if (sourceline.Length > lineType.Length)
                            {
                                display = sourceline.Substring((lineType.Length)).Trim();
                            }
                            else
                            {
                                display = sourceline;
                            }
                        }

                        var wrapLines = Utils.WordWrap(display, lineWidth);

                        var count = 1;
                        foreach (var wrapLine in wrapLines)
                        {
                            displayLines.Add(new GeminiLine(Utils.TabsToSpaces(wrapLine), lineType, linkTarget, count > 1, false));
                            count++;
                        }
                    }
                }
            }

            BuildStructureMenu(displayLines);
            lineView.SetSource(displayLines);
        }


        // cross platform - launch the system web browser with the supplied url
        // based on https://brockallen.com/2016/09/24/process-start-for-urls-on-net-core/
        // hack because of this: https://github.com/dotnet/corefx/issues/10361
        // with latter follow up to simplify using advice from 
        // https://github.com/dotnet/runtime/issues/17938
        static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    };
                    Process.Start(psi);

                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw;
                }
            }
        }

        static void AddBookmark()
        {
            var bookmarkContent = ReadAboutSchemeFile(_homeUri);
            var found = false;
            var pageTitle = "";

            //check if the bookmark is already in the list
            foreach (var line in bookmarkContent.Split("\n"))
            {
                var lineData = Utils.ParseGeminiLink(line);
                if (lineData[0] == _currentUri.AbsoluteUri)
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                Dialogs.MsgBoxOK("Bookmark exists", "That URL is already in the bookmark list:\n\n" + _currentUri.AbsoluteUri);
            }
            else
            {
                bookmarkContent += "\n=> " + _currentUri.AbsoluteUri;

                if (_structureMenu.Children.Length > 0)
                {
                    pageTitle = _structureMenu.Children[0].Title.ToString();
                    if (pageTitle.StartsWith("_"))
                    {
                        pageTitle = pageTitle.Substring(1);
                    }
                    bookmarkContent += " " + pageTitle + " - " + (_currentUri.Scheme == "about" ? _currentUri.AbsoluteUri : _currentUri.Authority);     //add this for context
                }

                WriteAboutSchemeFile(_homeUri, bookmarkContent);

                if (_currentUri.AbsoluteUri == _homeUri.AbsoluteUri)
                {
                    //user added link to home page, so reload it
                    var homeOffset = _lineView.TopItem;
                    var homeSelected = _lineView.SelectedItem;
                    Reload();
                    _lineView.TopItem = homeOffset;
                    _lineView.SelectedItem = homeSelected;
                }

                LoadBookmarks();
                Dialogs.MsgBoxOK("Bookmark added", "Bookmark added to: " + _currentUri.AbsoluteUri);
            }
        }

        static void LoadBookmarks()
        {
            var homeLines = ReadAboutSchemeFile(_homeUri).Split("\n");
            var bms = new List<MenuItem>();

            bms.Add(new MenuItem()
            {
                Title = "Add bookmark",
                Action = () =>
                {
                    AddBookmark();
                }
            });

            bms.Add(new MenuItem()
            {
                Title = "─────────────────"		//simplistic separator
            });

            foreach (var line in homeLines)
            {
                var linkinfo = Utils.ParseGeminiLink(line);
                if (linkinfo[0] != null)
                {
                    bms.Add(new MenuItem()
                    {
                        Title = "_" + linkinfo[1],
                        Action = () =>
                        {
                            LoadGeminiLink(new Uri(linkinfo[0]));
                        }
                    });
                }
            }
            _bookmarksMenu.Children = bms.ToArray();
        }



        public class CachedPage
        {
            public Uri uri;
            public string content;
            public int top;
            public int selected;

            public CachedPage(Uri pageUri, string pageContent, int topItem, int selectedItem)
            {
                uri = pageUri;
                content = pageContent;
                top = topItem;
                selected = selectedItem;
            }
        }

        public class GeminiLine
        {
            private string _display;
            private string _lineType;
            private string _link;
            private string _line;

            private string Space(int repeat)
            {
                var s = "";
                for (var n = 1; n <= repeat; n++)
                {
                    s = s + " ";
                }
                return s;
            }

            public GeminiLine(string line, string lineType, string linkTarget = "", bool isWrappedLine = false, bool preformat = false)
            {
                _lineType = lineType;
                _line = line;

                var prefix = "";
                var text = "";
                const int paraIndent = 2;
                const int bulletIndent = 3;
                const int linkIndent = 3;
                const int preformatIndent = 4;
                const int quoteIndent = 3;

                if (preformat)
                {
                    _display = Space(preformatIndent) + line;
                    return;
                }

                text = line;

                if (lineType == "=>")
                {
                    _link = linkTarget;

                    var linkGlyph = "»";        //right chevron

                    //handle schemeless absolute links explicitly
                    if (_link.StartsWith("//"))
                    {
                        _link = "gemini:" + _link;
                    }

                    //label non gemini links
                    Uri outUri;
                    if (Uri.TryCreate(_link, UriKind.Absolute, out outUri))
                    {
                        //is a full URL
                        if (
                                (outUri.Scheme != "gemini") && (outUri.Scheme != "about")	//these ones are "normally decorated links"
                                && (outUri.Scheme != "file")        //when running on linux, it seems unschemed relative paths may be converted to file:// scheme links not relative paths
                        )
                        {
                            linkGlyph = "►";        //widely supported even in windows shell, unlike some other glyphs

                            if (outUri.Scheme != "http" && outUri.Scheme != "https")
                            {
                                text += " (" + outUri.Scheme + ")";
                            }

                        }
                    }

                    prefix = Space(linkIndent) + (!isWrappedLine ? linkGlyph + " " : "  ");

                }
                else if (lineType == ">")
                {
                    prefix = Space(quoteIndent) + (!isWrappedLine ? "│ " : "│ ");
                }
                else if (lineType == "*")
                {
                    prefix = Space(bulletIndent) + (!isWrappedLine ? "• " : "  ");
                }
                else if (lineType == "#")
                {
                    text = text.ToUpper();
                }
                else if (lineType == "##")
                {
                    text = line;
                }
                else if (lineType == "###")
                {
                    text = "~ " + text + " ~";
                }
                else if (lineType == "")
                {
                    text = "";
                }
                else
                {
                    //normal para
                    prefix = Space(paraIndent);
                }
                _display = prefix + Utils.TabsToSpaces(text);

            }

            public override string ToString()
            {
                return _display;
            }

            public string Line
            {
                get
                {
                    return _line;
                }
            }

            public string LineType
            {
                get
                {
                    return _lineType;
                }
            }

            public string Link
            {
                get
                {
                    return _link;
                }
            }
        }
    }
}
