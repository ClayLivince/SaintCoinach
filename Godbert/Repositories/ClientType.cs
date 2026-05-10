using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SaintCoinach.Ex;
using System.Linq;

namespace Godbert.Repositories {
    public enum ClientType {
        SquareEnix=1,
        ShanDa=2,
        ACTOZ=3,
        Userjoy=4,

        Unknown=0
    }

    public static class ClientTypeExtensions {
    
        public static Dictionary<ClientType, Language[]> AvailableLanguages = new() {
            { ClientType.SquareEnix, new[] { Language.English, Language.Japanese, Language.German, Language.French } },
            { ClientType.ShanDa, new[] { Language.ChineseSimplified} },
            { ClientType.ACTOZ, new[] { Language.Korean} },
            { ClientType.Userjoy, new[] { Language.TraditionalChinese} },
            { ClientType.Unknown, new[] { Language.Unsupported } }
        };

        public static Language[] GetAvailableLanguages(this ClientType c) {
            return AvailableLanguages[c];
        }

        public static Language GetFirstLanguage(this ClientType c) {
            return AvailableLanguages[c][0];
        }

        public static IEnumerable<string> GetAvailableLanguageCodes(this ClientType c) {
            Language[] languages = GetAvailableLanguages(c);
            return languages.Select(t => t.GetCode());
        }

        public static IEnumerable<string> GetIconVariants(this ClientType c) {
            IEnumerable<string> languageCodes = c.GetAvailableLanguageCodes();
            return (new[] { "" }).Concat(languageCodes.Select(t => "/" + t));
           

        }
    }
}
