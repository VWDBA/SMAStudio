﻿using Gemini.Framework;
using Gemini.Framework.Commands;
using SMAStudiovNext.Core;
using SMAStudiovNext.Models;
using SMAStudiovNext.Modules.Shell.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SMAStudiovNext.Services;
using System.Windows;
using SMAStudiovNext.Commands;
using SMAStudiovNext.Exceptions;

namespace SMAStudiovNext.Modules.WindowModule.ViewModels
{
    public sealed class ModuleViewModel : Document, IViewModel, ICommandHandler<SaveCommandDefinition>
    {
        private readonly ModuleModelProxy model;

        public ModuleViewModel(ModuleModelProxy module)
        {
            model = module;

            UnsavedChanges = true;
            Owner = module.Context.Service;

            if (module.ModuleName != null)
                ModuleName = module.ModuleName;
            else
                ModuleName = string.Empty;

            ModuleVersion = "1.0.0.0";
            LongRunningOperation.Stop();
        }

        public override void CanClose(Action<bool> callback)
        {
            if (UnsavedChanges)
            {
                var result = MessageBox.Show("There are unsaved changes in the module object, changes will be lost. Do you want to continue?", "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    callback(false);
                    return;
                }
            }

            callback(true);
        }

        public override string DisplayName
        {
            get
            {
                if (!UnsavedChanges)
                {
                    return model.ModuleName;
                }
                
                return ModuleName.Length > 0 ? ModuleName + "*" : "New Module*";
            }
        }

        public string ModuleName { get; set; }

        public string ModuleUrl { get; set; }

        public string ModuleVersion { get; set; }

        public string Content
        {
            get
            {
                return string.Empty;
            }
        }

        public object Model
        {
            get
            {
                return model;
            }

            set
            {
                throw new NotSupportedException(); // NOTE: Why not make set private?
            }
        }

        public IBackendService Owner
        {
            private get;
            set;
        }

        public bool UnsavedChanges
        {
            get; set;
        }

        async Task ICommandHandler<SaveCommandDefinition>.Run(Command command)
        {
            await Task.Run(delegate ()
            {
                LongRunningOperation.Start();

                model.ViewModel = this;
                model.ModuleName = ModuleName;
                model.ModuleUrl = ModuleUrl;
                model.ModuleVersion = ModuleVersion;

                try
                {
                    Owner.Save(this, command);
                    Owner.Context.AddToModules(model);
                }
                catch (ApplicationException ex)
                {
                    GlobalExceptionHandler.Show(ex);
                }

                // Update the UI to notify that the changes has been saved
                UnsavedChanges = false;
                NotifyOfPropertyChange(() => DisplayName);

                LongRunningOperation.Stop();
            });
        }

        void ICommandHandler<SaveCommandDefinition>.Update(Command command)
        {
            if (UnsavedChanges)
                command.Enabled = true;
            else
                command.Enabled = false;
        }
    }
}
