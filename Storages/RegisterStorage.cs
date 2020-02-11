using System;
using System.ComponentModel;
using Atomex.Common;

namespace atomex_frontend.Storages
{
    enum Nets {
        MainNet,
        TestNet
    }

    enum MnemonicWordsAmount {
        [Description("12")]
        Twelve,

        [Description("15")]
        Fifteen,

        [Description("18")]
        Eighteen,

        [Description("20")]
        Twenty,

        [Description("24")]
        TwentyFour,
    }

    public enum PassTypes {
        Derived,
        Storage
    }

    public class RegisterStorage
    {
        public RegisterStorage()
        {
            CurrentStep = Steps.WalletType;
            SelectedNetType = Nets.MainNet.ToName();
            MnemonicWordCount = MnemonicWordsAmount.Eighteen.ToName();
            DerivedPasswordStrongness = PassStrongness.Blank;
            StoragePasswordStrongness = PassStrongness.Blank;
            WalletName = "";
            DerivedKeyPassword1 = "";
            DerivedKeyPassword2 = "";
            StoragePassword1 = "";
            StoragePassword2 = "";
            DerivedPassword2Typed = false;
            StoragePassword2Typed = false;
            WalletNameTyped = false;
        }

        public enum PassStrongness
        {
            Blank,
            [Description("Too short")]
            TooShort,
            Weak,
            Medium,
            Strong,
            [Description("Very Strong")]
            VeryStrong
        }

        public enum PasswordErrors
        {
            None,
            Mismatch,
            Empty,
            Weak
        }

        public enum WalletNameErrors
        {
            None,
            Empty,
        }

        public enum Steps
        {
            WalletType = 1,
            WalletName,
            MnemonicPhrase,
            DerivedPassword,
            StoragePassword
        }

        public string SelectedNetType { get; private set; }
        public string WalletName { get; private set; }
        public bool WalletNameTyped { get; private set; }
        public string MnemonicWordCount { get; private set; }
        public string DerivedKeyPassword1 { get; private set; }
        public string DerivedKeyPassword2 { get; private set; }
        public PassStrongness DerivedPasswordStrongness { get; private set; }
        public bool DerivedPassword2Typed { get; private set; }
        public string StoragePassword1 { get; private set; }
        public string StoragePassword2 { get; private set; }
        public bool StoragePassword2Typed { get; private set; }
        public PassStrongness StoragePasswordStrongness { get; private set; }
        public Steps CurrentStep { get; private set; }
        public int TotalSteps
        {
            get { return Enum.GetNames(typeof(Steps)).Length; }
            set { }
        }

        public PasswordErrors DerivedPasswordsError {
            get { return GetPasswordErrorsCode(PassTypes.Derived); }
            private set { }
        }

        public PasswordErrors StoragePasswordsError
        {
            get { return GetPasswordErrorsCode(PassTypes.Storage); }
            private set { }
        }

        public WalletNameErrors WalletNameError {
            get {
                return WalletName.Length == 0 && WalletNameTyped ?
                    WalletNameErrors.Empty : WalletNameErrors.None;
            }
            set { }
        }

        // Constants:
        public string[] NetOptions { get; private set; } = new string[] {
            Nets.MainNet.ToName(), Nets.TestNet.ToName()
        };
        public string[] MnemonicWordOptions { get; private set; } =
            new string[] { MnemonicWordsAmount.Twelve.ToName(),
                MnemonicWordsAmount.Fifteen.ToName(),
                MnemonicWordsAmount.Eighteen.ToName(),
                MnemonicWordsAmount.Twenty.ToName(),
                MnemonicWordsAmount.TwentyFour.ToName() };
        public string[] MnemonicPhrases { get; private set; } = new string[]
        { "Lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "Lorem",
            "ipsum", "dolor", "sit", "amet", "consectetur" };


        public void IncrementCurrentStep()
        {
            if ((int)CurrentStep < TotalSteps) {
                CurrentStep = CurrentStep.Next();
            }
        }

        public void DecrementCurrentStep()
        {
            if (CurrentStep > Steps.WalletType) {
                CurrentStep = CurrentStep.Previous();
            }
        }

        public void SetSelectedNetType(string netType) {
            SelectedNetType = netType;
        }

        public void SetWalletName(string name) {
            WalletName = name;
            if (!WalletNameTyped) {
                WalletNameTyped = true;
            }
        }

        public void SetDerivedKeyPassword1(string password) {
            DerivedKeyPassword1 = password;
            DerivedPasswordStrongness =
                (PassStrongness)PasswordAdvisor.CheckStrength(password);
        }

        public void SetDerivedKeyPassword2(string password)
        {
            DerivedKeyPassword2 = password;
            if (!DerivedPassword2Typed) {
                DerivedPassword2Typed = true;
            }
        }

        public void SetStoragePassword1(string password)
        {
            StoragePassword1 = password;
            StoragePasswordStrongness =
                (PassStrongness)PasswordAdvisor.CheckStrength(password);
        }

        public void SetStoragePassword2(string password)
        {
            StoragePassword2 = password;
            if (!StoragePassword2Typed)
            {
                StoragePassword2Typed = true;
            }
        }

        public bool GetDerivedPasswordsMatch() {
            return DerivedKeyPassword1.Equals(DerivedKeyPassword2);
        }

        public void SetMnemonicWordCount(string count) {
            MnemonicWordCount = count;
        }

        protected PasswordErrors GetPasswordErrorsCode(PassTypes PassType) {
            string pass1 = "";
            string pass2 = "";
            bool hasTyped = false;
            PassStrongness strongness = PassStrongness.Blank;


            if (PassType == PassTypes.Derived) {
                pass1 = DerivedKeyPassword1;
                pass2 = DerivedKeyPassword2;
                hasTyped = DerivedPassword2Typed;
                strongness = DerivedPasswordStrongness;
            } else if (PassType == PassTypes.Storage) {
                pass1 = StoragePassword1;
                pass2 = StoragePassword2;
                hasTyped = StoragePassword2Typed;
                strongness = StoragePasswordStrongness;
            }

            if (!hasTyped)
            {
                return PasswordErrors.None;
            }
            else if (pass1.Length == 0 && pass2.Length == 0)
            {
                return PasswordErrors.Empty;
            }
            else if (!pass1.Equals(pass2))  
            {
                return PasswordErrors.Mismatch;
            }
            else if (strongness < PassStrongness.Medium)
            {
                return PasswordErrors.Weak;
            }
            else return PasswordErrors.None;
        }
    }
}
