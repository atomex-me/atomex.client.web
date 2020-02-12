using System;
using System.Collections.Generic;
using NBitcoin;

namespace atomex_frontend.Storages
{
    public class UserStorage
    {
        public UserStorage()
        {
            SelectedLanguage = nameof(Languages.English);

            LanguageCode = new Dictionary<string, string>();
            LanguageCode.Add(Languages.English.ToName(), LangCodes.en.ToName());
            LanguageCode.Add(Languages.China.ToName(), LangCodes.ch.ToName());
            LanguageCode.Add(Languages.Spanish.ToName(), LangCodes.sp.ToName());
            LanguageCode.Add(Languages.Russian.ToName(), LangCodes.ru.ToName());
        }

        public enum Languages
        {
            English,
            China,
            Spanish,
            Russian
        }

        public enum LangCodes
        {
            en,
            ch,
            sp,
            ru
        }



        private Dictionary<string, string> LanguageCode;

        public string[] LanguageOptions = new string[] {
            Languages.English.ToName(), Languages.China.ToName(),
            Languages.Spanish.ToName(), Languages.Russian.ToName()
        };

        public string SelectedLanguage { get; private set; }

        public string GetCurrentLanguageCode()
        {
            return LanguageCode[SelectedLanguage];
        }

        public void SetSelectedLanguage(string language) {
            SelectedLanguage = language;
            Wordlist english = Wordlist.English;
            Console.Write(english);
        }
    }
}
