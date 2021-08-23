using System;
using System.Linq;
using System.Net.Http;
using Atomex.Services;
using Atomex.Wallet;
using Microsoft.AspNetCore.Components;
using Blazored.LocalStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.IO;
using System.Reflection;
using Atomex.Common.Configuration;
using Atomex;
using Atomex.MarketData.Bitfinex;
using Atomex.MarketData.Abstract;
using Atomex.Core;
using System.Security;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atomex.Blockchain;
using Atomex.Abstract;
using Atomex.Services.Abstract;
using Atomex.MarketData;
using Atomex.Common;
using Atomex.Cryptography;
using Atomex.Wallet.Abstract;

namespace atomex_frontend.Storages
{
  public class AccountStorage
  {
    public AccountStorage(HttpClient httpClient,
      ILocalStorageService localStorage,
      IJSRuntime jSRuntime,
      Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      this.httpClient = httpClient;
      this.localStorage = localStorage;
      this.jSRuntime = jSRuntime;

      LoadTranslations(I18nText);
      InitializeAtomexConfigs();
    }

    I18nText.Translations Translations = new I18nText.Translations();
    private async void LoadTranslations(Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      Translations = await I18nText.GetTextTableAsync<I18nText.Translations>(null);
    }
    public string lastLoggedWalletNameDueInactivityKey = "walletLoggedOutDueInactivity";
    public int IdleTimeoutToLogout = 900; // Seconds amount for logging out if user afk, 30 min now;

    public bool LoadFromRestore = false;

    private bool _updateAllCurrencies;

    public bool UpdateAllCurrencies
    {
      get => _updateAllCurrencies;
      set
      {
        if (!NewWallet) _updateAllCurrencies = value;
      }
    }

    public AccountDataRepository ADR;
    public IAccount Account { get; set; }
    public IAtomexApp AtomexApp { get; set; }
    public IAtomexClient Terminal { get; set; }
    public static ICurrencies Currencies { get; set; }

    public ISymbols Symbols
    {
      get => AtomexApp.SymbolsProvider.GetSymbols(Account.Network);
    }
    public BitfinexQuotesProvider QuotesProvider { get; set; }

    public event Action<bool> InitializeCallback;
    private void CallInitialize(bool IsRestarting)
    {
      InitializeCallback?.Invoke(IsRestarting);
    }

    public event Action RefreshUI;
    private void CallRefreshUI()
    {
      RefreshUI?.Invoke();
    }

    public CurrenciesProvider currenciesProvider;
    private IConfiguration currenciesConfiguration;
    private IConfiguration symbolsConfiguration;
    private Assembly coreAssembly;
    private HttpClient httpClient;
    public ILocalStorageService localStorage;
    public IJSRuntime jSRuntime;
    public string CurrentWalletName;
    public bool NewWallet;
    private SecureString _password;

    public Network CurrentNetwork
    {
      get => !String.IsNullOrEmpty(CurrentWalletName) && !CurrentWalletName.Contains("[test]") ? Network.MainNet : Network.TestNet;
    }

    private bool _passwordIncorrect = false;
    public bool PasswordIncorrect
    {
      get => this._passwordIncorrect;
      set
      {
        this._passwordIncorrect = value;

        if (this._passwordIncorrect)
        {
          this.WalletLoading = false;
        }
        else
        {
          this.CallRefreshUI();
        }
      }
    }

    private bool _walletLoading = false;
    public bool WalletLoading
    {
      get => this._walletLoading;
      set
      {
        this._walletLoading = value;
        this.CallRefreshUI();
      }
    }

    private bool _isQuotesProviderAvailable = false;
    public bool IsQuotesProviderAvailable
    {
      get => this._isQuotesProviderAvailable;
      set { this._isQuotesProviderAvailable = value; CallRefreshUI(); }
    }

    private bool _isExchangeConnected = false;
    public bool IsExchangeConnected
    {
      get => this._isExchangeConnected;
      set { this._isExchangeConnected = value; CallRefreshUI(); }
    }

    private bool _isMarketDataConnected = false;
    public bool IsMarketDataConnected
    {
      get => this._isMarketDataConnected;
      set { this._isMarketDataConnected = value; CallRefreshUI(); }
    }

    public string GetUserId()
    {
      using var servicePublicKey = Account.Wallet.GetServicePublicKey(Account.UserSettings.AuthenticationKeyIndex);
      using var publicKey = servicePublicKey.ToUnsecuredBytes();
      return Sha256.Compute(Sha256.Compute(publicKey)).ToHexString();
    }

