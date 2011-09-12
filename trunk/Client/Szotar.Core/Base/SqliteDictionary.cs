﻿using System;
using System.Data;
using System.Data.SQLite;
using System.Data.Common;
using System.Collections.Generic;

namespace Szotar {
    public class SqliteDictionary : Sqlite.SqliteDatabase, IBilingualDictionary {
        SqliteSection forwardsSection, reverseSection;

        protected SqliteDictionary(string path)
            : base(path) {
            forwardsSection = new SqliteSection(this, 0);
            reverseSection = new SqliteSection(this, 1);
        }

        static Dictionary<string, NullWeakReference<SqliteDictionary>> openDicts
            = new Dictionary<string,NullWeakReference<SqliteDictionary>>();

        public static SqliteDictionary FromPath(string path) {
            // Handle cases where paths are not normalized.
            path = System.IO.Path.GetFullPath(path);

            NullWeakReference<SqliteDictionary> weakRef;
            if(openDicts.TryGetValue(path, out weakRef) && weakRef.IsAlive && weakRef.Target.Connection.State == ConnectionState.Open)
                return weakRef.Target;

            var dict = new SqliteDictionary(path);
            openDicts[path] = new NullWeakReference<SqliteDictionary>(dict);
            return dict;
        }

        public void AddEntries(IEnumerable<Entry> forwards, IEnumerable<Entry> backwards) {
            forwardsSection.AddEntries(forwards);
            reverseSection.AddEntries(backwards);
        }

        protected override void  Dispose(bool disposing) {
            forwardsSection.Dispose();
            reverseSection.Dispose();
 	        base.Dispose(disposing);
        }

        string name;
        public string Name {
            get {
                if (name != null)
                    return name;
                return name = GetMetadata("Name", "").ToString();
            }
            set {
                SetMetadata("Name", name = value);
            }
        }

        string author;
        public string Author {
            get {
                if (author != null)
                    return author;
                return author = GetMetadata("Author", "").ToString();
            }
            set {
                SetMetadata("Author", author = value);
            }
        }

        public new string Path {
            get {
                return base.Path;
            }
            set {
                string oldPath = Path;

                base.Path = System.IO.Path.GetFullPath(value);

                // Modify the list of open dictionaries.
                oldPath = System.IO.Path.GetFullPath(oldPath);
                if (openDicts.ContainsKey(oldPath)) {
                    openDicts.Remove(oldPath);
                    openDicts[Path] = new NullWeakReference<SqliteDictionary>(this);
                }
            }
        }

        string url;
        public string Url {
            get {
                if (url != null)
                    return url;
                return url = GetMetadata("Url", "").ToString();
            }
            set {
                SetMetadata("Url", url = value);
            }
        }

        public DictionaryInfo Info {
            get {
                var epath = Path;
                return new DictionaryInfo {
                    Author = Author,
                    Name = Name,
                    Path = Path,
                    Url = Url,
                    // Make sure the DictionaryInfo doesn't contain a reference to the SqliteDictionary object.
                    GetFullInstance = () => new SqliteDictionary(epath)
                };
            }
        }

        public IDictionarySection ForwardsSection {
            get { return forwardsSection; }
        }

        public IDictionarySection ReverseSection {
            get { return reverseSection; }
        }

        string firstLanguage;
        public string FirstLanguage {
            get {
                if (firstLanguage != null)
                    return firstLanguage;
                return firstLanguage = GetMetadata("FirstLanguage", "").ToString();
            }
            set {
                SetMetadata("FirstLanguage", firstLanguage = value);
            }
        }

        string secondLanguage;
        public string SecondLanguage {
            get {
                if (secondLanguage != null)
                    return secondLanguage;
                return secondLanguage = GetMetadata("SecondLanguage", "").ToString();
            }
            set {
                SetMetadata("SecondLanguage", secondLanguage = value);
            }
        }

        string firstLanguageCode;
        public string FirstLanguageCode {
            get {
                if (firstLanguageCode != null)
                    return firstLanguageCode;
                return firstLanguageCode = GetMetadata("FirstLanguageCode", "").ToString();
            }
            set {
                SetMetadata("FirstLanguageCode", firstLanguageCode = value);
            }
        }

        string secondLanguageCode;
        public string SecondLanguageCode {
            get {
                if (secondLanguageCode != null)
                    return secondLanguageCode;
                return secondLanguageCode = GetMetadata("SecondLanguageCode", "").ToString();
            }
            set {
                SetMetadata("SecondLanguageCode", secondLanguageCode = value);
            }
        }

        public void Save() {
        }

        protected override int ApplicationSchemaVersion() {
            return 1;
        }

        protected override void IncrementalUpgradeSchema(int toVersion) {
            switch (toVersion) {
                case 1: InitDatabase(); break;
                default: throw new ArgumentOutOfRangeException("toVersion");
            }
        }

        void InitDatabase() {
            ExecuteSQL(@"
                CREATE TABLE Phrases (
                    PhraseID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Phrase TEXT NOT NULL,
                    Section INTEGER NOT NULL);
                
                CREATE INDEX Phrases_IndexPS ON Phrases (Phrase, Section);

                CREATE TABLE Translations (
                    PhraseID INTEGER NOT NULL,
                    Translation TEXT NOT NULL);

                CREATE INDEX Translations_IndexT ON Translations (PhraseID);
            ");
        }
    }

    class SqliteSection : Sqlite.SqliteObject, IDictionarySection {
        int section;

