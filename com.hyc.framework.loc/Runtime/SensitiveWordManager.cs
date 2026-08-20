using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace HYC.Framework.Loc
{
    /// <summary>
    /// Sensitive-word filter backed by a trie (Hashtable) built from
    /// <see cref="LocalizationManager.SensitiveWords"/>. Provides filtering,
    /// validation and word enumeration.
    /// </summary>
    public static class SensitiveWordManager
    {
        private const string END_FLAG = "IsEnd";

        private static Hashtable mMaskWordTable;

        /// <summary>Initializes the trie from the loaded sensitive-word list.</summary>
        public static void Init()
            => InitSensitiveWordMap(LocalizationManager.SensitiveWords);

        /// <summary>Clears the trie.</summary>
        public static void Clean()
        {
            mMaskWordTable?.Clear();
            mMaskWordTable = null;
        }

        /// <summary>Returns <paramref name="info"/> with sensitive words masked as '*'.</summary>
        public static string Filter(this string info)
        {
            if (mMaskWordTable == null)
                InitSensitiveWordMap(LocalizationManager.SensitiveWords);
            return ReplaceSensitiveWords(info);
        }

        /// <summary>Whether <paramref name="value"/> contains any invalid content.</summary>
        public static bool Validate(this string value)
        {
            if (mMaskWordTable == null)
                InitSensitiveWordMap(LocalizationManager.SensitiveWords);

            for (int i = 0; i < value.Length; i++)
            {
                int length = SearchSensitiveWord(value, i);
                if (length > 0)
                {
                    bool isChange = true;
                    string input = value.Substring(i, length);
                    if (IsAllLetters(input))
                    {
                        if ((i + length < value.Length && IsAllLetters(value[i + length]))
                            || (i > 0 && IsAllLetters(value[i - 1])))
                            isChange = false;
                    }
                    if (isChange) return true;
                    i += length - 1;
                }
            }
            return false;
        }

        /// <summary>Returns all sensitive words found within <paramref name="value"/>.</summary>
        public static List<string> GetAllMaskWords(this string value)
        {
            if (mMaskWordTable == null)
                InitSensitiveWordMap(LocalizationManager.SensitiveWords);

            var result = new List<string>();
            for (int i = 0; i < value.Length; i++)
            {
                int length = SearchSensitiveWord(value, i);
                if (length > 0)
                {
                    bool isChange = true;
                    string input = value.Substring(i, length);
                    if (IsAllLetters(input))
                    {
                        if ((i + length < value.Length && IsAllLetters(value[i + length]))
                            || (i > 0 && IsAllLetters(value[i - 1])))
                            isChange = false;
                    }
                    if (isChange) result.Add(value.Substring(i, length));
                    i += length - 1;
                }
            }
            return result;
        }

        private static bool IsAllLetters(string s)
        {
            foreach (var c in s)
                if (!char.IsLetter(c))
                    return false;
            return true;
        }

        private static bool IsAllLetters(char c)
            => Regex.IsMatch(c.ToString(), @"^[a-zA-Z]$");

        private static void InitSensitiveWordMap(string[] words)
        {
            mMaskWordTable = new Hashtable(words.Length);
            foreach (var word in words)
            {
                Hashtable hashtable = mMaskWordTable;
                for (int i = 0; i < word.Length; i++)
                {
                    char c = word[i];
                    if (IsSymbol(c)) continue;
                    if (hashtable.ContainsKey(c))
                        hashtable = (Hashtable)hashtable[c];
                    else
                    {
                        var newHashtable = new Hashtable { { END_FLAG, 0 } };
                        hashtable.Add(c, newHashtable);
                        hashtable = newHashtable;
                    }
                    if (i == word.Length - 1)
                        hashtable[END_FLAG] = 1;
                }
            }
        }

        private static int SearchSensitiveWord(string text, int startIndex)
        {
            Hashtable newMap = mMaskWordTable;
            bool flag = false;
            int len = 0;
            for (int i = startIndex; i < text.Length; i++)
            {
                char word = text[i];
                if (IsSymbol(word))
                {
                    len++;
                    continue;
                }
                Hashtable temp = (Hashtable)newMap[word]
                    ?? (Hashtable)newMap[char.ToLower(word)]
                    ?? (Hashtable)newMap[char.ToUpper(word)];
                if (temp != null)
                {
                    flag = (int)temp[END_FLAG] == 1;
                    newMap = temp;
                    len++;
                }
                else break;
            }
            return flag ? len : 0;
        }

        private static string ReplaceSensitiveWords(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            int i = 0;
            var builder = new StringBuilder(text);
            while (i < text.Length)
            {
                int len = SearchSensitiveWord(text, i);
                if (len > 0)
                {
                    bool isChange = true;
                    string input = text.Substring(i, len);
                    if (IsAllLetters(input))
                    {
                        if ((i + len < text.Length && IsAllLetters(text[i + len]))
                            || (i > 0 && IsAllLetters(text[i - 1])))
                            isChange = false;
                    }
                    if (isChange)
                        for (int j = 0; j < len; j++)
                            builder[i + j] = '*';
                    i += len;
                }
                else ++i;
            }
            return builder.ToString();
        }

        /// <summary>
        /// Whether <paramref name="c"/> is a symbol (not digit/letter and not in
        /// the 0x2E80-0x9FFF East-Asian range).
        /// </summary>
        private static bool IsSymbol(char c)
        {
            int ic = c;
            return !((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                && (ic < 0x2E80 || ic > 0x9FFF);
        }
    }
}