    public async Task<IList<string>> GetAvailableWallets()
    {
      string availableWallets = await localStorage.GetItemAsync<string>("available_wallets");

      if (String.IsNullOrEmpty(availableWallets))
      {
        availableWallets = "[]";
      }
      string[] availableWalletsArr = JsonConvert.DeserializeObject<string[]>(availableWallets);

      IList<string> availableWalletsList = availableWalletsArr.ToList();
      availableWalletsList.Distinct();

      return availableWalletsList;
    }

    public async Task DeleteFromAvailableWallet(string walletName)
    {
      IList<string> availableWalletsList = await GetAvailableWallets();
      var listWithDeletedWallet = availableWalletsList.Where(wallet => !wallet.StartsWith(walletName));
      var newSerializedWalletNames = JsonConvert.SerializeObject(listWithDeletedWallet.ToArray<string>());
      await localStorage.SetItemAsync("available_wallets", newSerializedWalletNames);
      await localStorage.RemoveItemAsync(lastLoggedWalletNameDueInactivityKey);
    }

    public async Task SaveWallet(HdWallet wallet, SecureString storagePass, string walletName)
    {
      IList<string> availableWalletsList = await GetAvailableWallets();
      availableWalletsList.Add(walletName);

      var newSerializedWalletNames = JsonConvert.SerializeObject(availableWalletsList.ToArray<string>());

      await wallet.EncryptAsync(storagePass);

      wallet.SaveToFile($"/{walletName}.wallet", storagePass);
      byte[] walletBytes = File.ReadAllBytes($"/{walletName}.wallet");
      string walletBase64 = Convert.ToBase64String(walletBytes);

      await localStorage.SetItemAsync("available_wallets", newSerializedWalletNames);
      await localStorage.SetItemAsync($"{walletName}.wallet", walletBase64);
    }

    public async Task ConnectToWallet(string WalletName, SecureString Password)
    {
      WalletLoading = true;
      _password = Password;
      CurrentWalletName = WalletName;

      bool walletFileExist = File.Exists($"/{WalletName}.wallet");
      if (!walletFileExist)
      {
        var wallet = await localStorage.GetItemAsync<string>($"{WalletName}.wallet");
        if (wallet != null && wallet.Length > 0)
        {
          Console.WriteLine($"Wallet {WalletName} Found in LS");
          Byte[] walletBytesNew = Convert.FromBase64String(wallet);
          File.WriteAllBytes($"/{WalletName}.wallet", walletBytesNew);
        }
        else
        {
          Console.WriteLine($"Wallet {WalletName} not found in LS");
          return;
        }
      }
      else
      {
        Console.WriteLine($"Wallet {WalletName} founded on FS");
      }
      
      InitializeAtomexConfigs();
      
      ADR = new AccountDataRepository(Currencies);
      ADR.SaveDataCallback += SaveDataCallback;
      
      await jSRuntime.InvokeVoidAsync("getData", WalletName, DotNetObjectReference.Create(this));
    }

    [JSInvokableAttribute("AddADRData")]
    public void AddADRData(string data)
    {
      ADR.AddData(data);
    }