        public SqliteSection(SqliteDictionary dict, int section) : base(dict) {
            this.section = section;
        }

        public int HeadWords {
            get {
                return (int)Select(@"SELECT COUNT(*) FROM Phrases WHERE Section = ?", section);
            }
        }

        DbCommand selectTranslations;
        DbParameter selectTranslationsPhraseID;
        public void GetFullEntry(Entry entry) {
            if (entry.Tag != null && entry.Tag.Data != null) {
                if (selectTranslations == null) {
                    selectTranslations = conn.CreateCommand();
                    selectTranslations.CommandText = @"TYPES String; SELECT Translation FROM Translations WHERE PhraseID = ?";
                    selectTranslationsPhraseID = selectTranslations.CreateParameter();
                    selectTranslations.Parameters.Add(selectTranslationsPhraseID);
                }

                selectTranslationsPhraseID.Value = Convert.ToInt32(entry.Tag.Data);

                using (var reader = selectTranslations.ExecuteReader()) {
                    var translations = new List<Translation>();
                    while (reader.Read())
                        translations.Add(new Translation(reader.GetString(0)));

                    entry.Translations = translations;
                }
            }
        }

        public IEnumerable<SearchResult> Search(string search, bool ignoreAccents, bool ignoreCase) {
            string searchString;
            if (string.IsNullOrEmpty(search))
                searchString = "%";
            else
                searchString = "%" + search.Replace("%", "%%") + "%";

            using(var reader = SelectReader(
                // TODO: Better phrase matching
                @"TYPES Integer, String;
                        SELECT PhraseID, Phrase FROM Phrases 
                        WHERE Section = ? AND FilterPhrase(?, ?, Phrase, ?) = 1", section, ignoreCase ? 1 : 0, ignoreAccents ? 1 : 0, search)) {
                
                while (reader.Read()) {
                    var sr = GetSearchResult(search, reader.GetString(1), reader.GetInt32(0));
                    if (sr != null)
                        yield return sr;
                }
            }
        }

        SearchResult GetSearchResult(string search, string phrase, int phraseID) {
            var entry = new Entry(phrase, null);
            var matchType = MatchType.NormalMatch;
            if (entry.Phrase == search)
                matchType = MatchType.PerfectMatch;
            else if (entry.Phrase.StartsWith(search))
                matchType = MatchType.StartMatch;
            if (string.IsNullOrEmpty(search))
                matchType = MatchType.NormalMatch;
            entry.Tag = new EntryTag(this, phraseID);
            return new SearchResult(entry, matchType);
        }

        public IEnumerator<Entry> GetEnumerator() {
            Entry current = null;

            using (var reader = SelectReader(@"TYPES String, String, 
                SELECT Phrase, Translation
                FROM Phrases, Translation
                WHERE Phrases.PhraseID = Translation.TranslationID AND Section = ?", section)) {

                string phrase = reader.GetString(0);
                string translation = reader.GetString(1);

                if (current != null && phrase == current.Phrase) {
                    current.Translations.Add(new Translation(translation));
                } else {
                    if (current != null)
                        yield return current;
                    current = new Entry(phrase, new List<Translation>());
                    current.Tag = new EntryTag(this, null);
                    current.Translations.Add(new Translation(translation));
                }
            }

            if (current != null)
                yield return current;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
            return (System.Collections.IEnumerator)this.GetEnumerator();
        }

        public void AddEntries(IEnumerable<Entry> entries) {
            using (var txn = conn.BeginTransaction()) {
                using (var addPhrase = conn.CreateCommand()) {
                    var phraseParam = addPhrase.CreateParameter();
                    addPhrase.Parameters.Add(phraseParam);
                    var sectionParam = addPhrase.CreateParameter();
                    addPhrase.Parameters.Add(sectionParam);
                    addPhrase.CommandText = @"INSERT INTO Phrases (Phrase, Section) VALUES (?, ?)";
                    sectionParam.Value = section;

                    using (var addTranslation = conn.CreateCommand()) {
                        var ownerPhrase = addTranslation.CreateParameter();
                        addTranslation.Parameters.Add(ownerPhrase);
                        var translationParam = addTranslation.CreateParameter();
                        addTranslation.Parameters.Add(translationParam);
                        addTranslation.CommandText = @"INSERT INTO Translations (PhraseID, Translation) VALUES (?, ?)";

                        foreach (var e in entries) {
                            phraseParam.Value = e.Phrase;
                            addPhrase.ExecuteNonQuery();

                            foreach(var t in e.Translations) {
                                ownerPhrase.Value = GetLastInsertRowID();
                                translationParam.Value = t.Value;
                                addTranslation.ExecuteNonQuery();
                            }
                        }
                    }
                }
                txn.Commit();
            }
        }

        protected override void Dispose(bool disposing) {
            if (selectTranslations != null)
                selectTranslations.Dispose();
            base.Dispose(disposing);
        }
    }

    [SQLiteFunction(Name = "FilterPhrase", FuncType = FunctionType.Scalar, Arguments = 4)]
    public class FilterPhrase : SQLiteFunction {
        public FilterPhrase() {}

        public override object Invoke(object[] args) {
            var x = args[2].ToString();
            var y = args[3].ToString();
            bool ignoreCase = Convert.ToInt32(args[0]) == 1;
            bool ignoreAccents = Convert.ToInt32(args[1]) == 1;

            if (ignoreAccents)
                return Searcher.Contains(x, y, ignoreCase);

           if(x.IndexOf(y, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) >= 0)
               return 1;
           return 0;
        }
    }
}