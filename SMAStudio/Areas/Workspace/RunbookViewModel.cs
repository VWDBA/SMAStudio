﻿using SMAStudio.Util;
using SMAStudio.SMAWebService;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using SMAStudio.Resources;
using SMAStudio.Editor;
using SMAStudio.Settings;
using SMAStudio.Models;
using System.Collections.ObjectModel;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using System.Management.Automation.Language;
using System.Windows;

namespace SMAStudio.ViewModels
{
    public class RunbookViewModel : ObservableObject, IDocumentViewModel
    {
        private bool _checkedOut = true;
        private bool _unsavedChanges = false;

        private string _content = string.Empty;  // used for comparsion between the local copy and the remote (to detect changes)
        private string _icon = Icons.Runbook;
        private DateTime _lastFetched = DateTime.MinValue;

        private Runbook _runbook = null;
        private TextDocument _document = null;

        public RunbookViewModel()
        {
            Versions = new List<RunbookVersionViewModel>();
            References = new ObservableCollection<DocumentReference>();
            LoadedVersions = false;

            // The UI thread needs to own the document in order to be able
            // to edit it.
            if (App.Current == null)
                return;

            App.Current.Dispatcher.Invoke(delegate()
            {
                Document = new TextDocument();
            });

            
        }

        /// <summary>
        /// Event triggered when the text changes in the text editor when this runbook
        /// is active.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void TextChanged(object sender, EventArgs e)
        {
            if (sender == null)
                return;

            if (!(sender is CodeTextEditor))
                return;

            var editor = ((CodeTextEditor)sender);

            if (editor.Document == null)
                return;

            if (editor.Document.Text.Equals(_content))
                return;

            UnsavedChanges = true;
        }

        /// <summary>
        /// Retrieve the content of the current runbook version
        /// </summary>
        /// <param name="forceDownload">Forces the application to download new content from the web service instead of using the cached information.</param>
        /// <param name="publishedVersion">Set to true if we want to download the published version of the runbook, otherwise we'll get the draft</param>
        /// <returns>The content of the runbook</returns>
        public string GetContent(bool forceDownload = false, bool publishedVersion = false)
        {
            if (!String.IsNullOrEmpty(Content) && !forceDownload && (DateTime.Now - _lastFetched) < new TimeSpan(0, 30, 0))
                return Content;

            string runbookVersion = "DraftRunbookVersion";
            if (publishedVersion)
                runbookVersion = "PublishedRunbookVersion";

            Core.Log.DebugFormat("Downloading content for runbook '{0}', version: {1}", ID, runbookVersion);

            try
            {
                HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(Uri.AbsoluteUri + "/" + runbookVersion + "/$value");
                if (SettingsManager.Current.Settings.Impersonate)
                {
                    request.Credentials = CredentialCache.DefaultCredentials;
                }
                else
                {
                    request.Credentials = new NetworkCredential(SettingsManager.Current.Settings.UserName, SettingsManager.Current.Settings.GetPassword(), SettingsManager.Current.Settings.Domain);
                }

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                TextReader reader = new StreamReader(response.GetResponseStream());

                Content = reader.ReadToEnd();
                _content = Content;
                _lastFetched = DateTime.Now;

                reader.Close();
            }
            catch (WebException e)
            {
                Core.Log.Error("WebException received when trying to download content of runbook from SMA", e);

                try
                {
                    if (e.Status != WebExceptionStatus.ConnectFailure &&
                        e.Status != WebExceptionStatus.ConnectionClosed)
                    {
                        Content = GetContent(forceDownload, true);
                        _content = Content;
                        _lastFetched = DateTime.Now;
                    }
                }
                catch (WebException ex)
                {
                    Core.Log.Error("Unable to retrieve any content for the runbook. Ignoring this.", ex);
                }
            }

            return Content;
        }

        public void DocumentLoaded()
        {
            
        }