    [JSInvokableAttribute("LoadWallet")]
    public async void LoadWallet(int dbVersion)
    {
      Console.WriteLine($"Loading wallet with db version {dbVersion}");

      if (dbVersion == 1 && CurrentNetwork == Network.TestNet)
      {
        await MigrateFrom_0_to_1();
        return;
      }

      if (dbVersion == 1 && CurrentNetwork == Network.MainNet)
      {
        await MigrateFrom_1_to_2();
        return;
      }

      if (dbVersion == 2 && CurrentNetwork == Network.MainNet)
      {
        await MigrateFrom_2_to_3();
        return;
      }
      
      if (dbVersion == 3 && CurrentNetwork == Network.MainNet)
      {
        await MigrateFrom_3_to_4();
        return;
      }

      var confJson = await httpClient.GetAsync("conf/configuration.json");
      Stream confStream = await confJson.Content.ReadAsStreamAsync();

      IConfiguration configuration = new ConfigurationBuilder()
          .AddJsonStream(confStream)
          .Build();

      IConfiguration symbolsConfiguration = new ConfigurationBuilder()
        .SetBasePath("/")
        .AddEmbeddedJsonFile(this.coreAssembly, "symbols.json")
        .Build();

      Currencies = currenciesProvider.GetCurrencies(CurrentNetwork);
      var symbolsProvider = new SymbolsProvider(symbolsConfiguration);

      try
      {
        Account = new Account(
          HdWallet.LoadFromFile($"/{CurrentWalletName}.wallet", _password),
          _password,
          ADR,
          currenciesProvider,
          clientType: ClientType.Web
        );
      }
      catch (Exception e)
      {
        PasswordIncorrect = true;
        Console.WriteLine($"Incorrect password {e}");
        _password = null;
        return;
      }
      _password = null;


      if (AtomexApp != null) // restarting flow
      {
        Terminal = new WebSocketAtomexClient(configuration, Account, AtomexApp.SymbolsProvider, AtomexApp.QuotesProvider);
        AtomexApp.UseAtomexClient(Terminal);

        AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
        AtomexApp.Account.BalanceUpdated += OnBalanceChangedEventHandler;
        AtomexApp.Terminal.ServiceConnected += OnTerminalServiceStateChangedEventHandler;
        AtomexApp.Terminal.ServiceDisconnected += OnTerminalServiceStateChangedEventHandler;

        AtomexApp.Start();

        CallInitialize(IsRestarting: true);
        Console.WriteLine($"Restarting: switched to Account with {CurrentWalletName} wallet");
        return;
      }
      else
      {
        AtomexApp = new AtomexApp()
            .UseCurrenciesProvider(currenciesProvider)
            .UseSymbolsProvider(symbolsProvider)
            .UseCurrenciesUpdater(new CurrenciesUpdater(currenciesProvider))
            .UseSymbolsUpdater(new SymbolsUpdater(symbolsProvider))
            .UseQuotesProvider(new BitfinexQuotesProvider(
                currencies: currenciesProvider.GetCurrencies(CurrentNetwork),
                baseCurrency: BitfinexQuotesProvider.Usd));

        Terminal = new WebSocketAtomexClient(configuration, Account, AtomexApp.SymbolsProvider, AtomexApp.QuotesProvider);
        AtomexApp.UseAtomexClient(Terminal);
      }

      if (AtomexApp.HasQuotesProvider)
      {
        Console.WriteLine("Subscribing for quotes Provider");
        AtomexApp.QuotesProvider.QuotesUpdated += OnQuotesUpdatedEventHandler;
        AtomexApp.QuotesProvider.AvailabilityChanged += OnQuotesProviderAvailabilityChangedEventHandler;
      }
      AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
      AtomexApp.Account.BalanceUpdated += OnBalanceChangedEventHandler;
      AtomexApp.Terminal.ServiceConnected += OnTerminalServiceStateChangedEventHandler;
      AtomexApp.Terminal.ServiceDisconnected += OnTerminalServiceStateChangedEventHandler;

      Console.WriteLine($"Starting Atomex app with {CurrentWalletName}");
      CallInitialize(IsRestarting: false);
      AtomexApp.Start();
    }

    private void SaveDataCallback(AccountDataRepository.AvailableDataType type, string key, string value)
    {
      jSRuntime.InvokeAsync<string>("saveData", new [] { type.ToName(), CurrentWalletName, key, value });
    }

    private void OnTerminalServiceStateChangedEventHandler(object sender, TerminalServiceEventArgs args)
    {
      if (!(sender is IAtomexClient terminal))
        return;

      IsExchangeConnected = terminal.IsServiceConnected(TerminalService.Exchange);
      IsMarketDataConnected = terminal.IsServiceConnected(TerminalService.MarketData);

      // subscribe to symbols updates
      if (args.Service == TerminalService.MarketData && IsMarketDataConnected)
      {
        Console.WriteLine("SUBSCRIBING TO WEBSOCKET MASRKET DATA");
        terminal.SubscribeToMarketData(SubscriptionType.TopOfBook);
        terminal.SubscribeToMarketData(SubscriptionType.DepthTwenty);
      }
    }

    private void OnQuotesUpdatedEventHandler(object sender, EventArgs args)
    {
      if (sender is BitfinexQuotesProvider quotesProvider)
      {
        QuotesProvider = quotesProvider;
      }
    }

    private void OnQuotesProviderAvailabilityChangedEventHandler(object sender, EventArgs args)
    {
      if (!(sender is ICurrencyQuotesProvider provider))
        return;

      IsQuotesProviderAvailable = provider.IsAvailable;
    }

    private void OnBalanceChangedEventHandler(object sender, CurrencyEventArgs args)
    {
      Console.WriteLine($"BALANCE UPDATED ON {args.Currency}");
    }

