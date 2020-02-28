using System;
using System.Linq;
using System.Net.Http;
using Atomex.Subsystems;
using Atomex.Wallet;
using Microsoft.AspNetCore.Components;
using Blazored.LocalStorage;
using Microsoft.Extensions.Configuration;
using System.IO;
using Atomex.Common.Configuration;
using Atomex;
using Atomex.MarketData.Bitfinex;
using Atomex.Core;
using System.Security;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace atomex_frontend.Storages
{
  public class AccountStorage
  {
    public AccountStorage(HttpClient httpClient,
        ILocalStorageService localStorage)
    {
      this.httpClient = httpClient;
      this.localStorage = localStorage;
    }

    private HttpClient httpClient;
    private ILocalStorageService localStorage;

    public Account Account { get; set; }
    public IAtomexApp AtomexApp { get; set; }
    public Terminal Terminal { get; set; }


    private Stream GenerateStreamFromString(string s)
    {
      var stream = new MemoryStream();
      var writer = new StreamWriter(stream);
      writer.Write(s);
      writer.Flush();
      stream.Position = 0;
      return stream;
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
      //if (Account != null && Terminal != null) {

      //}

      var coreAssembly = AppDomain.CurrentDomain
          .GetAssemblies()
          .FirstOrDefault(a => a.GetName().Name == "Atomex.Client.Core");

      var currenciesConfiguration = new ConfigurationBuilder()
          .AddEmbeddedJsonFile(coreAssembly, "currencies.json")
          .Build();

      var symbolsConfiguration = new ConfigurationBuilder()
          .AddEmbeddedJsonFile(coreAssembly, "symbols.json")
          .Build();

      var confJson = await httpClient.GetJsonAsync<dynamic>("conf/configuration.json");
      Stream confStream = GenerateStreamFromString(confJson.ToString());

      var configuration = new ConfigurationBuilder()
          .AddJsonStream(confStream)
          .Build();

      var currenciesProvider = new CurrenciesProvider(currenciesConfiguration);
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
      // Account = Account.LoadFromFile($"/{WalletName}.wallet", Password, currenciesProvider, symbolsProvider);

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

    }
  }
}