        /// <summary>
        /// Returns a list of parameters this runbook has defined
        /// </summary>
        /// <param name="silent">If set to true, it doesn't show any message boxes etc</param>
        /// <returns></returns>
        public List<UIInputParameter> GetParameters(bool silent = false, bool downloadIfNeeded = false)
        {
            var parameters = new List<UIInputParameter>();
            Token[] tokens;
            ParseError[] parseErrors;

            string runbookContent = GetContent(false, CheckedOut ? false : true);

            var scriptBlock = Parser.ParseInput(runbookContent, out tokens, out parseErrors);

            if ((scriptBlock.EndBlock == null || scriptBlock.EndBlock.Statements.Count == 0))
            {
                if (!silent)
                    MessageBox.Show("Your runbook is broken and it's possible that the runbook won't run. Please fix any errors.", "Error", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                
                return null;
            }

            var functionBlock = (FunctionDefinitionAst)scriptBlock.EndBlock.Statements[0];

            if (functionBlock.Body.ParamBlock != null)
            {
                if (functionBlock.Body.ParamBlock.Parameters == null)
                {
                    Core.Log.InfoFormat("Runbook contains ParamBlock but no Parameters.");
                    return null;
                }

                foreach (var param in functionBlock.Body.ParamBlock.Parameters)
                {
                    try
                    {
                        AttributeBaseAst attrib = null;
                        attrib = param.Attributes[param.Attributes.Count - 1]; // always the last one

                        var input = new UIInputParameter
                        {
                            Name = ConvertToNiceName(param.Name.Extent.Text),
                            Command = param.Name.Extent.Text.Substring(1),                  // Remove the $
                            IsArray = (attrib.TypeName.IsArray ? true : false),
                            TypeName = attrib.TypeName.Name
                        };

                        parameters.Add(input);
                    }
                    catch (Exception ex)
                    {
                        Core.Log.Error("Unable to create a UIInputParameter for a runbook parameter.", ex);

                        if (!silent)
                            MessageBox.Show("An error occurred when enumerating the runbook parameters. Please refer to the logs for more information", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                //parameters = parameters.OrderBy(i => i.Name).ToObservableCollection();
            }

            return parameters;
        }

        private string ConvertToNiceName(string parameterName)
        {
            if (parameterName == null)
                return string.Empty;

            if (parameterName.Length == 0)
                return string.Empty;

            parameterName = parameterName.Replace("$", "");
            parameterName = char.ToUpper(parameterName[0]) + parameterName.Substring(1);

            return parameterName;
        }

        #region Properties
        /// <summary>
        /// Contains a mapping to the Model object of our runbook
        /// </summary>
        public Runbook Runbook
        {
            get { return _runbook; }
            set
            {
                _runbook = value;
                base.RaisePropertyChanged("Title");
                base.RaisePropertyChanged("RunbookName");
            }
        }

        /// <summary>
        /// Contains all versions of the runbook that has been checked in
        /// </summary>
        public List<RunbookVersionViewModel> Versions { get; set; }

        /// <summary>
        /// A mapping to the Runbook ID
        /// </summary>
        public Guid ID
        {
            get { return Runbook.RunbookID; }
            set { Runbook.RunbookID = value; }
        }

        /// <summary>
        /// Title to be shown in the tab and treeview. Will contain (draft) if it's not yet published
        /// and a * if the Runbook contains unsaved work.
        /// </summary>
        public string Title
        {
            get
            {
                string runbookName = (Runbook != null) ? Runbook.RunbookName : string.Empty;

                if (String.IsNullOrEmpty(runbookName))
                    runbookName = "untitled";

                if (UnsavedChanges)
                    runbookName += "*";

                if (CheckedOut)
                    runbookName += " (draft)";

                return runbookName;
            }
        }

        /// <summary>
        /// Name of the Runbook
        /// </summary>
        public string RunbookName
        {
            get
            {
                return (Runbook != null) ? Runbook.RunbookName : "";
            }
        }

        public TextDocument Document
        {
            get { return _document; }
            set
            {
                _document = value;
            }
        }

        /// <summary>
        /// Data bound to the TextArea of AvalonEdit
        /// </summary>
        public TextArea MvvmTextArea
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the content of the Runbook (the actual Powershell script)
        /// </summary>
        public string Content
        {
            get
            {
                string content = "";

                // App is closing
                if (App.Current == null)
                    return string.Empty;

                App.Current.Dispatcher.Invoke(delegate()
                {
                    content = Document.Text;
                });

                return content;
            }
            set
            {
                // App is closing
                if (App.Current == null)
                    return;

                App.Current.Dispatcher.Invoke(delegate()
                {
                    Document.Text = value;
                });

                base.RaisePropertyChanged("Document");

                if (!_content.Equals(value))
                    base.RaisePropertyChanged("UnsavedChanges");
            }
        }

        /// <summary>
        /// Gets or sets whether or not the Runbook is checked out
        /// </summary>
        public bool CheckedOut {
            get { return _checkedOut; }
            set { _checkedOut = value; base.RaisePropertyChanged("CheckedIn"); }
        }

        /// <summary>
        /// Gets the opposite of CheckedOut
        /// </summary>
        public bool CheckedIn
        {
            get { return !CheckedOut; }
        }

        /// <summary>
        /// Gets or sets whether this Runbook contains unsaved work
        /// </summary>
        public bool UnsavedChanges
        {
            get { return _unsavedChanges; }
            set
            {
                _unsavedChanges = value;

                // Set the CachedChanges to false in order for our auto saving engine to store a
                // local copy in case the application crashes
                CachedChanges = false;

                base.RaisePropertyChanged("Title");
            }
        }

        /// <summary>
        /// Set to true if the runbook contains changes that are cached and not saved
        /// </summary>
        public bool CachedChanges
        {
            get;
            set;
        }

        /// <summary>
        /// Set to true if versions has been loaded
        /// </summary>
        public bool LoadedVersions
        {
            get;
            set;
        }

        /// <summary>
        /// Returns the string of Tags defined on the runbook
        /// </summary>
        private string _tags = string.Empty;
        public string Tags
        {
            get { return Runbook != null ? Runbook.Tags : ""; }
            set
            {
                if (!_tags.Equals(value))
                    UnsavedChanges = true;

                _tags = value;

                if (Runbook.Tags == null || !Runbook.Tags.Equals(_tags))
                {
                    Runbook.Tags = value;
                    UnsavedChanges = true;
                }
            }
        }

        /// <summary>
        /// Gets the Uri of the Runbook
        /// </summary>
        public Uri Uri
        {
            get
            {
                return new Uri(SettingsManager.Current.Settings.SmaWebServiceUrl + "/Runbooks(guid'" + Runbook.RunbookID + "')");
            }
        }

        /// <summary>
        /// Icon for a Runbook
        /// </summary>
        public string Icon
        {
            get { return _icon; }
            set { _icon = value; base.RaisePropertyChanged("Icon"); }
        }

        /// <summary>
        /// Last DateTime a key was pressed in the text editor of this runbook instance
        /// </summary>
        public DateTime LastTimeKeyDown
        {
            get;
            set;
        }

        /// <summary>
        /// Set to the ID of the job if the runbook is currently executed otherwise Guid.Empty
        /// </summary>
        public Guid JobID
        {
            get;
            set;
        }

        public ObservableCollection<DocumentReference> References
        {
            get;
            set;
        }

        public int CaretOffset
        {
            get;
            set;
        }

        public bool IsExpanded
        {
            get;
            set;
        }
        #endregion

        /// <summary>
        /// Returns the Runbook Name
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return RunbookName;
        }
    }
}