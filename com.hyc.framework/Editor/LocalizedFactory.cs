using System.Collections.Generic;
using System.Linq;

namespace HYC.Framework.Loc.Editor
{
    /// <summary>
    /// AI / tooling-facing factory for authoring localization Excel tables
    /// programmatically. Accumulates keys × languages and writes a single
    /// .xlsx the importer reads — no Editor UI required. Mirrors the role of
    /// <c>ConfigAssetFactory</c> / <c>BTAssetFactory</c> for their asset kinds.
    /// </summary>
    public static class LocalizedFactory
    {
        /// <summary>Starts a fresh, empty table builder.</summary>
        public static LocalizationTableBuilder Create() => new LocalizationTableBuilder();

        /// <summary>Loads an existing .xlsx into a builder for round-trip editing
        /// (add / fix rows, then Save). Uses the same parse path as the importer.</summary>
        public static LocalizationTableBuilder Load(string path)
        {
            LocalizedExcelReader.ParseExcelFile(path, out var keys, out var langs, out var cells);
            var b = new LocalizationTableBuilder
            {
                DefaultLanguage = LocalizationSettings.DefaultLanguage
            };
            foreach (var l in langs) b.AddLanguage(l);
            foreach (var k in keys) b.AddKey(k);
            foreach (var kv in cells)
                foreach (var t in kv.Value)
                    b.Set(kv.Key, t.Key, t.Value);
            b.SourcePath = path;
            return b;
        }

        /// <summary>One-shot: build a table from a key → (lang → text) map and
        /// write it. <paramref name="languages"/> (if given) fixes column order.</summary>
        public static void WriteExcel(string path,
            Dictionary<string, Dictionary<string, string>> table,
            IEnumerable<string> languages = null,
            string defaultLanguage = null,
            bool import = true)
        {
            var b = Create();
            if (defaultLanguage != null) b.DefaultLanguage = defaultLanguage;
            if (languages != null) foreach (var l in languages) b.AddLanguage(l);
            foreach (var kv in table)
                foreach (var t in kv.Value)
                    b.Set(kv.Key, t.Key, t.Value);
            if (import) b.SaveAndImport(path);
            else b.Save(path);
        }
    }

    /// <summary>
    /// Fluent builder for a localization table. All mutators return <c>this</c>
    /// for chaining. Languages are normalized via
    /// <see cref="LocaleUtil.NormalizeLangHeader"/> so output matches what the
    /// importer expects (e.g. "中文" / "English" → "cn" / "en").
    /// </summary>
    public sealed class LocalizationTableBuilder
    {
        private readonly List<string> _langs = new List<string>();
        private readonly HashSet<string> _langSet = new HashSet<string>();
        private readonly Dictionary<string, Dictionary<string, string>> _cells =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Default language code written to the blob (default "en").</summary>
        public string DefaultLanguage { get; set; } = LocaleUtil.DefaultCode;

        /// <summary>Path the table was loaded from (null for a fresh builder).</summary>
        public string SourcePath { get; internal set; }

        /// <summary>Adds a language column (idempotent; normalized).</summary>
        public LocalizationTableBuilder AddLanguage(string code)
        {
            var norm = LocaleUtil.NormalizeLangHeader(code);
            if (_langSet.Add(norm)) _langs.Add(norm);
            return this;
        }

        /// <summary>Adds a key row (idempotent). Fill text via <see cref="Set"/>.</summary>
        public LocalizationTableBuilder AddKey(string key)
        {
            if (!_cells.ContainsKey(key))
                _cells[key] = new Dictionary<string, string>();
            return this;
        }

        /// <summary>Sets the translation of <paramref name="key"/> in
        /// <paramref name="language"/>; auto-creates the key and language column.</summary>
        public LocalizationTableBuilder Set(string key, string language, string text)
        {
            AddKey(key);
            var norm = LocaleUtil.NormalizeLangHeader(language);
            AddLanguage(norm);
            _cells[key][norm] = text ?? string.Empty;
            return this;
        }

        /// <summary>Sets multiple languages for one key at once.</summary>
        public LocalizationTableBuilder SetMany(string key, IDictionary<string, string> translations)
        {
            if (translations == null) return this;
            foreach (var t in translations) Set(key, t.Key, t.Value);
            return this;
        }

        /// <summary>Removes a key row (and its translations).</summary>
        public LocalizationTableBuilder RemoveKey(string key)
        {
            _cells.Remove(key);
            return this;
        }

        /// <summary>Removes a language column from every key.</summary>
        public LocalizationTableBuilder RemoveLanguage(string code)
        {
            var norm = LocaleUtil.NormalizeLangHeader(code);
            _langSet.Remove(norm);
            _langs.Remove(norm);
            foreach (var row in _cells.Values) row.Remove(norm);
            if (DefaultLanguage == norm)
                DefaultLanguage = _langs.Count > 0 ? _langs[0] : LocaleUtil.DefaultCode;
            return this;
        }

        /// <summary>Current language columns (normalized codes).</summary>
        public IReadOnlyList<string> Languages => _langs;

        /// <summary>Current keys.</summary>
        public IReadOnlyCollection<string> Keys => _cells.Keys;

        /// <summary>Writes the table to <paramref name="path"/> as .xlsx
        /// (single sheet "Localization"). Also records the default language.</summary>
        public void Save(string path)
        {
            LocalizationSettings.DefaultLanguage = DefaultLanguage;
            var cells = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            foreach (var kv in _cells) cells[kv.Key] = kv.Value;
            LocalizedExcelReader.WriteWorkbook(path, _cells.Keys.ToList(), _langs, cells);
            SourcePath = path;
        }

        /// <summary>Writes the table then re-runs the import to refresh blobs.</summary>
        public void SaveAndImport(string path)
        {
            Save(path);
            LocalizedExcelReader.ImportAll();
        }
    }
}
