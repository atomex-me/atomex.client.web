using System;
using System.Linq;
using System.Net.Http;
using Atomex.Subsystems;
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
using Atomex.Subsystems.Abstract;
using Atomex.MarketData;

namespace atomex_frontend.Storages
{
  public class AccountStorage
  {
    public AccountStorage(HttpClient httpClient,
      ILocalStorageService localStorage,
      IJSRuntime jSRuntime,
      NavigationManager uriHelper,
      Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      this.httpClient = httpClient;
      this.localStorage = localStorage;
      this.jSRuntime = jSRuntime;
      this.URIHelper = uriHelper;

      LoadTranslations(I18nText);
      InitializeAtomexConfigs();
    }

    I18nText.Translations Translations = new I18nText.Translations();
    private async void LoadTranslations(Toolbelt.Blazor.I18nText.I18nText I18nText)
    {
      Translations = await I18nText.GetTextTableAsync<I18nText.Translations>(null);
    }

    public static Currency Bitcoin
    {
      get => Currencies.GetByName("BTC");
    }

    public static Currency Ethereum
    {
      get => Currencies.GetByName("ETH");
    }

    public static Currency Litecoin
    {
      get => Currencies.GetByName("LTC");
    }

    public static Currency Tezos
    {
      get => Currencies.GetByName("XTZ");
    }

    public static Currency Tether
    {
      get => Currencies.GetByName("USDT");
    }

    public static Currency FA12
    {
      get => Currencies.GetByName("FA12");
    }

    public AccountDataRepository ADR;
    private NavigationManager URIHelper;
    public Account Account { get; set; }
    public IAtomexApp AtomexApp { get; set; }
    public IAtomexClient Terminal { get; set; }
    public static ICurrencies Currencies { get; set; }
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
    private ILocalStorageService localStorage;
    public IJSRuntime jSRuntime;
    private string CurrentWalletName;
    private SecureString _password;

    private Network CurrentNetwork
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
      await jSRuntime.InvokeVoidAsync("getData", WalletName, DotNetObjectReference.Create(this));
    }

    [JSInvokableAttribute("LoadWallet")]
    public async void LoadWallet(string data)
    {
      Console.WriteLine("Loading wallet....");

      var confJson = await httpClient.GetJsonAsync<dynamic>("conf/configuration.json");
      Stream confStream = Common.Helper.GenerateStreamFromString(confJson.ToString());

      IConfiguration configuration = new ConfigurationBuilder()
          .AddJsonStream(confStream)
          .Build();

      IConfiguration symbolsConfiguration = new ConfigurationBuilder()
        .SetBasePath("/")
        .AddEmbeddedJsonFile(this.coreAssembly, "symbols.json")
        .Build();

      Currencies = currenciesProvider.GetCurrencies(CurrentNetwork);

      var symbolsProvider = new SymbolsProvider(symbolsConfiguration);

      Console.WriteLine(Currencies == null);
      ADR = new AccountDataRepository(Currencies, initialData: data);
      ADR.SaveDataCallback += SaveDataCallback;

      try
      {
        Account = new Account(
          HdWallet.LoadFromFile($"/{CurrentWalletName}.wallet", _password),
          _password,
          ADR,
          currenciesProvider,
          symbolsProvider
        );
      }
      catch (Exception e)
      {
        PasswordIncorrect = true;
        Console.WriteLine(e.ToString());
        _password = null;
        return;
      }
      _password = null;

      Terminal = new WebSocketAtomexClient(configuration, Account);

      if (AtomexApp != null)
      {
        AtomexApp.UseTerminal(Terminal);

        AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
        AtomexApp.Account.BalanceUpdated += OnBalanceChangedEventHandler;
        AtomexApp.Terminal.ServiceConnected += OnTerminalServiceStateChangedEventHandler;
        AtomexApp.Terminal.ServiceDisconnected += OnTerminalServiceStateChangedEventHandler;

        AtomexApp.Start();

        this.CallInitialize(IsRestarting: true);
        Console.WriteLine($"Restarting: switched to Account with {CurrentWalletName} wallet");
        return;
      }
      else
      {
        AtomexApp = new AtomexApp()
            .UseCurrenciesProvider(currenciesProvider)
            .UseSymbolsProvider(symbolsProvider)
            // .UseCurrenciesUpdater(new CurrenciesUpdater(currenciesProvider))
            .UseQuotesProvider(new BitfinexQuotesProvider(
                currencies: currenciesProvider.GetCurrencies(CurrentNetwork),
                baseCurrency: BitfinexQuotesProvider.Usd))
            .UseTerminal(Terminal);
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

      Console.WriteLine($"Starting Atomex app with {CurrentWalletName} wallet with data {data.Length}");
      this.CallInitialize(IsRestarting: false);
      AtomexApp.Start();
    }

    private void SaveDataCallback(AccountDataRepository.AvailableDataType type, string key, string value)
    {
      jSRuntime.InvokeAsync<string>("saveData", new string[] { type.ToName(), CurrentWalletName, key, value }); // todo: delete tx in InexedDB
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
      Console.WriteLine($"New trans!!!!!!!!!!!!! {e.Transaction.State}");
      //   Console.WriteLine($"New transaction!! {e.Transaction.Id}");
      //   if (!e.Transaction.IsConfirmed && e.Transaction.State != BlockchainTransactionState.Failed)
      //     Console.WriteLine($"New transaction!! {e.Transaction.Id}");
    }

    public async void SignOut()
    {
      Console.WriteLine("Signing out");
      try
      {
        if (await WhetherToCancelClosingAsync())
          return;

        AtomexApp.UseTerminal(null);
        AtomexApp.Stop();

        await jSRuntime.InvokeVoidAsync("signOut", Translations.ActiveSwapsWarning);
      }
      catch (Exception e)
      {
        Console.Write($"Sign Out error {e.ToString()}");
      }
    }

    private async Task<bool> WhetherToCancelClosingAsync()
    {
      // if (!AtomexApp.Account.UserSettings.ShowActiveSwapWarning)
      //   return false;

      var hasActiveSwaps = await HasActiveSwapsAsync();

      if (!hasActiveSwaps)
        return false;

      await jSRuntime.InvokeVoidAsync("alert", Translations.ActiveSwapsWarning);
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
      this.coreAssembly = AppDomain.CurrentDomain
        .GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "Atomex.Client.Core");

      this.currenciesConfiguration = new ConfigurationBuilder()
        .AddEmbeddedJsonFile(coreAssembly, "currencies.json")
        .Build();

      this.symbolsConfiguration = new ConfigurationBuilder()
        .AddEmbeddedJsonFile(coreAssembly, "symbols.json")
        .Build();

      this.currenciesProvider = new CurrenciesProvider(this.currenciesConfiguration);

      Currencies = currenciesProvider.GetCurrencies(CurrentNetwork);
    }
  }
}