    private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
    {
      Console.WriteLine($"New transaction, state: {e.Transaction.State}");
      //   Console.WriteLine($"New transaction!! {e.Transaction.Id}");
      //   if (!e.Transaction.IsConfirmed && e.Transaction.State != BlockchainTransactionState.Failed)
      //     Console.WriteLine($"New transaction!! {e.Transaction.Id}");
    }

    [JSInvokableAttribute("SignOut")]
    public async void SignOut(bool notFromIdle = true, string path = "/")
    {
      try
      {
        if (await WhetherToCancelClosingAsync(notFromIdle))
          return;

        AtomexApp.UseAtomexClient(null);
        AtomexApp.Stop();

        if (!notFromIdle)
        {
          await localStorage.SetItemAsync(lastLoggedWalletNameDueInactivityKey, CurrentWalletName);
        }
        await jSRuntime.InvokeVoidAsync("signOut", path);
      }
      catch (Exception e)
      {
        Console.Write($"Sign Out error {e.ToString()}");
      }
    }

    private async Task<bool> WhetherToCancelClosingAsync(bool notFromIdle)
    {
      var hasActiveSwaps = await HasActiveSwapsAsync();

      if (!hasActiveSwaps)
        return false;

      if (notFromIdle)
      {
        await jSRuntime.InvokeVoidAsync("alert", Translations.ActiveSwapsWarning);
      }
      return true;
    }

    private async Task<bool> HasActiveSwapsAsync()
    {
      var swaps = await AtomexApp.Account
          .GetSwapsAsync();

      return swaps.Any(swap => swap.IsActive);
    }

    private void InitializeAtomexConfigs()
    {
      coreAssembly = AppDomain.CurrentDomain
        .GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "Atomex.Client.Core");

      currenciesConfiguration = new ConfigurationBuilder()
        .AddEmbeddedJsonFile(coreAssembly, "currencies.json")
        .Build();

      symbolsConfiguration = new ConfigurationBuilder()
        .AddEmbeddedJsonFile(coreAssembly, "symbols.json")
        .Build();

      currenciesProvider = new CurrenciesProvider(CurrenciesConfigurationJson(coreAssembly));

      Currencies = currenciesProvider.GetCurrencies(CurrentNetwork);
    }

    private string CurrenciesConfigurationJson(Assembly coreAssembly)
    {
        var resourceName = "currencies.json";
        var resourceNames = coreAssembly.GetManifestResourceNames();
        var fullFileName = resourceNames.FirstOrDefault(n => n.EndsWith(resourceName));
        var stream = coreAssembly.GetManifestResourceStream(fullFileName!);

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private async Task MigrateFrom_0_to_1()
    {
      int TARGET_VER = 1;
      Console.WriteLine($"Applying migration database to verson {1}.");

      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.Transaction.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.WalletAddress.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("saveDBVersion", CurrentWalletName, TARGET_VER);

      Console.WriteLine("Migration applied, DB version saved, restarting.");
      _ = ConnectToWallet(CurrentWalletName, _password);
    }

    private async Task MigrateFrom_1_to_2()
    {
      int TARGET_VER = 2;
      Console.WriteLine($"Applying migration database to verson {TARGET_VER}.");

      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.Transaction.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.Output.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("saveDBVersion", CurrentWalletName, TARGET_VER);

      Console.WriteLine("Migration applied, DB version saved, restarting.");
      _ = ConnectToWallet(CurrentWalletName, _password);
    }

    private async Task MigrateFrom_2_to_3()
    {
      int TARGET_VER = 3;
      Console.WriteLine($"Applying migration database to verson {TARGET_VER}.");

      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.Transaction.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.Output.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("saveDBVersion", CurrentWalletName, TARGET_VER);

      Console.WriteLine("Migration applied, DB version saved, restarting.");
      UpdateAllCurrencies = true;
      _ = ConnectToWallet(CurrentWalletName, _password);
    }
    
    private async Task MigrateFrom_3_to_4()
    {
      int TARGET_VER = 4;
      Console.WriteLine($"Applying migration database to verson {TARGET_VER}.");

      await jSRuntime.InvokeAsync<string>("deleteData", AccountDataRepository.AvailableDataType.WalletAddress.ToName(), CurrentWalletName);
      await jSRuntime.InvokeAsync<string>("saveDBVersion", CurrentWalletName, TARGET_VER);

      Console.WriteLine("Migration applied, DB version saved, restarting.");
      UpdateAllCurrencies = true;
      _ = ConnectToWallet(CurrentWalletName, _password);
    }
  }
}
