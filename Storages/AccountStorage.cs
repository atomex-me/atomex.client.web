using System;
using System.Linq;
using System.Net.Http;
using Atomex.Subsystems;
using Atomex.Wallet;
using Microsoft.AspNetCore.Components;
using Blazored.LocalStorage;
using Microsoft.Extensions.Configuration;
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


namespace atomex_frontend.Storages
{
  public class AccountStorage
  {
    public AccountStorage(HttpClient httpClient,
        ILocalStorageService localStorage)
    {
      this.httpClient = httpClient;
      this.localStorage = localStorage;

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

    public Account Account { get; set; }
    public IAtomexApp AtomexApp { get; set; }
    public Terminal Terminal { get; set; }
    public static ICurrencies Currencies { get; set; }
    public BitfinexQuotesProvider QuotesProvider { get; set; }

    private CurrenciesProvider currenciesProvider;
    private IConfiguration currenciesConfiguration;
    private IConfiguration symbolsConfiguration;
    private Assembly coreAssembly;
    private HttpClient httpClient;
    private ILocalStorageService localStorage;

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
      var confJson = await httpClient.GetJsonAsync<dynamic>("conf/configuration.json");
      Stream confStream = Common.Helper.GenerateStreamFromString(confJson.ToString());

      IConfiguration configuration = new ConfigurationBuilder()
          .AddJsonStream(confStream)
          .Build();
      var symbolsProvider = new SymbolsProvider(symbolsConfiguration, currenciesProvider);

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

      Account = new Account(
        HdWallet.LoadFromFile($"/{WalletName}.wallet", Password),
        Password,
        new AccountDataRepository(),
        currenciesProvider,
        symbolsProvider
      );

      Terminal = new Terminal(configuration, Account);

      if (AtomexApp != null)
      {
        AtomexApp.UseTerminal(Terminal, restart: true);
        Console.WriteLine($"Restarting: switched to Account with {WalletName} wallet");
      }
      else
      {
        AtomexApp = new AtomexApp()
            .UseCurrenciesProvider(currenciesProvider)
            .UseSymbolsProvider(symbolsProvider)
            .UseQuotesProvider(new BitfinexQuotesProvider(
                currencies: currenciesProvider.GetCurrencies(Network.TestNet),
                baseCurrency: BitfinexQuotesProvider.Usd))
            .UseTerminal(Terminal);
        Console.WriteLine($"Starting Atomex app with {WalletName} wallet");
        AtomexApp.Start();
      }

      if (AtomexApp.HasQuotesProvider)
      {
        Console.WriteLine("Subscribing to QuotesUpdated Event.");
        AtomexApp.QuotesProvider.QuotesUpdated += OnQuotesUpdatedEventHandler;
      }
      Console.WriteLine("Subscribing to New Trans Event Event.");
      AtomexApp.Account.UnconfirmedTransactionAdded += OnUnconfirmedTransactionAddedEventHandler;
      Console.WriteLine("Subscribing to BalanceUpdated event.");
      AtomexApp.Account.BalanceUpdated += OnBalanceChangedEventHandler;
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
      Console.WriteLine($"BALANCE UPDATED ON {args.Currency.Name}");
    }

    private void OnUnconfirmedTransactionAddedEventHandler(object sender, TransactionEventArgs e)
    {
      Console.WriteLine("New trans!!!!!!!!!!!!!");
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
