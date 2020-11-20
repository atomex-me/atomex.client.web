using System;
using System.Security;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Wallet;
using NBitcoin;
using System.Net;
using Blazored.LocalStorage;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace atomex_frontend.Storages
{
  public enum PassTypes
  {
    Derived,
    Storage
  }

  public class RegisterStorage
  {
    public RegisterStorage(AccountStorage accountStorage,
        ILocalStorageService localStorage,
        NavigationManager navigationManager)
    {
      this.accountStorage = accountStorage;
      this.localStorage = localStorage;
      this.URIHelper = navigationManager;

      ResetData();
    }

    private AccountStorage accountStorage;
    private ILocalStorageService localStorage;
    private NavigationManager URIHelper;

    public enum MnemonicWordsAmount
    {
      [Description("12")]
      Twelve,

      [Description("15")]
      Fifteen,

      [Description("18")]
      Eighteen,

      [Description("21")]
      TwentyOne,

      [Description("24")]
      TwentyFour,
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
      Exist,
    }

    public enum Steps
    {
      WalletType = 1,
      WalletName,
      MnemonicPhrase,
      ConfirmMnemonic,
      DerivedPassword,
      StoragePassword
    }

    public enum Nets
    {
      MainNet,
      TestNet
    }

    public string TEST_VAR_KOS;

    private int _entropyLength;

    private Dictionary<string, int> MnemonicWordsToEntropy;
    public string SelectedNetType { get; private set; }
    public string WalletName { get; private set; }
    public bool WalletNameTyped { get; private set; }
    public Dictionary<string, Wordlist> AvailableMnemonicLangs { get; set; }
    public Wordlist CurrentMnemonicLang { get; private set; }
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

    private string _mnemonicString;
    public string MnemonicString
    {
      get => _mnemonicString;
      set
      {
        _mnemonicString = value;
        RandomMnemonicStringList = _mnemonicString.Split(' ').ToList();
        OrderedMnemonicStringList = new List<string>();
      }
    }

    public bool OrderedMnemonicCorrect
    {
      get => string
             .Join(" ", OrderedMnemonicStringList)
             .Equals(MnemonicString) && OrderedMnemonicStringList.Count() == int.Parse(MnemonicWordCount);
    }

    private List<string> _orderedMnemonicStringList = new List<string>();
    public List<string> OrderedMnemonicStringList
    {
      get => _orderedMnemonicStringList;
      set
      {
        _orderedMnemonicStringList = value;
      }
    }

    private List<string> _randomMnemonicStringList = new List<string>();
    public List<string> RandomMnemonicStringList
    {
      get => _randomMnemonicStringList;
      set
      {
        var shuffledList = value.ToList();
        shuffledList.Shuffle();
        _randomMnemonicStringList = shuffledList;
      }
    }
    public int TotalSteps
    {
      get { return Enum.GetNames(typeof(Steps)).Length; }
      set { }
    }

    public string GetWalletName
    {
      get => GetNetworkCode() == Atomex.Core.Network.TestNet ?
        $"[test] {WalletName}" :
        WalletName;
    }

    public bool Loading { get; set; }

    public PasswordErrors DerivedPasswordsError
    {
      get { return GetPasswordErrorsCode(PassTypes.Derived); }
      private set { }
    }

    public PasswordErrors StoragePasswordsError
    {
      get { return GetPasswordErrorsCode(PassTypes.Storage); }
      private set { }
    }

    private WalletNameErrors _walletNameError;
    public WalletNameErrors WalletNameError
    {
      get => _walletNameError;
      set { _walletNameError = value; }
    }

    public string RestoreMnemonic { get; set; }

    public void SetRestoreMnemonic(string value)
    {
      RestoreMnemonic = value;
    }

    public async Task IncrementCurrentStep()
    {
      if (CurrentStep == Steps.StoragePassword)
      {
        Loading = true;
        Atomex.Core.Network NetCode = GetNetworkCode();
        string walletName = NetCode == Atomex.Core.Network.MainNet ?
            WalletName : $"[test] {WalletName}";

        var wallet = new HdWallet(
                mnemonic: MnemonicString,
                wordList: CurrentMnemonicLang,
                passPhrase: DerivedKeyPassword1.Length == 0 ? null : DerivedKeyPassword1.ToSecureString(),
                network: NetCode);

        await accountStorage
            .SaveWallet(wallet, StoragePassword1.ToSecureString(), walletName);

        await accountStorage.ConnectToWallet(walletName, StoragePassword1.ToSecureString());
      }

      if ((int)CurrentStep < TotalSteps)
      {
        CurrentStep = CurrentStep.Next();
      }
    }

    public async void RestoreIncrementStep()
    {
      if (CurrentStep == Steps.StoragePassword)
      {
        Loading = true;
        Atomex.Core.Network NetCode = GetNetworkCode();
        string walletName = NetCode == Atomex.Core.Network.MainNet ?
            WalletName : $"[test] {WalletName}";

        var wallet = new HdWallet(
                mnemonic: RestoreMnemonic,
                wordList: CurrentMnemonicLang,
                passPhrase: DerivedKeyPassword1.Length == 0 ? null : DerivedKeyPassword1.ToSecureString(),
                network: NetCode);

        await accountStorage
            .SaveWallet(wallet, StoragePassword1.ToSecureString(), walletName);
        accountStorage.LoadFromRestore = true;
        await accountStorage.ConnectToWallet(walletName, StoragePassword1.ToSecureString());
      }

      if ((int)CurrentStep < TotalSteps)
      {
        if (CurrentStep == Steps.MnemonicPhrase)
        {
          CurrentStep = Steps.DerivedPassword;
        }
        else
        {
          CurrentStep = CurrentStep.Next();
        }
      }
    }

    public void DecrementCurrentStep()
    {
      if (CurrentStep > Steps.WalletType)
      {
        if (CurrentStep == Steps.DerivedPassword)
        {
          CurrentStep = Steps.MnemonicPhrase;
        }
        else
        {
          CurrentStep = CurrentStep.Previous();
        }
      }
      else
      {
        URIHelper.NavigateTo("/");
      }
    }

    public void SetSelectedNetType(string netType)
    {
      SelectedNetType = netType;
    }

    public async Task SetWalletName(string name)
    {
      WalletName = name;
      if (!WalletNameTyped)
      {
        WalletNameTyped = true;
      }

      string walletName = GetNetworkCode() == Atomex.Core.Network.MainNet ?
          WalletName : $"[test] {WalletName}";

      bool walletExist = await localStorage.ContainKeyAsync($"{walletName}.wallet");

      WalletNameError = WalletName.Length == 0 && WalletNameTyped ? WalletNameErrors.Empty :
          walletExist ? WalletNameErrors.Exist : WalletNameErrors.None;
    }

    public void SetDerivedKeyPassword1(string password)
    {
      DerivedKeyPassword1 = password;
      DerivedPasswordStrongness =
          (PassStrongness)PasswordAdvisor.CheckStrength(password);
    }

    public void SetDerivedKeyPassword2(string password)
    {
      DerivedKeyPassword2 = password;
      if (!DerivedPassword2Typed)
      {
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

    public bool GetDerivedPasswordsMatch()
    {
      return DerivedKeyPassword1.Equals(DerivedKeyPassword2);
    }

    public void SetMnemonicWordCount(string count)
    {
      MnemonicWordCount = count;
      int entropy = MnemonicWordsToEntropy
          .FirstOrDefault(item => item.Key == count).Value;
      _entropyLength = entropy;

      GenerateMnemonic();
    }

    public void GenerateMnemonic()
    {
      var entropy = Rand.SecureRandomBytes(_entropyLength / 8);
      MnemonicString = new Mnemonic(CurrentMnemonicLang, entropy).ToString();

    }

    public void SetSelectedLanguage(string strLang)
    {
      CurrentMnemonicLang = AvailableMnemonicLangs
          .FirstOrDefault(x => x.Key == strLang).Value;
    }

    public Atomex.Core.Network GetNetworkCode()
    {
      if (SelectedNetType == Nets.MainNet.ToName())
      {
        return Atomex.Core.Network.MainNet;
      }

      if (SelectedNetType == Nets.TestNet.ToName())
      {
        return Atomex.Core.Network.TestNet;
      }

      return Atomex.Core.Network.MainNet;
    }

    protected PasswordErrors GetPasswordErrorsCode(PassTypes PassType)
    {
      string pass1 = "";
      string pass2 = "";
      bool hasTyped = false;
      PassStrongness strongness = PassStrongness.Blank;


      if (PassType == PassTypes.Derived)
      {
        pass1 = DerivedKeyPassword1;
        pass2 = DerivedKeyPassword2;
        hasTyped = DerivedPassword2Typed;
        strongness = DerivedPasswordStrongness;
      }
      else if (PassType == PassTypes.Storage)
      {
        pass1 = StoragePassword1;
        pass2 = StoragePassword2;
        hasTyped = StoragePassword2Typed;
        strongness = StoragePasswordStrongness;
      }

      if (!hasTyped)
      {
        return PasswordErrors.None;
      }
      else if (pass1.Length == 0 && pass2.Length == 0 && PassType != PassTypes.Derived)
      {
        return PasswordErrors.Empty;
      }
      else if (!pass1.Equals(pass2))
      {
        return PasswordErrors.Mismatch;
      }
      else if (strongness < PassStrongness.Medium)
      {
        if (PassType == PassTypes.Derived && (DerivedKeyPassword1.Length == 0 && DerivedKeyPassword2.Length == 0))
        {
          return PasswordErrors.None;
        }
        return PasswordErrors.Weak;
      }
      else return PasswordErrors.None;
    }

    public void ResetData()
    {
      CurrentStep = Steps.WalletType;
      SelectedNetType = Nets.MainNet.ToName();
      DerivedPasswordStrongness = PassStrongness.Blank;
      StoragePasswordStrongness = PassStrongness.Blank;
      WalletName = "";
      _walletNameError = WalletNameErrors.None;
      DerivedKeyPassword1 = "";
      DerivedKeyPassword2 = "";
      StoragePassword1 = "";
      StoragePassword2 = "";
      DerivedPassword2Typed = false;
      StoragePassword2Typed = false;
      WalletNameTyped = false;
      Loading = false;

      MnemonicWordCount = MnemonicWordsAmount.Eighteen.ToName();
      _entropyLength = 192;
      MnemonicString = "";

      RestoreMnemonic = null;

      CurrentMnemonicLang = Wordlist.English;

      AvailableMnemonicLangs = new Dictionary<string, Wordlist>();
      AvailableMnemonicLangs.Add("English", Wordlist.English);
      AvailableMnemonicLangs.Add("Spanish", Wordlist.Spanish);
      AvailableMnemonicLangs.Add("French", Wordlist.French);
      AvailableMnemonicLangs.Add("Japanese", Wordlist.Japanese);
      AvailableMnemonicLangs.Add("Portuguese Brazil", Wordlist.PortugueseBrazil);
      AvailableMnemonicLangs.Add("Chinese Traditional", Wordlist.ChineseTraditional);
      AvailableMnemonicLangs.Add("Chinese Simplified", Wordlist.ChineseSimplified);

      MnemonicWordsToEntropy = new Dictionary<string, int>();
      MnemonicWordsToEntropy.Add(MnemonicWordsAmount.Twelve.ToName(), 128);
      MnemonicWordsToEntropy.Add(MnemonicWordsAmount.Fifteen.ToName(), 160);
      MnemonicWordsToEntropy.Add(MnemonicWordsAmount.Eighteen.ToName(), 192);
      MnemonicWordsToEntropy.Add(MnemonicWordsAmount.TwentyOne.ToName(), 224);
      MnemonicWordsToEntropy.Add(MnemonicWordsAmount.TwentyFour.ToName(), 256);
    }
  }
}
