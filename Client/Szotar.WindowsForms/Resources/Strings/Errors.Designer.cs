﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:2.0.50727.1434
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Szotar.WindowsForms.Resources {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "2.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Errors {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Errors() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
					global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Szotar.WindowsForms.Resources.Strings.Errors", typeof(Errors).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Cannot upgrade the database to version {0}.
        /// </summary>
        internal static string CannotUpgradeDatabaseToVersion {
            get {
                return ResourceManager.GetString("CannotUpgradeDatabaseToVersion", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to An error occurred when loading the dictionary {0}. The error was &quot;{1}&quot;..
        /// </summary>
        internal static string CouldNotLoadDictionary {
            get {
                return ResourceManager.GetString("CouldNotLoadDictionary", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Error saving list.
        /// </summary>
        internal static string CouldNotSaveListCaption {
            get {
                return ResourceManager.GetString("CouldNotSaveListCaption", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Couldn&apos;t save the list to the file &quot;{0}&quot;. {1} The error was: {2}.
        /// </summary>
        internal static string CouldNotSaveListMessage {
            get {
                return ResourceManager.GetString("CouldNotSaveListMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The import failed with the message: {1}{0}.
        /// </summary>
        internal static string ImportFailedWithMessage {
            get {
                return ResourceManager.GetString("ImportFailedWithMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The import had not begun..
        /// </summary>
        internal static string ImportHadNotBegun {
            get {
                return ResourceManager.GetString("ImportHadNotBegun", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to The operation was cancelled..
        /// </summary>
        internal static string TheOperationWasCancelled {
            get {
                return ResourceManager.GetString("TheOperationWasCancelled", resourceCulture);
            }
        }
    }
}
