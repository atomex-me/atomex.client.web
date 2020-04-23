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
using Atomex.Blockchain.Abstract;
using Atomex.Abstract;
using Atomex.Subsystems.Abstract;
using Atomex.MarketData;

namespace atomex_frontend.Storages
{
  public class AccountStorage
  {
    public AccountStorage(HttpClient httpClient,
      ILocalStorageService localStorage,
      IJSRuntime jSRuntime)
    {
      this.httpClient = httpClient;
      this.localStorage = localStorage;
      this.jSRuntime = jSRuntime;

      this.InitializeAtomexConfigs();
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

    public Account Account { get; set; }
    public IAtomexApp AtomexApp { get; set; }
    public IAtomexClient Terminal { get; set; }
    public static ICurrencies Currencies { get; set; }
    public BitfinexQuotesProvider QuotesProvider { get; set; }

    public event Action InitializeCallback;
    private void CallInitialize()
    {
      InitializeCallback?.Invoke();
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

    public async Task SaveWallet(HdWallet wallet, SecureString pass, string walletName)
    {
      IList<string> availableWalletsList = await GetAvailableWallets();
      availableWalletsList.Add(walletName);

      var newSerializedWalletNames = JsonConvert.SerializeObject(availableWalletsList.ToArray<string>());

      await wallet.EncryptAsync(pass);

      wallet.SaveToFile($"/{walletName}.wallet", pass);
      byte[] walletBytes = File.ReadAllBytes($"/{walletName}.wallet");
      string walletBase64 = Convert.ToBase64String(walletBytes);

      await localStorage.SetItemAsync("available_wallets", newSerializedWalletNames);
      await localStorage.SetItemAsync($"{walletName}.wallet", walletBase64);

    }

    public async Task ConnectToWallet(string WalletName, SecureString Password)
    {
      _password = Password;
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

      CurrentWalletName = WalletName;
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

      var symbolsProvider = new SymbolsProvider(symbolsConfiguration);

      ADR = new AccountDataRepository(currenciesProvider.GetCurrencies(Network.TestNet), initialData: data);
      ADR.SaveDataCallback += SaveDataCallback;

      Account = new Account(
        HdWallet.LoadFromFile($"/{CurrentWalletName}.wallet", _password),
        _password,
        ADR,
        currenciesProvider,
        symbolsProvider
      );

      Terminal = new WebSocketAtomexClient(configuration, Account);

      if (AtomexApp != null)
      {
        AtomexApp.UseTerminal(Terminal, restart: true);
        Console.WriteLine($"Restarting: switched to Account with {CurrentWalletName} wallet");
      }
      else
      {
        AtomexApp = new AtomexApp()
            .UseCurrenciesProvider(currenciesProvider)
            .UseSymbolsProvider(symbolsProvider)
            // .UseCurrenciesUpdater(new CurrenciesUpdater(currenciesProvider))
            .UseQuotesProvider(new BitfinexQuotesProvider(
                currencies: currenciesProvider.GetCurrencies(Network.TestNet),
                baseCurrency: BitfinexQuotesProvider.Usd))
            .UseTerminal(Terminal);
        Console.WriteLine($"Starting Atomex app with {CurrentWalletName} wallet with data {data.Length}");
        AtomexApp.Start();
      }

      if (AtomexApp.HasQuotesProvider)
      {
        AtomexApp.QuotesProvider.QuotesUpdated += OnQuotesUpdatedEventHandler;
      }
      AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
      AtomexApp.Account.BalanceUpdated += OnBalanceChangedEventHandler;
      AtomexApp.Terminal.ServiceConnected += OnTerminalServiceStateChangedEventHandler;

      this.CallInitialize();
    }

    private void SaveDataCallback(AccountDataRepository.AvailableDataType type, string key, string value)
    {

      // if (type == AccountDataRepository.AvailableDataType.Transaction)
      // {
      //   Console.WriteLine($"W3riting to JAvascript tx with ID {key}");
      // }
      jSRuntime.InvokeAsync<string>("saveData", new string[] { type.ToName(), CurrentWalletName, key, value });
    }

    private void OnTerminalServiceStateChangedEventHandler(object sender, TerminalServiceEventArgs args)
    {
      if (!(sender is IAtomexClient terminal))
        return;

      bool IsExchangeConnected = terminal.IsServiceConnected(TerminalService.Exchange);
      bool IsMarketDataConnected = terminal.IsServiceConnected(TerminalService.MarketData);

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

      Currencies = currenciesProvider.GetCurrencies(Network.TestNet); // todo: Make actual Net;
    }
  }
}
