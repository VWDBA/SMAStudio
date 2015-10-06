﻿using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SMAStudio.Editor.CodeCompletion;
using SMAStudio.Editor.CodeCompletion.DataItems;
using SMAStudio.Language;
using SMAStudio.Resources;
using SMAStudio.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;

namespace SMAStudio.Editor
{
    public class CodeTextEditor : TextEditor, IDisposable, INotifyPropertyChanged
    {
        private CompletionWindow _completionWindow;
        private IWorkspaceViewModel _workspaceViewModel;

        public CodeTextEditor()
        {
            TextArea.TextEntering += OnTextEntering;
            TextArea.TextEntered += OnTextEntered;

            Background = (Brush)new BrushConverter().ConvertFrom("#1e1e1e");
            Foreground = Brushes.White;
            ShowLineNumbers = true;

            _workspaceViewModel = Core.Resolve<IWorkspaceViewModel>();

            if (_workspaceViewModel.CurrentDocument is RunbookViewModel)
            {
                ((RunbookViewModel)_workspaceViewModel.CurrentDocument).MvvmTextArea = TextArea;
            }

            #region Load Syntax Highlighting definition
            var dir = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SMAStudio.Xshd.SMA.xshd";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            using (XmlTextReader reader = new XmlTextReader(stream))
            {
                SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);

            }
            #endregion

            Context = new PowershellContext();

            var ctrlSpace = new RoutedCommand();
            ctrlSpace.InputGestures.Add(new KeyGesture(Key.Space, ModifierKeys.Control));
            var cb = new CommandBinding(ctrlSpace, OnCtrlSpaceCommand);

            this.CommandBindings.Add(cb);

            _workspaceViewModel.CurrentDocument.DocumentLoaded();
        }

        public PowershellContext Context { get; set; }

        #region Code Completion Event Handlers
        private void OnTextEntering(object sender, TextCompositionEventArgs e)
        {
            if (e.Text.Length > 0 && _completionWindow != null)
            {
                if (!char.IsLetterOrDigit(e.Text[0]) && e.Text[0] != '-')
                {
                    // Whenever a non-letter is typed while the completion window is open,
                    // insert the currently element.
                    //_completionWindow.CompletionList.RequestInsertion(e);
                }
            }
            else if (e.Text.Length > 0)
                Context.SetContent(Text);
        }

        private void OnTextEntered(object sender, TextCompositionEventArgs e)
        {
            ShowCompletion(e.Text, false);
        }

        private void OnCtrlSpaceCommand(object sender, ExecutedRoutedEventArgs e)
        {
            ShowCompletion(null, true);
        }

        private void ShowCompletion(string text, bool controlSpace)
        {
            if (_completionWindow != null)
                return;

            if (!controlSpace && text.Trim().Length == 0)
                return;

            List<string> data = null;
            var completionType = 0;

            // Cmdlets and variables
            if (text != null && text.StartsWith("$"))
            {
                // Variables
                data = Context.GetVariables(CaretOffset, text);
                completionType = 1;
            }
            else if (text != null && text.StartsWith("-"))
            {
                // Parameters
                completionType = 2;
            }
            else
            {
                // Cmdlets and stuff
                data = text != null ? Context.GetCmdlets(CaretOffset, text) : Context.GetCmdlets(CaretOffset);
                completionType = 3;

                if (text != null)
                {
                    var componentsViewModel = Core.Resolve<IComponentsViewModel>();
                    data.AddRange(componentsViewModel.Runbooks.Where(r => r.RunbookName.StartsWith(text, StringComparison.InvariantCultureIgnoreCase)).Select(r => r.RunbookName).ToList());

                    data.Sort();
                }
            }

            if (data == null || (data != null && data.Count == 0))
                return;

            _completionWindow = new CompletionWindow(TextArea);
            _completionWindow.CloseWhenCaretAtBeginning = true;
            _completionWindow.MinWidth = 260;

            if (text != null)
                _completionWindow.StartOffset -= text.Length;

            foreach (var item in data)
            {
                var completionData = new CompletionData(item);

                switch (completionType)
                {
                    case 1:
                        completionData.Image = Icons.GetImage(Icons.Variable);
                        break;
                    case 2:
                        completionData.Image = Icons.GetImage(Icons.Tag);
                        break;
                    case 3:
                        completionData.Image = Icons.GetImage(Icons.Runbook);
                        break;
                }

                _completionWindow.CompletionList.CompletionData.Add(completionData);
            }

            if (text != null)
                _completionWindow.CompletionList.SelectItem(text);

            /*if (results.TriggerWordLength > 0)
            {
                _completionWindow.CompletionList.SelectItem(results.TriggerWord);
            }*/

            _completionWindow.Show();
            _completionWindow.Closed += (o, args) => _completionWindow = null;

            /*if (Completion == null)
            {
                Core.Log.DebugFormat("Code completion is null, cannot run code completion.");
                return;
            }

            if (_completionWindow == null)
            {
                CodeCompletionResult results = null;
                try
                {
                    int offset = 0;
                    var doc = GetCompletionDocument(out offset);
                    results = Completion.GetCompletions(doc, offset, controlSpace, null);
                }
                catch (Exception e)
                {
                    Core.Log.Error("Error while getting code completion: ", e);
                }

                if (results == null)
                    return;

                if (_completionWindow == null && results != null && results.CompletionData.Any())
                {
                    _completionWindow = new CompletionWindow(TextArea);
                    _completionWindow.CloseWhenCaretAtBeginning = true;
                    _completionWindow.MinWidth = 260;
                    _completionWindow.StartOffset -= results.TriggerWordLength;

                    IList<ICompletionData> data = _completionWindow.CompletionList.CompletionData;
                    foreach (var completion in results.CompletionData.OrderBy(item => item.Text))
                    {
                        data.Add(completion);
                    }

                    if (results.TriggerWordLength > 0)
                    {
                        _completionWindow.CompletionList.SelectItem(results.TriggerWord);
                    }

                    _completionWindow.Show();
                    _completionWindow.Closed += (o, args) => _completionWindow = null;
                }
            }*/
        }

        /// <summary>
        /// Gets the document used for code completion, can be overridden to provide a custom document
        /// </summary>
        /// <param name="offset"></param>
        /// <returns>The document of this text editor.</returns>
        protected virtual TextDocument GetCompletionDocument(out int offset)
        {
            offset = CaretOffset;
            return Document;
        }
        #endregion

        #region Dependency Properties
        public static DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(CodeTextEditor),
                // binding changed callback: set value of underlying property
            new PropertyMetadata((obj, args) =>
            {
                CodeTextEditor target = (CodeTextEditor)obj;
                target.Text = (string)args.NewValue;
            })
        );

        public static DependencyProperty CaretOffsetProperty =
            DependencyProperty.Register("CaretOffset", typeof(int), typeof(CodeTextEditor),
                // binding changed callback: set value of underlying property
            new PropertyMetadata((obj, args) =>
            {
                CodeTextEditor target = (CodeTextEditor)obj;
                target.CaretOffset = (int)args.NewValue;
            })
        );
        #endregion

        #region Properties
        public new string Text
        {
            get { return base.Text; }
            set { base.Text = value; }
        }

        public new int CaretOffset
        {
            get { return base.CaretOffset; }
            set { base.CaretOffset = value; }
        }
        #endregion

        #region PropertyChangedEventHandler
        public event PropertyChangedEventHandler PropertyChanged;
        public void RaisePropertyChanged(string info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }
        #endregion

        public void Dispose()
        {
            
        }
    }
}
