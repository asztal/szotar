using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;

namespace Szotar {
	/// <summary>
	/// Represents a read-write dictionary.
	/// </summary>
	public interface IBilingualDictionary : IDisposable {
		string Name { get; set; }
		string Author { get; set; }
		string Path { get; set; }
		string Url { get; set; }

		DictionaryInfo Info { get; }

		IDictionarySection ForwardsSection { get; }
		IDictionarySection ReverseSection { get; }

		string FirstLanguage { get; set; }
		string SecondLanguage { get; set; }
		string FirstLanguageCode { get; set; }
		string SecondLanguageCode { get; set; }

		void Save();
	}

	public static class Dictionary {
		public static IEnumerable<DictionaryInfo> GetAll() {
			foreach (FileInfo file in DataStore.CombinedDataStore.GetFiles
					 (Configuration.DictionariesFolderName, new System.Text.RegularExpressions.Regex(@"\.dict$"), true)) {

				DictionaryInfo info = null;
				try {
					info = new SimpleDictionary.Info(file.FullName);
				} catch (IOException e) {
					ProgramLog.Default.AddMessage(LogType.Warning, "Failed loading dictionary info for {0}: {1}", file.FullName, e.Message);
				}

				if(info != null)
					yield return info;
			}

			yield break;
		}
	}

	public interface IDictionarySection : ISearchDataSource {
		int HeadWords { get; }

		void GetFullEntry(Entry entry);
	}

	[Serializable]
	public class DictionaryLoadException : Exception {
		public DictionaryLoadException() { }
		public DictionaryLoadException(string message) : base(message) { }
		public DictionaryLoadException(string message, Exception inner) : base(message, inner) { }
		protected DictionaryLoadException(
		  System.Runtime.Serialization.SerializationInfo info,
		  System.Runtime.Serialization.StreamingContext context)
			: base(info, context) { }
	}

	[Serializable]
	public class DictionarySaveException : Exception {
		public DictionarySaveException() { }
		public DictionarySaveException(string message) : base(message) { }
		public DictionarySaveException(string message, Exception inner) : base(message, inner) { }
		protected DictionarySaveException(
		  System.Runtime.Serialization.SerializationInfo info,
		  System.Runtime.Serialization.StreamingContext context)
			: base(info, context) { }
	}
}