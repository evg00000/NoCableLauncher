using System;
using System.Globalization;

namespace NoCableLauncher
{
    /// <summary>
    /// UI language. Every user-visible string is written as Tr.T("русский", "English"),
    /// so both translations stay next to each other in the code.
    /// </summary>
    public static class Tr
    {
        public static bool English { get; private set; }

        /// <summary>setting: "auto", "ru" or "en".</summary>
        public static void Init(string setting)
        {
            string s = (setting ?? "auto").Trim().ToLowerInvariant();
            if (s == "ru") English = false;
            else if (s == "en") English = true;
            else
            {
                string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                English = !(lang == "ru" || lang == "uk" || lang == "be" || lang == "kk");
            }
        }

        public static string T(string ru, string en)
        {
            return English ? en : ru;
        }
    }
}
